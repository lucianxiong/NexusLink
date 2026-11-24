using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ServiceLib.Services;

/// <summary>
/// 畅连内置节点管家（NexusLink Keeper）：
/// 换血 = mihomo 分层池管线：抓全球公益池 → TCP 预筛 → mihomo 真测速(Google 204) →
///   隔离晋升（连续可用且 &lt;800ms）进 Primary-Pool（高质量）→ 其余进 Backup-Pool（备用）；
///   分层结果落盘为 quality.yaml / backup.yaml（仅留档，mihomo 兜底接管已移除）。
///   Top3 高质量节点导入畅连主池（sing-box 原生策略组）。
/// 守卫 = 启动防抖（核心就绪+宽限期内不判定）+ 滑动窗口迟滞（连续 N 次 bad 才动作）+
///   池内补位（Topup：切换到池内最快节点，不全量换血）+
///   告警（备用池也全垃圾时保留现有连接并告警）。
/// </summary>
public sealed class KeeperService
{
    public const string SubId = "nexuslink-pool";
    public const string GroupIndexId = "nexuslink-auto-group";
    private const string GroupRemarks = "畅连·自动优选";

    private static readonly Lazy<KeeperService> _instance = new(() => new());
    public static KeeperService Instance => _instance.Value;

    private Config? _config;
    private Func<Task>? _reloadFunc;
    private Timer? _timer;
    private Timer? _statusTimer;
    private CancellationTokenSource? _wsCts;
    private long _upSpeed;
    private long _downSpeed;
    private DateTime _lastAutoRefresh = DateTime.MinValue;
    private DateTime _lastRefreshAt = DateTime.Now;
    private DateTime _lastTopupAt = DateTime.MinValue;

    private DateTime _startedAt = DateTime.MinValue;
    private int _running;                    // 1 = refresh in progress
    private string _lastSlot = string.Empty;

    // ---- 守卫 v2 状态 ----
    private readonly Queue<(bool ok, int latency)> _probeWindow = new(); // 最近探测滑动窗口

    private bool _coreApiOk;                 // 当前核心是否暴露 clash API；xray 天生没有，缺失属正常，不能算"核心死亡"

    private readonly HttpClient _subscriptionClient = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = async (context, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                {
                    throw new HttpRequestException("Subscription source resolved to a restricted address.");
                }
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2 * 1024 * 1024 };

    private readonly HttpClient _httpDirect;
    private HttpClient? _httpProxy;
    private int _proxyClientPort;

    public KeeperState State { get; private set; } = new();

    public bool IsRunning => Volatile.Read(ref _running) == 1;
    public double UpSpeed => Interlocked.Read(ref _upSpeed);
    public double DownSpeed => Interlocked.Read(ref _downSpeed);

    private KeeperService()
    {
        _httpDirect = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false, // 抓池永远直连：系统代理可能指向自家死节点，走代理必挂
        })
        { Timeout = TimeSpan.FromSeconds(15) };

    }

    // ------------------------------------------------------------------ paths
    private static string MihomoExe => Path.Combine(AppContext.BaseDirectory, "bin", "mihomo", "mihomo.exe");
    private static string WorkDir => Path.Combine(AppContext.BaseDirectory, "binConfigs", "keeper-mihomo");
    private const int TestPort = 10818;      // 换血测速 harness 入站
    private const int TestApiPort = 10819;   // 换血测速 / 兜底接管 clash API
    private const string TestUrl = "https://www.gstatic.com/generate_204";
    private const string SecondaryTestUrl = "https://cp.cloudflare.com/generate_204";
    public string RefreshStatus { get; private set; } = "等待刷新";

    // ------------------------------------------------------------------ entry
    public async Task StartAsync(Config config, Func<Task> reloadFunc)
    {
        _config = config;
        _reloadFunc = reloadFunc;
        LoadState();
        await EnsureGroupAsync();
        _timer = new Timer(async _ => await TimerTickAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));
        _statusTimer = new Timer(async _ => await PollStatusAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));
        _lastRefreshAt = DateTime.TryParse(State.LastRefresh, out var lr) ? lr : DateTime.Now;
        _startedAt = DateTime.Now;
        _probeWindow.Clear();

        _coreApiOk = false;
        _wsCts = new CancellationTokenSource();
        _ = Task.Run(() => SpeedLoopAsync());
        if (State.Enabled)
        {
            var items = await AppManager.Instance.ProfileItems(SubId);
            if (items == null || items.Count < 3)
            {
                _ = RefreshAsync();
            }
        }
        Logging.SaveLog($"KeeperService started (grace={State.StartupGraceSec}s guardN={State.GuardConsecutive} guardLat={State.GuardLatencyMs}ms)");
    }

    private async Task TimerTickAsync()
    {
        try
        {
            if (!State.Enabled)
            {
                return;
            }
            // 池健康盘点：每 TopupIntervalHours（降级检查+缺员补位，不动好节点）
            if ((DateTime.Now - _lastTopupAt).TotalHours >= State.TopupIntervalHours)
            {
                _lastTopupAt = DateTime.Now;
                if (!State.Online && !IsRunning)
                {
                    await HealthSweepAsync();
                }
            }
            if (!State.DailyRefresh)
            {
                return;
            }
            // 定时增量换血：每 RefreshIntervalHours（合并新达标节点，不盲目替换）
            if ((DateTime.Now - _lastRefreshAt).TotalHours >= State.RefreshIntervalHours)
            {
                if (!IsRunning)
                {
                    await RefreshAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService timer", ex);
        }
    }

    // ------------------------------------------------------------------ live status (which network am I on)
    private int _pollRunning;

    private async Task PollStatusAsync()
    {
        if (Interlocked.CompareExchange(ref _pollRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await PollStatusCoreAsync();
        }
        finally
        {
            Volatile.Write(ref _pollRunning, 0);
        }
    }

    private async Task PollStatusCoreAsync()
    {
        if (_config?.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange
            && SysProxyHandler.HasExternalWindowsProxy(_config))
        {
            _config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged;
            State.Online = false;
            await ConfigHandler.SaveConfig(_config);
            NoticeManager.Instance.SendMessage("畅连已让出系统代理");
            return;
        }
        try
        {
            var port = AppManager.Instance.StatePort2;
            var json = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{port}/proxies");
            _coreApiOk = true;
            var proxies = JsonUtils.ParseJson(json)?["proxies"];
            var node = string.Empty;
            var delay = -1;
            if (proxies != null)
            {
                var now = proxies["proxy"]?["now"]?.GetValue<string>();
                if (now.IsNotEmpty() && proxies[now] != null)
                {
                    var inner = proxies[now]?["now"]?.GetValue<string>();
                    node = inner.IsNullOrEmpty() ? now! : inner;
                    var hist = proxies[node]?["history"] as JsonArray;
                    if (hist is { Count: > 0 })
                    {
                        var d = hist[hist.Count - 1]?["delay"]?.GetValue<int>();
                        if (d != null)
                        {
                            delay = d.Value;
                        }
                    }
                }
            }

            State.ExitNode = node;
            State.LatencyMs = delay;
            State.ExitIp = State.ExitCountry = State.ExitCity = State.ExitIsp = string.Empty;
            State.PollAt = DateTime.Now.ToString("HH:mm:ss");
        }

        catch
        {
            // xray 没有 clash API，探不到是能力缺失而非核心死亡；只降级为"拿不到节点信息"，不影响在线判定
            _coreApiOk = false;
            State.ExitNode = string.Empty;
            State.LatencyMs = -1;
        }

        State.Online = await VerifyProxyAsync();
        State.PollAt = DateTime.Now.ToString("HH:mm:ss");
        if ((DateTime.Now - _startedAt).TotalSeconds >= State.StartupGraceSec)
        {
            EnqueueProbe(State.Online, State.LatencyMs);
            await AutoGuardAsync();
        }
    }

    private void EnqueueProbe(bool ok, int latency)
    {
        lock (_probeWindow)
        {
            _probeWindow.Enqueue((ok, latency));
            while (_probeWindow.Count > State.GuardConsecutive * 2)
            {
                _probeWindow.Dequeue();
            }
        }
    }

    private List<(bool ok, int latency)> ProbeSnapshot()
    {
        lock (_probeWindow)
        {
            return [.. _probeWindow];
        }
    }

    // ------------------------------------------------------------------ guard v2
    // 迟滞判定：滑动窗口最近 GuardConsecutive 次探测全部 bad（失败 或 latency>GuardLatencyMs）才允许动作。
    // 启动竞态由 StartupGraceSec 宽限承担（宽限期内不探测不判定）；宽限后连续 bad 即可动作（断网自愈不需要基线）。
    // 动作顺序：核心失联 → mihomo 分层池兜底接管；否则池内补位（切最快节点）；补位无解 → 全量换血；
    // 换血也失败 → 保留现有连接并告警。
    private async Task AutoGuardAsync()
    {
        try
        {
            if (!State.Enabled || !State.AutoSwitch || IsRunning || _config?.SystemProxyItem.SysProxyType != ESysProxyType.ForcedChange)
            {
                return;
            }
            var recent = ProbeSnapshot();
            if (recent.Count < State.GuardConsecutive)
            {
                return;
            }
            var window = recent.TakeLast(State.GuardConsecutive).ToList();
            var allBad = window.All(p => !p.ok || p.latency > State.GuardLatencyMs);
            if (!allBad)
            {
                return;
            }
            if ((DateTime.Now - _lastAutoRefresh).TotalMinutes < 30)
            {
                return;
            }
            _lastAutoRefresh = DateTime.Now;
            var lastLat = window[^1].latency;
            Logging.SaveLog($"KeeperService guard: {State.GuardConsecutive} consecutive bad probes (latency={lastLat}ms) → action");

            // 1) 池内补位：有 clash API 就按真测速结果切；xray 没有 clash API，改按 TCP 时延挑一条换上去
            var switched = _coreApiOk
                ? await TopupFromPoolAsync()
                : await SwitchToBestPoolNodeAsync();
            if (switched)
            {
                NoticeManager.Instance.SendMessage("畅连管家：线路质量连续劣化，已切到池内最快节点（未换血）");
                return;
            }

            // 2) 只是"慢但能用"（每轮真实验证都通过）→ 保留池子，不为延迟去清空可用节点
            if (window.All(p => p.ok))
            {
                Logging.SaveLog($"KeeperService guard: line slow (latency={lastLat}ms > {State.GuardLatencyMs}ms) but verified alive — keep pool, no refresh");
                return;
            }

            // 3) 确实断流 → 全量换血（purge：先清后导，达标新池直接接管）
            var ok = await RefreshAsync();
            if (!ok)
            {
                NoticeManager.Instance.SendMessage("畅连管家：⚠️ 线路劣化且备用池无可用节点，已保留当前连接（告警）");
                Logging.SaveLog("KeeperService guard: ALL POOLS BAD — keep current connection (ALARM)");
            }
        }
        catch { }
    }

    // 无 clash API 时的补位：按 TCP 连接时延在池子里挑最快的几条，逐条切换并用真实代理验证
    private async Task<bool> SwitchToBestPoolNodeAsync()
    {
        if (_config == null)
        {
            return false;
        }
        var items = await AppManager.Instance.ProfileItems(SubId) ?? [];
        var candidates = items.Where(t => t.IndexId != _config.IndexId && t.Address.IsNotEmpty() && t.Port > 0).ToList();
        if (candidates.Count == 0)
        {
            Logging.SaveLog("KeeperService topup: pool has no candidate to switch");
            return false;
        }

        var measured = new ConcurrentBag<(ProfileItem item, long ms)>();
        var sem = new SemaphoreSlim(8);
        var tasks = candidates.Select(async it =>
        {
            await sem.WaitAsync();
            var sw = Stopwatch.StartNew();
            try
            {
                using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
                await socket.ConnectAsync(it.Address, it.Port, timeout.Token);
                measured.Add((it, sw.ElapsedMilliseconds));
            }
            catch { }
            finally
            {
                sem.Release();
            }
        });
        await Task.WhenAll(tasks);

        foreach (var (item, ms) in measured.OrderBy(t => t.ms).Take(3))
        {
            if (await SwitchAndVerifyAsync(item.IndexId))
            {
                Logging.SaveLog($"KeeperService topup: switched to '{item.Remarks}' ({ms}ms tcp)");
                return true;
            }
        }
        Logging.SaveLog("KeeperService topup: no pool candidate passed verification");
        return false;
    }

    // 池内补位：经 clash API 对策略组成员真测速，切到 delay 最小且 < GuardLatencyMs 的节点
    private async Task<bool> TopupFromPoolAsync()
    {
        try
        {
            if (_config == null)
            {
                return false;
            }
            var port = AppManager.Instance.StatePort2;
            var testUrl = Uri.EscapeDataString(TestUrl);
            Dictionary<string, int> delays = [];
            try
            {
                var json = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{port}/group/proxy/delay?url={testUrl}&timeout=5000");
                delays = ParseDelayMap(json);
            }
            catch
            {
                // 回退：逐节点并行测
                var listJson = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{port}/proxies/proxy");
                var members = JsonUtils.ParseJson(listJson)?["proxies"]?["proxy"]?["all"] as JsonArray;
                if (members == null || members.Count == 0)
                {
                    return false;
                }
                var sem = new SemaphoreSlim(4);
                var bag = new ConcurrentBag<KeyValuePair<string, int>>();
                var tasks = members.OfType<JsonValue>().Select(async mv =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        var name = Uri.EscapeDataString(mv.GetValue<string>());
                        var r = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{port}/proxies/{name}/delay?url={testUrl}&timeout=5000");
                        var d = JsonUtils.ParseJson(r)?["delay"]?.GetValue<int>();
                        if (d != null)
                        {
                            bag.Add(new(mv.GetValue<string>(), d.Value));
                        }
                    }
                    catch { }
                    finally
                    {
                        sem.Release();
                    }
                });
                await Task.WhenAll(tasks);
                delays = bag.ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            var best = delays.Where(kv => kv.Value > 0 && kv.Value < State.GuardLatencyMs).OrderBy(kv => kv.Value).FirstOrDefault();
            if (best.Key == null || best.Value == default)
            {
                Logging.SaveLog($"KeeperService topup: no alive member under {State.GuardLatencyMs}ms (checked {delays.Count})");
                return false;
            }

            var items = await AppManager.Instance.ProfileItems(SubId) ?? [];
            var idx = MatchIndexId(best.Key, items);
            if (idx.IsNullOrEmpty())
            {
                Logging.SaveLog($"KeeperService topup: cannot map clash node '{best.Key}' to ProfileItem");
                return false;
            }
            if (!await SwitchAndVerifyAsync(idx))
            {
                return false;
            }
            Logging.SaveLog($"KeeperService topup: switched to '{best.Key}' ({best.Value}ms)");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService topup", ex);
            return false;
        }
    }

    // 池健康盘点（每 TopupIntervalHours）：全组真测速 → 连续劣化降级出池 → 缺员从质量备选库补位
    private async Task HealthSweepAsync()
    {
        try
        {
            if (!State.Enabled || _config == null || (DateTime.Now - _startedAt).TotalSeconds < State.StartupGraceSec)
            {
                return;
            }
            var port = AppManager.Instance.StatePort2;
            var testUrl = Uri.EscapeDataString(TestUrl);
            Dictionary<string, int> delays;
            try
            {
                var json = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{port}/group/proxy/delay?url={testUrl}&timeout=5000");
                delays = ParseDelayMap(json);
            }
            catch
            {
                return; // 核心不可用，交给守卫处理
            }
            if (delays.Count == 0)
            {
                return;
            }
            var items = await AppManager.Instance.ProfileItems(SubId) ?? [];
            // 降级：连续 DegradeRounds 轮真测速 > DegradeLatencyMs → 出池
            var toRemove = new List<ProfileItem>();
            foreach (var it in items)
            {
                var d = delays.Where(kv => kv.Key == it.Remarks || kv.Key.EndsWith(it.Remarks, StringComparison.Ordinal)).Select(kv => (int?)kv.Value).FirstOrDefault();
                if (d == null)
                {
                    continue; // 本轮组测速未覆盖
                }
                if (d > State.DegradeLatencyMs)
                {
                    State.DegradeMap[it.Remarks] = State.DegradeMap.GetValueOrDefault(it.Remarks) + 1;
                    if (State.DegradeMap[it.Remarks] >= State.DegradeRounds)
                    {
                        toRemove.Add(it);
                    }
                }
                else
                {
                    State.DegradeMap.Remove(it.Remarks);
                }
            }
            if (toRemove.Count > 0)
            {
                await ConfigHandler.RemoveServers(_config, toRemove);
                foreach (var it in toRemove)
                {
                    State.DegradeMap.Remove(it.Remarks);
                    Logging.SaveLog($"KeeperService sweep: degraded out '{it.Remarks}' (>{State.DegradeLatencyMs}ms x{State.DegradeRounds})");
                }
                items = items.Where(t => !toRemove.Contains(t)).ToList();
                _ = _reloadFunc?.Invoke();
            }
            // 补位：主池 < PoolMinMain → 从 QualityLinks（达标链接库）挑未入池的补齐
            if (items.Count < State.PoolMinMain && State.QualityLinks.Count > 0)
            {
                var inPool = items.Select(t => $"{t.Address.ToLowerInvariant()}:{t.Port}").ToHashSet();
                var candidates = new List<string>();
                foreach (var link in State.QualityLinks)
                {
                    if (candidates.Count >= State.PoolMinMain - items.Count)
                    {
                        break;
                    }
                    var hp = ParseHostPort(link);
                    if (ParsePoolCandidate(link) == null || hp == null || inPool.Contains($"{hp.Value.host.ToLowerInvariant()}:{hp.Value.port}"))
                    {
                        continue;
                    }
                    candidates.Add(link);
                }
                if (candidates.Count > 0)
                {
                    await EnsureSubItemAsync();
                    var cnt = await ConfigHandler.AddBatchServers(_config, string.Join(Environment.NewLine, candidates), SubId, false);
                    if (cnt > 0)
                    {
                        items = await AppManager.Instance.ProfileItems(SubId) ?? items;
                        Logging.SaveLog($"KeeperService sweep: topped up {cnt} nodes (pool now {items.Count})");
                        _ = _reloadFunc?.Invoke();
                    }
                }
            }
            State.Nodes = BuildNodes(items);
            State.LastAlive = items.Count;
            SaveState();
            NoticeManager.Instance.SendMessage($"畅连管家：池子体检完成——{items.Count} 条在池，本轮降级 {toRemove.Count} 条");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService sweep", ex);
        }
    }

    // 地区识别（节点名文本关键词 → 地区码；未识别归 OTHER 不设上限）
    private static readonly (string key, string region)[] RegionMap =
    [
        ("香港", "HK"), ("HK", "HK"), ("台湾", "TW"), ("TW", "TW"), ("台北", "TW"),
        ("日本", "JP"), ("JP", "JP"), ("新加坡", "SG"), ("SG", "SG"), ("美国", "US"), ("USA", "US"), ("US", "US"),
        ("德国", "DE"), ("DE", "DE"), ("英国", "GB"), ("GB", "GB"), ("法国", "FR"), ("FR", "FR"),
        ("韩国", "KR"), ("KR", "KR"), ("土耳其", "TR"), ("TR", "TR"), ("加拿大", "CA"), ("CA", "CA"),
        ("意大利", "IT"), ("IT", "IT"), ("印度", "IN"), ("IN", "IN"), ("俄罗斯", "RU"), ("RU", "RU"),
        ("巴西", "BR"), ("BR", "BR"), ("荷兰", "NL"), ("NL", "NL"), ("波兰", "PL"), ("PL", "PL"),
        ("芬兰", "FI"), ("FI", "FI"), ("澳大利亚", "AU"), ("AU", "AU"), ("马来", "MY"), ("MY", "MY"),
        ("泰国", "TH"), ("TH", "TH"), ("越南", "VN"), ("VN", "VN"), ("菲律宾", "PH"), ("PH", "PH"),
    ];

    private static string RegionOf(string name)
    {
        var s = name ?? string.Empty;
        foreach (var (key, region) in RegionMap)
        {
            if (s.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return region;
            }
        }
        return "OTHER";
    }

    private static string GetQualityKey(ProfileItem profile)
    {
        var identity = FmtHandler.GetShareUri(profile);
        if (string.IsNullOrEmpty(identity))
        {
            return string.Empty;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Split('#', 2)[0])));
    }

    private static Dictionary<string, int> ParseDelayMap(string json)
    {
        var map = new Dictionary<string, int>();
        var root = JsonUtils.ParseJson(json);
        if (root is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Value is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<int>(out var d))
                {
                    map[kv.Key] = d;
                }
            }
        }
        return map;
    }

    // clash outbound tag（proxy-N-备注）→ ProfileItem.IndexId
    private static string MatchIndexId(string clashName, List<ProfileItem> items)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(clashName, @"^proxy-\d+-", "");
        foreach (var it in items)
        {
            if (it.Remarks == clashName || it.Remarks == stripped || clashName.EndsWith(it.Remarks, StringComparison.Ordinal))
            {
                return it.IndexId;
            }
        }
        return string.Empty;
    }

    // ------------------------------------------------------------------ refresh cycle (mihomo 分层池管线)
    public async Task<bool> RefreshAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return false; // already running
        }
        if (_config == null)
        {
            Interlocked.Exchange(ref _running, 0);
            return false;
        }
        try
        {
            NoticeManager.Instance.SendMessage("畅连管家：开始抓取全球免费节点池…");
            // 1. fetch pools
            var links = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sourceByHostPort = new Dictionary<string, string>(StringComparer.Ordinal); // 节点 host:port → 来源池短名
            var rejectedReasons = new Dictionary<string, int>(); // 被安全闸拦下的节点按原因计数
            var poolUrls = State.Pools.Distinct().Take(20).ToList();
            RefreshStatus = "正在更新节点来源";
            var catalogs = await Task.WhenAll(poolUrls.Select(async url =>
            {
                var body = await FetchPoolAsync(url);
                return body == null ? [] : ExtractLinks(body).ToArray();
            }));
            var poolsOk = catalogs.Count(catalog => catalog.Length > 0);
            State.ReachableSources = poolsOk;
            var maxLength = catalogs.Select(catalog => catalog.Length).DefaultIfEmpty().Max();
            for (var i = 0; i < maxLength && links.Count < State.MaxCandidates; i++)
            {
                for (var ci = 0; ci < catalogs.Length; ci++)
                {
                    var catalog = catalogs[ci];
                    if (i >= catalog.Length)
                    {
                        continue;
                    }
                    var link = catalog[i];
                    var proxy = ParsePoolCandidate(link);
                    if (proxy == null)
                    {
                        CountSecurityRejection(link, rejectedReasons);
                        continue;
                    }
                    var identity = JsonSerializer.Serialize(proxy.Where(entry => entry.Key != "name").OrderBy(entry => entry.Key));
                    if (seen.Add(identity))
                    {
                        links.Add(link);
                        var sourceHp = ParseHostPort(link);
                        if (sourceHp != null)
                        {
                            sourceByHostPort[$"{sourceHp.Value.host.ToLowerInvariant()}:{sourceHp.Value.port}"] = PoolShortName(poolUrls[ci]);
                        }
                    }
                    if (links.Count >= State.MaxCandidates)
                    {
                        break;
                    }
                }
            }
            var rejectedTotal = rejectedReasons.Values.Sum();
            var rejectedSummary = string.Join("、", rejectedReasons.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
            State.SecurityAudit = new KeeperSecurityAudit
            {
                At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Candidates = links.Count,
                Rejected = rejectedTotal,
                Reasons = rejectedSummary,
            };
            Logging.SaveLog($"KeeperService security audit: poolsOk={poolsOk}/{poolUrls.Count} candidates={links.Count} rejectedBySecurity={rejectedTotal}{(rejectedTotal > 0 ? $" ({rejectedSummary})" : string.Empty)} minSecurity={State.MinSecurity}");
            if (links.Count < State.MinAlive)
            {
                Logging.SaveLog($"KeeperService refresh FAILED: poolsOk={poolsOk}/{State.Pools.Count} links={links.Count} (insufficient)");
                NoticeManager.Instance.SendMessage($"畅连管家：本轮池子未取到足够节点（{links.Count}），保持原线路不变");
                return false;
            }

            // 2. TCP prefilter (parallel, 3s) — 只做廉价的初筛，绝不做质量判定
            RefreshStatus = rejectedTotal > 0
                ? $"正在检查 {links.Count} 个节点的端口 · 已拦截 {rejectedTotal} 个不安全节点"
                : $"正在检查 {links.Count} 个节点的端口";
            var alive = new ConcurrentBag<(int rtt, string link)>();
            using var sem = new SemaphoreSlim(16);
            var tasks = links.Take(State.MaxCandidates).Select(async link =>
            {
                await sem.WaitAsync();
                try
                {
                    var hp = ParseHostPort(link)!;
                    var rtt = await TcpPingAsync(hp.Value.host, hp.Value.port);
                    if (rtt >= 0)
                    {
                        alive.Add((rtt, link));
                    }
                }
                catch { }
                finally
                {
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks);
            var candidates = alive.OrderBy(t => t.rtt).Take(60).ToList();
            if (candidates.Count < State.MinAlive)
            {
                Logging.SaveLog($"KeeperService refresh FAILED: links={links.Count} tcpAlive={candidates.Count} poolsOk={poolsOk} (too few alive)");
                NoticeManager.Instance.SendMessage($"畅连管家：实测仅 {candidates.Count} 个节点 TCP 可达（需 {State.MinAlive}），保持原线路不变");
                return false;
            }

            // 3. mihomo 真测速 harness：TCP 活 ≠ 能翻墙，必须对 Google 204 实测
            Directory.CreateDirectory(WorkDir);
            var parsed = new List<(string link, Dictionary<string, object?> proxy)>();
            var nameSeen = new HashSet<string>();
            foreach (var (rtt, link) in candidates)
            {
                var p = ParsePoolCandidate(link);
                if (p == null)
                {
                    continue;
                }
                var nm = p.TryGetValue("name", out var n0) ? (string)n0! : "node";
                var uniq = nm;
                var k = 2;
                while (!nameSeen.Add(uniq))
                {
                    uniq = $"{nm} #{k++}";
                }
                p["name"] = uniq;
                parsed.Add((link, p));
            }
            if (parsed.Count < State.MinAlive)
            {
                Logging.SaveLog($"KeeperService refresh FAILED: parse ok={parsed.Count} (too few)");
                return false;
            }
            await File.WriteAllTextAsync(Path.Combine(WorkDir, "candidates.yaml"), BuildClashYaml(parsed.Select(t => t.proxy).ToList()));
            var hc60 = $"{{enable: true, url: '{TestUrl}', interval: 60, lazy: false}}";
            var testCfg = string.Join("\n", new List<string>
            {
                $"mixed-port: {TestPort}",
                $"external-controller: 127.0.0.1:{TestApiPort}",
                "allow-lan: false",
                "bind-address: 127.0.0.1",
                "mode: rule",
                "log-level: warning",
                "proxy-providers:",
                "  backup:",
                "    type: file",
                "    path: ./candidates.yaml",
                $"    health-check: {hc60}",
                "proxy-groups:",
                $"  - {{name: TEST, type: url-test, use: [backup], url: '{TestUrl}', interval: 60, tolerance: 100, lazy: false}}",
                "rules:",
                "  - MATCH,TEST",
            });
            RefreshStatus = $"正在准备测速 · {parsed.Count} 个安全候选";
            await File.WriteAllTextAsync(Path.Combine(WorkDir, "config-test.yaml"), testCfg);

            var testProc = StartMihomo($"-d \"{WorkDir}\" -f config-test.yaml");
            if (testProc == null)
            {
                Logging.SaveLog("KeeperService refresh FAILED: mihomo harness cannot start");
                NoticeManager.Instance.SendMessage("畅连管家：测速引擎启动失败，保持原线路不变");
                return false;
            }
            Dictionary<string, int> testMap;
            try
            {
                var ready = false;
                for (var i = 0; i < 20; i++)
                {
                    await Task.Delay(1000);
                    try
                    {
                        _ = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{TestApiPort}/proxies");
                        ready = true;
                        break;
                    }
                    catch { }
                }
                if (!ready)
                {
                    Logging.SaveLog("KeeperService refresh FAILED: harness api not ready");
                    return false;
                }
                // 等首轮 health-check 出结果，再触发组级真测速（对 Google）
                await Task.Delay(5000);
                var delayJson = string.Empty;
                for (var i = 0; i < 3; i++)
                {
                    try
                    {
                        delayJson = await _httpDirect.GetStringAsync($"http://{Global.Loopback}:{TestApiPort}/group/TEST/delay?url={Uri.EscapeDataString(TestUrl)}&timeout=5000");
                        break;
                    }
                    catch
                    {
                        await Task.Delay(2000);
                    }
                }
                RefreshStatus = $"正在验证 {parsed.Count} 个节点的第二个测速目标";
                var secondary = ParseDelayMap(await _httpDirect.GetStringAsync(
                    $"http://{Global.Loopback}:{TestApiPort}/group/TEST/delay?url={Uri.EscapeDataString(SecondaryTestUrl)}&timeout=5000"));
                testMap = ParseDelayMap(delayJson)
                    .Where(pair => pair.Value > 0 && secondary.GetValueOrDefault(pair.Key) > 0)
                    .ToDictionary(pair => pair.Key, pair => Math.Max(pair.Value, secondary[pair.Key]));
            }
            finally
            {
                KillProc(testProc);
            }
            var testedAt = DateTime.Now;
            var qualityHistory = new Dictionary<string, KeeperQuality>(State.QualityHistory);
            foreach (var (link, proxy) in parsed)
            {
                if (FmtHandler.ResolveConfig(link, out _) is not { } profile)
                {
                    continue;
                }
                var key = GetQualityKey(profile);
                var measured = testMap.GetValueOrDefault((string)proxy["name"]!);
                var previous = qualityHistory.GetValueOrDefault(key);
                var samples = previous?.Samples ?? [];
                qualityHistory[key] = new KeeperQuality
                {
                    Samples = samples.Append(measured).TakeLast(20).ToList(),
                    TestedAt = testedAt,
                };
            }
            State.QualityHistory = qualityHistory
                .Where(item => item.Value.TestedAt >= testedAt.AddDays(-30))
                .ToDictionary();
            SaveState();
            var aliveForGoogle = testMap.Where(kv => kv.Value > 0).OrderBy(kv => kv.Value).ToList();
            var quarantined = aliveForGoogle.Where(kv => kv.Value < State.QuarantineMaxLatencyMs).ToList();
            Logging.SaveLog($"KeeperService refresh: tested={parsed.Count} dualTargetAlive={aliveForGoogle.Count} mainPoolPass={quarantined.Count}");
            if (aliveForGoogle.Count < 1)
            {
                NoticeManager.Instance.SendMessage($"畅连管家：⚠️ 本轮 {parsed.Count} 个节点无一通过真测速（免费池高峰期常见），保留现有连接（告警）");
                Logging.SaveLog("KeeperService refresh FAILED: no node passed both HTTPS targets — keep current connection (ALARM)");
                return false;
            }

            // 4. 隔离晋升：真测速 <800ms 的进 quality.yaml（Primary-Pool 专用），其余活节点留 backup.yaml
            var qualityProxies = new List<Dictionary<string, object?>>();
            var qualityLinks = new List<string>();
            var backupProxies = new List<Dictionary<string, object?>>();
            var nameByName = parsed.ToDictionary(t => (string)t.proxy["name"]!, t => t);
            foreach (var (name, delay) in aliveForGoogle)
            {
                if (!nameByName.TryGetValue(name, out var hit))
                {
                    continue;
                }
                if (delay < State.QuarantineMaxLatencyMs)
                {
                    qualityProxies.Add(hit.proxy);
                    qualityLinks.Add(hit.link);
                }
                else
                {
                    backupProxies.Add(hit.proxy);
                }
            }
            foreach (var (name, _) in aliveForGoogle.Where(kv => !nameByName.ContainsKey(kv.Key)).ToList())
            {
                Logging.SaveLog($"KeeperService refresh: unknown test node '{name}' skipped");
            }
            var backupNodes = backupProxies
                .Select(proxy => nameByName[(string)proxy["name"]!])
                .Select(item => FmtHandler.ResolveConfig(item.link, out _))
                .OfType<ProfileItem>()
                .Select(item => new KeeperNode
                {
                    Remarks = item.Remarks,
                    Host = item.Address,
                    Port = item.Port,
                    QualityKey = GetQualityKey(item),
                    Security = EvaluateSecurity(item).Level,
                    Source = SourceOfHostPort(sourceByHostPort, item.Address, item.Port),
                })
                .ToList();
            if (qualityProxies.Count == 0)
            {
                await File.WriteAllTextAsync(Path.Combine(WorkDir, "backup.yaml"), BuildClashYaml(backupProxies));
                State.BackupCount = backupProxies.Count;
                State.BackupNodes = backupNodes;
                State.Tested = parsed.Count;
                State.LastRefresh = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _lastRefreshAt = DateTime.Now;
                SaveState();
                RefreshStatus = $"刷新完成 · 保留主池，更新 {backupProxies.Count} 个备用节点";
                Logging.SaveLog($"KeeperService refresh: backup-only result count={backupProxies.Count}; retained previous primary pool");
                return true;
            }
            await File.WriteAllTextAsync(Path.Combine(WorkDir, "quality.yaml"), BuildClashYaml(qualityProxies));
            await File.WriteAllTextAsync(Path.Combine(WorkDir, "backup.yaml"), BuildClashYaml(backupProxies));
            Logging.SaveLog($"KeeperService refresh: pools written quality={qualityProxies.Count} backup={backupProxies.Count}");

            // 5. 增量准入（默认）：不盲目替换旧池——只补目标带内空位，地区分散优先；
            await EnsureSubItemAsync();
            var existing = await AppManager.Instance.ProfileItems(SubId) ?? [];
            var inPool = existing.Select(t => $"{t.Address.ToLowerInvariant()}:{t.Port}").ToHashSet();
            var target = Math.Max(State.PoolMinMain, Math.Min(State.PoolMaxMain, Math.Max(existing.Count, State.PoolMinMain)));
            var slots = Math.Max(0, target - existing.Count);
            var regionCount = new Dictionary<string, int>();
            foreach (var it in existing)
            {
                var rg0 = RegionOf(it.Remarks);
                regionCount[rg0] = regionCount.GetValueOrDefault(rg0) + 1;
            }
            var regionCap = (int)Math.Ceiling(target * 0.4);
            var importLinks = new List<string>();
            foreach (var link in qualityLinks)
            {
                if (importLinks.Count >= slots)
                {
                    break;
                }
                var hp = ParseHostPort(link);
                if (hp == null || inPool.Contains($"{hp.Value.host.ToLowerInvariant()}:{hp.Value.port}"))
                {
                    continue;
                }
                var region = RegionOf(link);
                if (region != "OTHER" && regionCount.GetValueOrDefault(region) >= regionCap)
                {
                    continue; // 同地区占比 ≤ 40%，防单地区全军覆没
                }
                regionCount[region] = regionCount.GetValueOrDefault(region) + 1;
                importLinks.Add(link);
            }
            var count = importLinks.Count > 0
                ? await ConfigHandler.AddBatchServers(_config, string.Join(Environment.NewLine, importLinks), SubId, false)
                : Math.Max(existing.Count, 1);
            if (count < 1)
            {
                Logging.SaveLog("KeeperService refresh FAILED: import returned 0");
                NoticeManager.Instance.SendMessage("畅连管家：节点导入失败，保持原线路不变");
                return false;
            }
            State.QualityLinks = qualityLinks;

            // 7. security filter：池内已有节点按同一口径复审（老版本放进来的、链接被改过的），低安全的一律出池
            var items = await AppManager.Instance.ProfileItems(SubId) ?? [];
            var bad = items.Where(t => !t.IsValid() || t.GetAllowInsecure() || !SecurityAllows(t)).ToList();
            if (bad.Count > 0)
            {
                foreach (var evicted in bad)
                {
                    Logging.SaveLog($"KeeperService security audit: evicted '{evicted.Remarks}' ({EvaluateSecurity(evicted).Reason})");
                }
                State.SecurityAudit.Evicted = bad.Count;
                await ConfigHandler.RemoveServers(_config, bad);
                items = items.Except(bad).ToList();
            }

            // 8. policy group + active
            await EnsureGroupAsync();
            if (_config.IndexId.IsNullOrEmpty() && items.Count > 0)
            {
                await ApplyActiveAsync(items);
                if (_reloadFunc != null)
                {
                    await _reloadFunc();
                }
            }
            else if (_config.IndexId == GroupIndexId && (importLinks.Count > 0 || bad.Count > 0) && _reloadFunc != null)
            {
                // 策略组按订阅筛选条件动态取成员：新节点入池、或不安全节点被剔除后，
                // 必须重载内核，运行中的配置才会跟着变（否则要等下次切线或重启才生效）。
                Logging.SaveLog($"KeeperService: pool membership changed (+{importLinks.Count} / -{bad.Count}), reloading core");
                await _reloadFunc();
            }

            // 9. state + reload
            State.LastRefresh = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _lastRefreshAt = DateTime.Now;
            State.LastAlive = items.Count;
            State.Tested = parsed.Count;
            State.Nodes = BuildNodes(items, sourceByHostPort);
            State.BackupCount = backupProxies.Count;
            State.BackupNodes = backupNodes;
            SaveState();
            RefreshStatus = State.SecurityAudit.Rejected > 0
                ? $"刷新完成 · 已测速 {parsed.Count} 个节点 · 拦截 {State.SecurityAudit.Rejected} 个不安全节点"
                : $"刷新完成 · 已测速 {parsed.Count} 个节点";

            NoticeManager.Instance.SendMessage($"畅连管家：换血完成——真测速 {parsed.Count} 个、隔离晋升 {qualityProxies.Count} 个入主池、备用池 {backupProxies.Count} 个兜底、拦截 {State.SecurityAudit.Rejected} 个不安全节点");
            Logging.SaveLog($"KeeperService refreshed: tested={parsed.Count} pool={items.Count} quality={qualityProxies.Count} backup={backupProxies.Count} rejected={State.SecurityAudit.Rejected} evicted={bad.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService refresh", ex);
            NoticeManager.Instance.SendMessage("畅连管家：刷新出错，保持原线路不变");
            return false;
        }
        finally
        {
            if (!RefreshStatus.StartsWith("刷新完成", StringComparison.Ordinal))
            {
                RefreshStatus = "刷新未完成 · 查看运行日志";
            }
            try
            {
                File.Delete(Path.Combine(WorkDir, "candidates.yaml"));
                File.Delete(Path.Combine(WorkDir, "config-test.yaml"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logging.SaveLog("KeeperService clean test files", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }

    private readonly object _proxyClientLock = new();

    private async Task<bool> VerifyProxyAsync()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        if (port is < 1 or > 65535)
        {
            return false;
        }

        HttpClient proxyClient;
        lock (_proxyClientLock)
        {
            if (_httpProxy == null || _proxyClientPort != port)
            {
                _httpProxy?.Dispose();
                _httpProxy = new HttpClient(new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                    AllowAutoRedirect = false,
                });
                _proxyClientPort = port;
            }
            proxyClient = _httpProxy;
        }
        foreach (var target in new[] { TestUrl, SecondaryTestUrl })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                using var response = await proxyClient.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return true;
                }
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { }
        }
        return false;
    }

    private async Task<bool> SwitchAndVerifyAsync(string indexId)
    {
        if (_config == null || _reloadFunc == null || _config.IndexId == indexId)
        {
            return false;
        }

        var previous = _config.IndexId;
        try
        {
            await ConfigHandler.SetDefaultServerIndex(_config, indexId);
            await _reloadFunc();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (await VerifyProxyAsync())
                {
                    return true;
                }
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Keeper switch verification", ex);
        }

        await ConfigHandler.SetDefaultServerIndex(_config, previous);
        await _reloadFunc();
        Logging.SaveLog("Keeper switch failed verification; restored previous selection");
        return false;
    }

    private async Task ApplyActiveAsync(List<ProfileItem> items)
    {
        if (_config == null)
        {
            return;
        }
        if (State.AutoSwitch)
        {
            await ConfigHandler.SetDefaultServerIndex(_config, GroupIndexId);
        }
        else
        {
            // 导入顺序 = 真测速延迟排序，取第一名
            var best = items.FirstOrDefault();
            if (best != null)
            {
                await ConfigHandler.SetDefaultServerIndex(_config, best.IndexId);
            }
        }
    }

    // ------------------------------------------------------------------ ensure group/subitem
    private async Task EnsureSubItemAsync()
    {
        if (_config == null)
        {
            return;
        }
        var subs = await AppManager.Instance.SubItems();
        if (subs.Any(t => t.Id == SubId))
        {
            return;
        }
        await ConfigHandler.AddSubItem(_config, new SubItem
        {
            Id = SubId,
            Remarks = "畅连优选池",
            Url = string.Empty,
            Enabled = true,
            Sort = 0,
            Memo = "由畅连管家自动维护，勿手动更新订阅",
        });
    }

    private async Task EnsureGroupAsync()
    {
        if (_config == null)
        {
            return;
        }
        var group = await AppManager.Instance.GetProfileItem(GroupIndexId);
        if (group != null)
        {
            return;
        }
        await EnsureSubItemAsync();
        var profile = new ProfileItem
        {
            IndexId = GroupIndexId,
            CoreType = ECoreType.sing_box,
            ConfigType = EConfigType.PolicyGroup,
            Remarks = GroupRemarks,
            IsSub = false,
        };
        profile.SetProtocolExtra(new ProtocolExtraItem
        {
            GroupType = EConfigType.PolicyGroup.ToString(),
            SubChildItems = SubId,
            Filter = Global.PolicyGroupDefaultAllFilter,
            MultipleLoad = EMultipleLoad.LeastPing,
        });
        await ConfigHandler.AddServerCommon(_config, profile, true);
    }

    // ------------------------------------------------------------------ toggles
    // 实时网速：ws://clash-api/traffic 每秒推 {up,down}
    private async Task SpeedLoopAsync()
    {
        var buf = new byte[2048];
        while (_wsCts != null && !_wsCts.IsCancellationRequested)
        {
            try
            {
                // xray 没有 clash 的 /traffic 接口，硬连只会每 3 秒抛一次 SocketException（日志噪音源）
                if (!_coreApiOk)
                {
                    await Task.Delay(3000, _wsCts.Token);
                    continue;
                }
                using var ws = new ClientWebSocket();
                var uri = new Uri($"ws://{Global.Loopback}:{AppManager.Instance.StatePort2}/traffic");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_wsCts.Token);
                cts.CancelAfter(5000);
                await ws.ConnectAsync(uri, cts.Token);
                while (!_wsCts.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), _wsCts.Token);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            throw new WebSocketException("traffic closed");
                        }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    } while (!res.EndOfMessage);
                    var item = JsonUtils.Deserialize<TrafficItem>(sb.ToString());
                    if (item != null)
                    {
                        Interlocked.Exchange(ref _upSpeed, (long)item.Up);
                        Interlocked.Exchange(ref _downSpeed, (long)item.Down);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                Interlocked.Exchange(ref _upSpeed, 0);
                Interlocked.Exchange(ref _downSpeed, 0);
                try { await Task.Delay(3000, _wsCts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    // 连接/断开：切系统代理（ForcedChange=走畅连 / ForcedClear=全部直连）
    public async Task SetConnectedAsync(bool on)
    {
        if (_config == null)
        {
            return;
        }
        var target = on ? ESysProxyType.ForcedChange : ESysProxyType.ForcedClear;
        if (_config.SystemProxyItem.SysProxyType != target)
        {
            _config.SystemProxyItem.SysProxyType = target;
            var applied = await SysProxyHandler.UpdateSysProxy(_config, false);
            await ConfigHandler.SaveConfig(_config);
            if (!applied)
            {
                NoticeManager.Instance.SendMessage("系统代理由其他应用使用");
                return;
            }
        }
        NoticeManager.Instance.SendMessage(on ? "畅连：已连接，浏览器走畅连线路" : "畅连：已断开，浏览器恢复直连");
    }

    public async Task SetEnabledAsync(bool on)
    {
        State.Enabled = on;
        SaveState();
        NoticeManager.Instance.SendMessage(on ? "畅连管家：已开启自动维护" : "畅连管家：已关闭自动维护（手动模式）");
        if (on)
        {
            await EnsureGroupAsync();
            _ = _reloadFunc?.Invoke();
        }
    }

    public async Task SetAutoSwitchAsync(bool on)
    {
        State.AutoSwitch = on;
        SaveState();
        if (_config == null || !State.Enabled)
        {
            return;
        }
        if (on)
        {
            await ConfigHandler.SetDefaultServerIndex(_config, GroupIndexId);
        }
        else
        {
            var items = await AppManager.Instance.ProfileItems(SubId) ?? [];
            await ApplyActiveAsync(items);
        }
        _ = _reloadFunc?.Invoke();
        NoticeManager.Instance.SendMessage(on ? "畅连管家：自动切换最快线路已开启" : "畅连管家：已锁定当前线路，不再自动切换");
    }

    public void SetDailyRefresh(bool on)
    {
        State.DailyRefresh = on;
        SaveState();
        NoticeManager.Instance.SendMessage(on
            ? $"畅连管家：每 {State.RefreshIntervalHours} 小时自动刷新节点"
            : "畅连管家：定时刷新已关闭");
    }

    // ------------------------------------------------------------------ fetch / parse
    private async Task<string?> FetchPoolAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps
            || source.UserInfo.Length != 0)
        {
            return null;
        }
        foreach (var client in new[] { _subscriptionClient })
        {
            try
            {
                using var response = await client.GetAsync(source);
                if (!response.IsSuccessStatusCode) { continue; }
                var body = await response.Content.ReadAsStringAsync();
                if (body.IsNotEmpty())
                {
                    return body;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
        }
        return null;
    }

    private static IEnumerable<string> ExtractLinks(string body)
    {
        var lines = body.Split('\n').Select(t => t.Trim()).Where(t => t.Length > 10);
        var found = lines.Where(t => t.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)
                                  || t.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)
                                  || t.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)
                                  || t.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count > 0)
        {
            return found;
        }
        // whole body may be base64
        var decoded = B64Decode(body);
        if (decoded != null)
        {
            return decoded.Split('\n').Select(t => t.Trim())
                .Where(t => t.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)
                         || t.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)
                         || t.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)
                         || t.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase));
        }
        return [];
    }

    private static string? B64Decode(string s)
    {
        try
        {
            s = s.Replace("-", "+").Replace("_", "/").Trim();
            s += new string('=', (4 - s.Length % 4) % 4);
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch
        {
            return null;
        }
    }

    internal static (string host, int port)? ParseHostPort(string link)
    {
        try
        {
            if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            {
                var json = B64Decode(link[8..].Split('#')[0]);
                if (json == null)
                {
                    return null;
                }
                using var doc = JsonDocument.Parse(json);
                var host = doc.RootElement.TryGetProperty("add", out var add) ? add.GetString() : null;
                var port = doc.RootElement.TryGetProperty("port", out var p) ? p.GetInt32() : 0;
                if (host.IsNullOrEmpty() || port <= 0)
                {
                    return null;
                }
                return (host, port);
            }

            // ss:// | vless:// | trojan://  → strip fragment, split at last '@'
            var body = link.Split('#')[0].Split('?')[0];
            var at = body.LastIndexOf('@');
            if (at < 0)
            {
                // legacy ss://base64(method:pass@host:port)
                if (!link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                var dec = B64Decode(body[5..]);
                at = dec?.LastIndexOf('@') ?? -1;
                if (dec == null || at < 0)
                {
                    return null;
                }
                body = dec;
            }
            var hp = body[(at + 1)..];
            var colon = hp.LastIndexOf(':');
            if (colon < 0)
            {
                return null;
            }
            var host2 = hp[..colon].Trim('[', ']');
            if (!int.TryParse(hp[(colon + 1)..], out var port2) || host2.IsNullOrEmpty() || port2 <= 0)
            {
                return null;
            }
            return (host2, port2);
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ security audit
    // 节点安全分级只看节点自身的客观属性（协议、加密、传输、地址），不看来源与口碑。
    // 「低」= 禁止注入（弱加密 / 明文 / 跳过证书校验 / 内网地址）；「中」「高」= 允许入池并在界面标注。
    internal const string SecurityHigh = "高";
    internal const string SecurityMedium = "中";
    internal const string SecurityLow = "低";

    private static int _minSecurityRank = 1; // State.MinSecurity 的静态镜像（候选闸是静态方法）；下限钳到 1，低永远拦

    private static readonly HashSet<string> SsAeadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "aes-128-gcm", "aes-192-gcm", "aes-256-gcm",
        "chacha20-ietf-poly1305", "xchacha20-ietf-poly1305",
        "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm", "2022-blake3-chacha20-poly1305",
    };

    internal static int SecurityRank(string level) => level switch
    {
        SecurityHigh => 2,
        SecurityMedium => 1,
        _ => 0,
    };

    internal static (string Level, string Reason) EvaluateSecurity(ProfileItem item)
    {
        if (item.GetAllowInsecure())
        {
            return (SecurityLow, "跳过证书校验（可被中间人解密篡改）");
        }
        var host = (item.Address ?? string.Empty).TrimEnd('.');
        if (IPAddress.TryParse(host, out var address) && !IsPublicAddress(address))
        {
            return (SecurityLow, "节点地址为内网/保留地址");
        }
        var extra = item.GetProtocolExtra();
        var tls = item.StreamSecurity is Global.StreamSecurity or Global.StreamSecurityReality;
        switch (item.ConfigType)
        {
            case EConfigType.Shadowsocks:
                if (!SsAeadMethods.Contains(extra.SsMethod ?? string.Empty))
                {
                    return (SecurityLow, $"Shadowsocks 弱加密或无认证（{(extra.SsMethod.IsNullOrEmpty() ? "未声明" : extra.SsMethod)}）");
                }
                return tls
                    ? (SecurityHigh, "SS AEAD + TLS")
                    : (SecurityMedium, "SS AEAD 直连（流量特征可识别）");
            case EConfigType.VMess:
                if (!tls)
                {
                    return (SecurityLow, "VMess 未启用 TLS（明文传输）");
                }
                return extra.VmessSecurity is "none" or "zero"
                    ? (SecurityMedium, "VMess + TLS（无内层加密）")
                    : (SecurityHigh, "VMess + TLS + AEAD");
            case EConfigType.VLESS:
                return tls
                    ? (SecurityHigh, item.StreamSecurity == Global.StreamSecurityReality ? "VLESS + Reality" : "VLESS + TLS")
                    : (SecurityLow, "VLESS 未启用 TLS（UUID 与流量明文）");
            case EConfigType.Trojan:
                // 链接不写 security=tls 时 sing-box 出站不会带 TLS：测速能过（mihomo 对 Trojan 默认 TLS）但真用是明文，必须拦
                return tls
                    ? (SecurityHigh, "Trojan + TLS")
                    : (SecurityLow, "Trojan 未启用 TLS（口令明文）");
            default:
                return (SecurityLow, "不支持的协议类型");
        }
    }

    // 是否允许注入：等级不低于门槛（ParsePoolCandidate 候选闸与池内复审共用）
    internal static bool SecurityAllows(ProfileItem item) => SecurityRank(EvaluateSecurity(item).Level) >= _minSecurityRank;

    // 候选被闸拦下时，若原因是安全等级不足则计入分布（解析失败不算安全拦截）
    private static void CountSecurityRejection(string link, Dictionary<string, int> reasons)
    {
        var profile = FmtHandler.ResolveConfig(link, out _);
        if (profile == null || !profile.IsValid())
        {
            return;
        }
        var (level, reason) = EvaluateSecurity(profile);
        if (SecurityRank(level) < _minSecurityRank)
        {
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
        }
    }

    // 来源池短名：raw.githubusercontent.com/{owner}/{repo}/... → owner/repo；其余取主机名
    internal static string PoolShortName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }
        if (uri.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            var seg = uri.AbsolutePath.Trim('/').Split('/');
            if (seg.Length >= 2)
            {
                return seg[0] + "/" + seg[1];
            }
        }
        return uri.Host;
    }

    private static string SourceOfHostPort(Dictionary<string, string> sourceByHostPort, string host, int port)
        => sourceByHostPort.GetValueOrDefault($"{(host ?? string.Empty).TrimEnd('.').ToLowerInvariant()}:{port}", string.Empty);

    // 池内节点 → 界面展示模型：安全等级按同口径现算，来源优先取本轮抓取记录、其次沿用上次记录
    private List<KeeperNode> BuildNodes(List<ProfileItem> items, Dictionary<string, string>? sourceByHostPort = null)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in State.Nodes)
        {
            known.TryAdd(node.QualityKey, node.Source);
        }
        return items.Select(t =>
        {
            var key = GetQualityKey(t);
            var src = sourceByHostPort != null ? SourceOfHostPort(sourceByHostPort, t.Address, t.Port) : string.Empty;
            return new KeeperNode
            {
                Remarks = t.Remarks,
                Host = t.Address,
                Port = t.Port,
                QualityKey = key,
                Security = EvaluateSecurity(t).Level,
                Source = src.Length > 0 ? src : known.GetValueOrDefault(key, string.Empty),
            };
        }).ToList();
    }

    // ------------------------------------------------------------------ link → clash proxy (mihomo)
    private static string UrlDecode(string s) => Uri.UnescapeDataString(s ?? string.Empty);

    private static Dictionary<string, object?>? ParsePoolCandidate(string link)
    {
        var profile = FmtHandler.ResolveConfig(link, out _);
        if (profile == null || !profile.IsValid() || profile.GetAllowInsecure()
            || profile.ConfigType is EConfigType.VLESS or EConfigType.VMess
                && profile.StreamSecurity is not ("tls" or "reality"))
        {
            return null;
        }
        if (!SecurityAllows(profile))
        {
            return null; // 安全等级低于门槛（弱加密 / 明文 / 跳过证书校验 / 内网地址）：换血与补位任何阶段都不注入
        }

        var host = profile.Address.TrimEnd('.');
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown
            || (!IPAddress.TryParse(host, out _) && (!host.Contains('.') || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))))
        {
            return null;
        }

        return IPAddress.TryParse(host, out var address) && !IsPublicAddress(address) ? null : ParseToClash(link);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 16 ? (bytes[0] & 0xe0) == 0x20 :
            !(bytes[0] is 0 or 10 or 127 or >= 224
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                || bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    private static Dictionary<string, object?>? ParseToClash(string link)
    {
        try
        {
            if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            {
                return ParseVmess(link);
            }
            if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                return ParseVless(link);
            }
            if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSs(link);
            }
            if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
            {
                return ParseTrojan(link);
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static Dictionary<string, object?>? ParseVmess(string link)
    {
        var raw = B64Decode(link[8..].Split('#')[0]);
        if (raw == null)
        {
            return null;
        }
        using var doc = JsonDocument.Parse(raw);
        var r = doc.RootElement;
        string Get(string k) => r.TryGetProperty(k, out var v) ? v.ToString() : string.Empty;
        var server = Get("add");
        var port = int.TryParse(Get("port"), out var pt) ? pt : 0;
        var uuid = Get("id");
        if (server.IsNullOrEmpty() || port <= 0 || uuid.IsNullOrEmpty())
        {
            return null;
        }
        var p = new Dictionary<string, object?>
        {
            ["name"] = Get("ps").IsNullOrEmpty() ? "vmess" : Get("ps"),
            ["type"] = "vmess",
            ["server"] = server,
            ["port"] = port,
            ["uuid"] = uuid,
            ["alterId"] = int.TryParse(Get("aid"), out var aid) ? aid : 0,
            ["cipher"] = Get("scy").IsNullOrEmpty() ? "auto" : Get("scy"),
            ["udp"] = true,
        };
        var tls = Get("tls");
        if (tls is "tls" or "True" or "true")
        {
            p["tls"] = true;
            var sni = Get("sni").IsNotEmpty() ? Get("sni") : Get("host");
            if (sni.IsNotEmpty())
            {
                p["servername"] = sni;
            }
        }
        var net = Get("net").IsNullOrEmpty() ? "tcp" : Get("net");
        if (net != "tcp")
        {
            p["network"] = net;
        }
        if (net == "ws")
        {
            var wo = new Dictionary<string, object?> { ["path"] = Get("path").IsNullOrEmpty() ? "/" : Get("path") };
            if (Get("host").IsNotEmpty())
            {
                wo["headers"] = new Dictionary<string, object?> { ["Host"] = Get("host") };
            }
            p["ws-opts"] = wo;
        }
        return p;
    }

    private static Dictionary<string, object?>? ParseVless(string link)
    {
        var body = link[8..];
        var frag = string.Empty;
        var hash = body.IndexOf('#');
        if (hash >= 0)
        {
            frag = UrlDecode(body[(hash + 1)..]);
            body = body[..hash];
        }
        var at = body.LastIndexOf('@');
        if (at < 0)
        {
            return null;
        }
        var userinfo = UrlDecode(body[..at]);
        var hostport = body[(at + 1)..];
        var q = string.Empty;
        var qm = hostport.IndexOf('?');
        if (qm >= 0)
        {
            q = hostport[(qm + 1)..];
            hostport = hostport[..qm];
        }
        var colon = hostport.LastIndexOf(':');
        if (colon < 0 || !int.TryParse(hostport[(colon + 1)..], out var port))
        {
            return null;
        }
        var host = hostport[..colon].Trim('[', ']');
        var kv = q.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2))
            .Where(t => t.Length == 2)
            .ToDictionary(t => t[0], t => UrlDecode(t[1]));
        var uuid = userinfo;
        if (uuid.IsNullOrEmpty() || uuid.Length < 8)
        {
            return null;
        }
        var p = new Dictionary<string, object?>
        {
            ["name"] = frag.IsNullOrEmpty() ? "vless" : frag,
            ["type"] = "vless",
            ["server"] = host,
            ["port"] = port,
            ["uuid"] = uuid,
            ["udp"] = true,
        };
        kv.TryGetValue("security", out var sec);
        if (sec is "tls" or "reality")
        {
            p["tls"] = true;
            if (kv.TryGetValue("sni", out var sni))
            {
                p["servername"] = sni;
            }
            if (kv.TryGetValue("fp", out var fp))
            {
                p["client-fingerprint"] = fp;
            }
            if (sec == "reality")
            {
                var ro = new Dictionary<string, object?>();
                if (kv.TryGetValue("pbk", out var pbk))
                {
                    ro["public-key"] = pbk;
                }
                if (kv.TryGetValue("sid", out var sid))
                {
                    ro["short-id"] = sid;
                }
                if (ro.Count > 0)
                {
                    p["reality-opts"] = ro;
                }
            }
        }
        if (kv.TryGetValue("flow", out var flow))
        {
            p["flow"] = flow;
        }
        var net = kv.TryGetValue("type", out var ty) && ty.IsNotEmpty() ? ty : "tcp";
        if (net != "tcp")
        {
            p["network"] = net;
            if (net == "ws")
            {
                var wo = new Dictionary<string, object?> { ["path"] = kv.TryGetValue("path", out var path) ? path : "/" };
                if (kv.TryGetValue("host", out var wsHost))
                {
                    wo["headers"] = new Dictionary<string, object?> { ["Host"] = wsHost };
                }
                p["ws-opts"] = wo;
            }
        }
        return p;
    }

    private static Dictionary<string, object?>? ParseSs(string link)
    {
        var body = link[5..];
        var frag = string.Empty;
        var hash = body.IndexOf('#');
        if (hash >= 0)
        {
            frag = UrlDecode(body[(hash + 1)..]);
            body = body[..hash];
        }
        var qm = body.IndexOf('?');
        if (qm >= 0)
        {
            body = body[..qm];
        }
        string cipher, pwd, hostport;
        var at = body.LastIndexOf('@');
        if (at >= 0)
        {
            var userinfo = body[..at];
            hostport = body[(at + 1)..];
            var dec = B64Decode(userinfo);
            if (dec != null && dec.Contains(':'))
            {
                var ix = dec.IndexOf(':');
                cipher = dec[..ix];
                pwd = dec[(ix + 1)..];
            }
            else
            {
                var u = UrlDecode(userinfo);
                var ix = u.IndexOf(':');
                cipher = ix >= 0 ? u[..ix] : u;
                pwd = ix >= 0 ? u[(ix + 1)..] : string.Empty;
            }
        }
        else
        {
            var dec = B64Decode(body);
            if (dec == null || !dec.Contains('@'))
            {
                return null;
            }
            at = dec.LastIndexOf('@');
            var userinfo = dec[..at];
            hostport = dec[(at + 1)..];
            var ix = userinfo.IndexOf(':');
            cipher = ix >= 0 ? userinfo[..ix] : userinfo;
            pwd = ix >= 0 ? userinfo[(ix + 1)..] : string.Empty;
        }
        var colon = hostport.LastIndexOf(':');
        if (colon < 0 || !int.TryParse(hostport[(colon + 1)..], out var port))
        {
            return null;
        }
        var host = hostport[..colon].Trim('[', ']');
        if (host.IsNullOrEmpty() || cipher.IsNullOrEmpty() || pwd.IsNullOrEmpty())
        {
            return null;
        }
        if (cipher.ToLowerInvariant() is not ("aes-128-gcm" or "aes-192-gcm" or "aes-256-gcm"
            or "chacha20-ietf-poly1305" or "xchacha20-ietf-poly1305"
            or "2022-blake3-aes-128-gcm" or "2022-blake3-aes-256-gcm" or "2022-blake3-chacha20-poly1305"))
        {
            return null;
        }
        return new Dictionary<string, object?>
        {
            ["name"] = frag.IsNullOrEmpty() ? "ss" : frag,
            ["type"] = "ss",
            ["server"] = host,
            ["port"] = port,
            ["cipher"] = cipher,
            ["password"] = pwd,
            ["udp"] = true,
        };
    }

    private static Dictionary<string, object?>? ParseTrojan(string link)
    {
        var body = link[9..];
        var frag = string.Empty;
        var hash = body.IndexOf('#');
        if (hash >= 0)
        {
            frag = UrlDecode(body[(hash + 1)..]);
            body = body[..hash];
        }
        var q = string.Empty;
        var qm = body.IndexOf('?');
        if (qm >= 0)
        {
            q = body[(qm + 1)..];
            body = body[..qm];
        }
        var at = body.LastIndexOf('@');
        if (at < 0)
        {
            return null;
        }
        var pwd = UrlDecode(body[..at]);
        var hostport = body[(at + 1)..];
        var colon = hostport.LastIndexOf(':');
        if (colon < 0 || !int.TryParse(hostport[(colon + 1)..], out var port))
        {
            return null;
        }
        var host = hostport[..colon].Trim('[', ']');
        if (host.IsNullOrEmpty() || pwd.IsNullOrEmpty())
        {
            return null;
        }
        var p = new Dictionary<string, object?>
        {
            ["name"] = frag.IsNullOrEmpty() ? "trojan" : frag,
            ["type"] = "trojan",
            ["server"] = host,
            ["port"] = port,
            ["password"] = pwd,
            ["udp"] = true,
        };
        var kv = q.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2))
            .Where(t => t.Length == 2)
            .ToDictionary(t => t[0], t => UrlDecode(t[1]));
        if (kv.TryGetValue("sni", out var sni))
        {
            p["sni"] = sni;
        }
        var net = kv.TryGetValue("type", out var ty) && ty.IsNotEmpty() ? ty : "tcp";
        if (net != "tcp")
        {
            p["network"] = net;
            if (net == "ws")
            {
                p["ws-opts"] = new Dictionary<string, object?> { ["path"] = kv.TryGetValue("path", out var path) ? path : "/" };
            }
        }
        return p;
    }

    // ------------------------------------------------------------------ clash yaml emit
    private static string Esc(object? v)
    {
        var s = v?.ToString() ?? string.Empty;
        return "'" + s.Replace("'", "''") + "'";
    }

    private static string InlineMap(Dictionary<string, object?> p)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var kv in p)
        {
            if (!first)
            {
                sb.Append(", ");
            }
            first = false;
            sb.Append(kv.Key).Append(": ");
            switch (kv.Value)
            {
                case null:
                    sb.Append("''");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i);
                    break;
                case long l:
                    sb.Append(l);
                    break;
                case Dictionary<string, object?> inner:
                    sb.Append('{').Append(InlineMap(inner)).Append('}');
                    break;
                default:
                    sb.Append(Esc(kv.Value));
                    break;
            }
        }
        return sb.ToString();
    }

    private static string BuildClashYaml(List<Dictionary<string, object?>> proxies)
    {
        var sb = new StringBuilder("proxies:\n");
        foreach (var p in proxies)
        {
            sb.Append("  - {").Append(InlineMap(p)).Append("}\n");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ mihomo process
    private static Process? StartMihomo(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = MihomoExe,
                Arguments = args,
                WorkingDirectory = WorkDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService mihomo start", ex);
            return null;
        }
    }

    private static void KillProc(Process? p)
    {
        try
        {
            if (p != null && !p.HasExited)
            {
                p.Kill(true);
            }
        }
        catch { }
    }

    private static async Task<int> TcpPingAsync(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) { return -1; }
            var connect = tcp.ConnectAsync(addresses, port);
            if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
            {
                return -1;
            }
            if (!tcp.Connected)
            {
                return -1;
            }
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }

    // ------------------------------------------------------------------ state io
    private void LoadState()
    {
        try
        {
            var path = Utils.GetConfigPath("keeper.json");
            if (File.Exists(path))
            {
                var st = JsonUtils.Deserialize<KeeperState>(File.ReadAllText(path));
                if (st != null)
                {
                    State = st;
                    State.SecurityAudit ??= new();
                    _minSecurityRank = Math.Max(1, SecurityRank(State.MinSecurity));
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("KeeperService load state", ex);
        }
    }

    private readonly object _stateWriteLock = new();

    private void SaveState()
    {
        lock (_stateWriteLock)
        {
            try
            {
                var path = Utils.GetConfigPath("keeper.json");
                WriteStateFile(path, JsonUtils.Serialize(State));
            }
            catch (Exception ex)
            {
                Logging.SaveLog("KeeperService save state", ex);
                NoticeManager.Instance.SendMessage("畅连管家：状态保存失败，请查看运行日志");
            }
        }
    }

    private static void WriteStateFile(string path, string contents)
    {
        var staged = path + ".tmp";
        File.WriteAllText(staged, contents);
        File.Move(staged, path, overwrite: true);
    }
}

[Serializable]
public class KeeperState
{
    public bool Enabled { get; set; } = true;
    public bool AutoSwitch { get; set; } = true;
    public bool DailyRefresh { get; set; } = true;


    public List<string> Pools { get; set; } =
    [
        "https://raw.githubusercontent.com/free-nodes/v2rayfree/main/sub",
        "https://raw.githubusercontent.com/Pawdroid/Free-servers/main/sub",
        "https://raw.githubusercontent.com/ermaozi/get_subscribe/main/subscribe/v2ray.txt",
        "https://raw.githubusercontent.com/peasoft/NoMoreWalls/master/list.txt",
        "https://raw.githubusercontent.com/ts-sf/fly/main/v2",
        "https://raw.githubusercontent.com/Leon406/SubCrawler/master/sub/share/v2",
        "https://raw.githubusercontent.com/ripaojiedian/freenode/main/sub",
        "https://raw.githubusercontent.com/mfuu/v2ray/master/sub",
        "https://raw.githubusercontent.com/MhdiTaheri/V2rayCollector/main/sub/mix",
        "https://raw.githubusercontent.com/ALIILAPRO/v2rayNG-Config/main/server.txt",
    ];

    public int MaxCandidates { get; set; } = 300;
    public int MinAlive { get; set; } = 3;

    // ---- 守卫 v2 配置（全部可配置，PascalCase 与 keeper.json 兼容）----
    public int GuardConsecutive { get; set; } = 3;        // 连续 N 次 bad 才允许判定 quality_bad
    public int GuardLatencyMs { get; set; } = 3000;       // 单次探测延迟超过此值记为 bad（免费跨境节点 HTTPS 常见 1.5–2.5s，阈值过低会把"慢"误判成"断"）
    public int StartupGraceSec { get; set; } = 90;        // 启动宽限：核心就绪+宽限期内的探测结果不作为换血依据
    public int QuarantineMaxLatencyMs { get; set; } = 2000; // 隔离池晋升门槛：真测速 < 此值才能进 Primary-Pool

    // ---- 池管理平衡档（v1.3.0）----
    public int PoolMinMain { get; set; } = 10;            // 主池目标带下限（太少无冗余）
    public int PoolMaxMain { get; set; } = 15;            // 主池目标带上限（太多测速轮次慢）
    public int DegradeLatencyMs { get; set; } = 3000;     // 单节点劣化阈值（真测速超过即记一轮；与守卫阈值一致）
    public int DegradeRounds { get; set; } = 2;           // 连续 N 轮劣化才降级出池
    public int TopupIntervalHours { get; set; } = 4;      // 池健康盘点间隔（降级检查+缺员补位）
    public int RefreshIntervalHours { get; set; } = 12;   // 定时增量换血周期（12h，不盲目替换）
    public List<string> QualityLinks { get; set; } = []; // 最近一轮真测速达标链接库（补位来源）

    // ---- 节点安全审查 ----
    public string MinSecurity { get; set; } = "中"; // 注入门槛：中 = 放行「中」「高」；高 = 只放行「高」；「低」无论如何都拦截
    public KeeperSecurityAudit SecurityAudit { get; set; } = new(); // 最近一轮安全审查结果
    public Dictionary<string, int> DegradeMap { get; set; } = []; // 节点进连续劣化轮数


    public string LastRefresh { get; set; } = string.Empty;
    public int ReachableSources { get; set; }
    public int BackupCount { get; set; }
    public List<KeeperNode> BackupNodes { get; set; } = [];
    public Dictionary<string, KeeperQuality> QualityHistory { get; set; } = [];
    public int LastAlive { get; set; }
    public int Tested { get; set; }
    public List<KeeperNode> Nodes { get; set; } = [];

    public bool Online { get; set; }
    public string ExitNode { get; set; } = string.Empty;
    public string ExitIp { get; set; } = string.Empty;
    public string ExitCountry { get; set; } = string.Empty;
    public string ExitCity { get; set; } = string.Empty;
    public string ExitIsp { get; set; } = string.Empty;
    public int LatencyMs { get; set; } = -1;
    public string PollAt { get; set; } = string.Empty;
}

[Serializable]
public class KeeperNode
{
    public string Remarks { get; set; } = string.Empty;
    public string QualityKey { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Source { get; set; } = string.Empty;   // 来源池（owner/repo 短名）
    public string Security { get; set; } = string.Empty; // 安全等级：高 / 中
}

[Serializable]
public class KeeperSecurityAudit
{
    public string At { get; set; } = string.Empty;
    public int Candidates { get; set; }
    public int Rejected { get; set; }
    public int Evicted { get; set; }
    public string Reasons { get; set; } = string.Empty;
}

public sealed class KeeperQuality
{
    public List<int> Samples { get; set; } = [];
    public DateTime TestedAt { get; set; }
}
