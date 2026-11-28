namespace ServiceLib.Tests.Services;

/// <summary>
/// 节点管家策略：名称闸（国内 / 日抛）、自动选路排序（安全 > 稳定 > 带宽 > 延迟）、出口归属与带宽解析。
/// </summary>
public class KeeperPolicyTests
{
    private static object? InvokeStatic(string name, params object?[] args)
    {
        var method = typeof(KeeperService).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return method?.Invoke(null, args);
    }

    private static (string Tag, int Security, int Days, int Speed, int Delay)[] Rows(params (string, int, int, int, int)[] rows)
    {
        return rows;
    }

    private static string? Rank((string Tag, int Security, int Days, int Speed, int Delay)[] rows, string now, string? exclude, int maxLatency)
    {
        return (string?)InvokeStatic("RankAuto", rows, now, exclude, maxLatency);
    }

    // ---- 名称闸：国内 / 港澳台（非海外）/ 日抛不进池；"CN2"线路类型、日本东京不受影响 ----
    [Test]
    [Arguments("🇨🇳 中国 上海 01", "国内节点")]
    [Arguments("回国专线 IEPL", "国内节点")]
    [Arguments("北京 BGP 中转", "国内节点")]
    [Arguments("#CN-01 ss", "国内节点")]
    [Arguments("香港 IEPL 01", "港澳台节点（非海外）")]
    [Arguments("台湾 Hinet 家宽", "港澳台节点（非海外）")]
    [Arguments("澳门 CTM 01", "港澳台节点（非海外）")]
    [Arguments("Hong Kong BGP 02", "港澳台节点（非海外）")]
    [Arguments("东京 台湾中转 03", "港澳台节点（非海外）")]
    [Arguments("美国 CN2 GIA 03", null)]
    [Arguments("US Los Angeles CN2", null)]
    [Arguments("东京 日本 02", null)]
    [Arguments("日抛节点 11-24", "日抛节点")]
    [Arguments("限时体验 高速", "日抛节点")]
    [Arguments("临时节点 03", "日抛节点")]
    [Arguments("美国 洛杉矶 24h", "日抛节点")]
    [Arguments("德国 法兰克福 02", null)]
    public async Task NameGateRejectsDomesticAndEphemeralNodes(string name, string? expected)
    {
        var reason = (string?)InvokeStatic("NameRejection", name);
        await reason.Should().BeEqualTo(expected);
    }

    [Test]
    public async Task PoolCandidateAppliesNameGate()
    {
        await (InvokeStatic("ParsePoolCandidate", "ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#CN-01") != null).Should().BeFalse();
        await (InvokeStatic("ParsePoolCandidate", "ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#%E6%97%A5%E6%8A%9B") != null).Should().BeFalse();
        await (InvokeStatic("ParsePoolCandidate", "ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#US-01") != null).Should().BeTrue();
    }

    // ---- 自动选路：安全 > 稳定 > 带宽 > 延迟 ----
    [Test]
    public async Task RankPrefersSecurityOverEverythingElse()
    {
        var pick = Rank(Rows(("low-sec-fast", 1, 3, 2, 120), ("high-sec-slow", 2, 0, 0, 400)), "", null, 800);
        await pick.Should().BeEqualTo("high-sec-slow");
    }

    [Test]
    public async Task RankPrefersStableNodeWithinSameSecurity()
    {
        var pick = Rank(Rows(("new-fast", 3, 0, 1, 100), ("old-slower", 3, 4, 1, 300)), "", null, 800);
        await pick.Should().BeEqualTo("old-slower");
    }

    [Test]
    public async Task RankPrefersBandwidthOverLatency()
    {
        var pick = Rank(Rows(("slow-net-fast-ping", 3, 2, 1, 120), ("fast-net-slow-ping", 3, 2, 2, 500)), "", null, 800);
        await pick.Should().BeEqualTo("fast-net-slow-ping");
    }

    [Test]
    public async Task RankKeepsWorkingCurrentNode()
    {
        // 同档：不因为别人快几十毫秒就换
        await Rank(Rows(("current", 3, 2, 1, 300), ("other", 3, 2, 1, 250)), "current", null, 800).Should().BeEqualTo("current");
        // 别人"更老"也不换：在用的节点能用就是稳定，出口不来回跳
        await Rank(Rows(("current", 3, 1, 1, 300), ("other", 3, 9, 1, 250)), "current", null, 800).Should().BeEqualTo("current");
    }

    [Test]
    public async Task RankSwitchesOnlyWhenCurrentIsWorse()
    {
        // 当前超延迟上限
        await Rank(Rows(("current", 1, 2, 1, 900), ("other", 1, 2, 1, 250)), "current", null, 800).Should().BeEqualTo("other");
        // 当前已不可用
        await Rank(Rows(("current", 2, 5, 1, 0), ("other", 1, 0, 1, 250)), "current", null, 800).Should().BeEqualTo("other");
        // 当前带宽明显差
        await Rank(Rows(("current", 3, 2, 0, 200), ("other", 3, 2, 2, 300)), "current", null, 800).Should().BeEqualTo("other");
        // 有安全等级更高的
        await Rank(Rows(("current", 1, 9, 2, 150), ("other", 3, 0, 0, 600)), "current", null, 800).Should().BeEqualTo("other");
    }

    [Test]
    public async Task RankExcludesFailingNodeAndFallsBackWithoutData()
    {
        await Rank(Rows(("a", 3, 2, 1, 300), ("b", 3, 2, 1, 250)), "b", "b", 800).Should().BeEqualTo("a");

        var noData = Rows(("a", 3, 2, 1, 0), ("b", 3, 2, 1, 0));
        await Rank(noData, "", null, 800).Should().BeEqualTo("proxy-auto");
        await Rank(noData, "", "proxy-auto", 800).Should().BeNull();

        await Rank(Rows(("a", 3, 2, 1, 0), ("b", 3, 2, 1, 250)), "b", "b", 800).Should().BeEqualTo("proxy-auto");
    }

    // ---- 带宽档位 ----
    [Test]
    public async Task SpeedTierAndTextFollowThresholds()
    {
        await ((int)InvokeStatic("SpeedTier", 3072d)!).Should().BeEqualTo(2);
        await ((int)InvokeStatic("SpeedTier", 800d)!).Should().BeEqualTo(1);
        await ((int)InvokeStatic("SpeedTier", 100d)!).Should().BeEqualTo(0);
        await ((int)InvokeStatic("SpeedTier", 0d)!).Should().BeEqualTo(0);

        await ((string)InvokeStatic("SpeedText", 3072d)!).Should().BeEqualTo("3.0 MB/s");
        await ((string)InvokeStatic("SpeedText", 800d)!).Should().BeEqualTo("800 KB/s");
        await ((string)InvokeStatic("SpeedText", 0d)!).Should().BeEqualTo(string.Empty);
    }

    // ---- 出口归属：机房 / 家宽、运营商、国家、代理出口标记 ----
    [Test]
    public async Task ExitInfoParsesIpApiFields()
    {
        var datacenter = (KeeperExit)InvokeStatic("ParseExitInfo",
            "{\"status\":\"success\",\"country\":\"United States\",\"countryCode\":\"US\",\"city\":\"Los Angeles\",\"isp\":\"Hetzner\",\"org\":\"\",\"hosting\":true,\"proxy\":false,\"query\":\"1.2.3.4\"}")!;
        await datacenter.CountryCode.Should().BeEqualTo("US");
        await datacenter.NetType.Should().BeEqualTo("机房");
        await datacenter.Ip.Should().BeEqualTo("1.2.3.4");
        await datacenter.Isp.Should().BeEqualTo("Hetzner");
        await datacenter.Proxy.Should().BeFalse();
        await datacenter.DownKBs.Should().BeEqualTo(0d);

        var flaggedHome = (KeeperExit)InvokeStatic("ParseExitInfo",
            "{\"status\":\"success\",\"country\":\"Japan\",\"countryCode\":\"JP\",\"city\":\"Tokyo\",\"isp\":\"NTT\",\"hosting\":false,\"proxy\":true,\"query\":\"5.6.7.8\"}")!;
        await flaggedHome.NetType.Should().BeEqualTo("家宽");
        await flaggedHome.Proxy.Should().BeTrue();

        await (InvokeStatic("ParseExitInfo", "{\"status\":\"fail\",\"message\":\"private range\"}") is null).Should().BeTrue();
    }

    [Test]
    public async Task ExitPlaceUsesChineseCountryName()
    {
        var us = new KeeperExit { CountryCode = "US", Country = "United States", City = "Los Angeles" };
        await ((string)InvokeStatic("ExitPlace", us)!).Should().BeEqualTo("美国 Los Angeles");

        var unknown = new KeeperExit { CountryCode = "XX", Country = "Nowhere", City = string.Empty };
        await ((string)InvokeStatic("ExitPlace", unknown)!).Should().BeEqualTo("Nowhere");
    }

    // ---- 旧版状态文件（没有 MaxLatencyMs / Exits / PinnedIndexId 等新字段）必须能读进来，新字段取默认值 ----
    [Test]
    public async Task LegacyStateFileLoadsWithNewFieldDefaults()
    {
        var legacy = "{\"Enabled\":true,\"AutoSwitch\":true,\"LastRefresh\":\"2026-11-24 14:12:22\",\"QualityHistory\":{\"k1\":{\"Samples\":[120,0],\"TestedAt\":\"2026-11-24T14:12:22\"}},\"Nodes\":[{\"Remarks\":\"n1\",\"Host\":\"h1\",\"Port\":443,\"QualityKey\":\"k1\",\"Security\":\"高\",\"Source\":\"s\"}],\"BackupNodes\":[],\"QualityLinks\":[]}";
        var st = ServiceLib.Common.JsonUtils.Deserialize<KeeperState>(legacy);
        if (st == null)
        {
            await "legacy state deserialized".Should().BeEqualTo("null");
            return;
        }
        await st.Nodes.Count.Should().BeEqualTo(1);
        await st.QualityHistory.Count.Should().BeEqualTo(1);
        await st.MaxLatencyMs.Should().BeEqualTo(1200); // 旧状态文件没有该字段 → 取默认值 1200
        await st.Exits.Count.Should().BeEqualTo(0);
        await st.PinnedIndexId.Should().BeEqualTo(string.Empty);
        await st.QualityHistory["k1"].FirstSeen.Should().BeEqualTo(default(DateTime));
    }

    // ---- 界面显示名只来自应用自己的检测：实测出口 + 协议；没有出口档案时如实写「出口未探测」，不采用节点池写的名称 ----
    [Test]
    public async Task NodeNameComesFromMeasuredExitOnly()
    {
        var itFlag = char.ConvertFromUtf32(0x1F1EE) + char.ConvertFromUtf32(0x1F1F9);
        await KeeperService.FlagOf("IT").Should().BeEqualTo(itFlag);
        await KeeperService.FlagOf("it").Should().BeEqualTo(itFlag);
        await KeeperService.FlagOf("").Should().BeEqualTo(string.Empty);
        await KeeperService.FlagOf("ITA").Should().BeEqualTo(string.Empty);
        var probed = new KeeperNode { Remarks = "🇺🇸 US美国(mibei77.com)", Exit = "意大利 Milan", ExitCode = "IT", Protocol = "SS" };
        await probed.Name.Should().BeEqualTo(itFlag + " 意大利 Milan · SS");
        var unprobed = new KeeperNode { Remarks = "美国+Xerox", Protocol = "VLESS" };
        await unprobed.Name.Should().BeEqualTo("出口未探测 · VLESS");
        await KeeperService.ProtocolOf(ServiceLib.Enums.EConfigType.Hysteria2).Should().BeEqualTo("Hy2");
        await KeeperService.ProtocolOf(ServiceLib.Enums.EConfigType.VLESS).Should().BeEqualTo("VLESS");
    }

    // ---- 内核只在连接期间运行：连接状态或 TUN 开启时才需要内核；断开后内核进程应结束，后台不留代理 ----
    [Test]
    public async Task CoreRunsOnlyWhileConnected()
    {
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedChange).Should().BeTrue();
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear).Should().BeFalse();
        await KeeperService.CoreShouldRun(ESysProxyType.Unchanged).Should().BeFalse();
        // 本地代理常驻模式：系统代理关闭也要保持内核运行
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear, true).Should().BeTrue();
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear, false).Should().BeFalse();
    }

    // ---- 内置节点源只在首次运行时写入；用户删过的源不再自动加回 ----
    [Test]
    public async Task BuiltinPoolsSeedOnlyOnFirstRun()
    {
        await KeeperService.ShouldSeedDefaultPools(false, 0).Should().BeTrue();
        await KeeperService.ShouldSeedDefaultPools(false, 2).Should().BeFalse();
        await KeeperService.ShouldSeedDefaultPools(true, 0).Should().BeFalse();
        await KeeperService.ShouldSeedDefaultPools(true, 13).Should().BeFalse();
    }

    // ---- 节点池成员变化：在用节点还在就不重载内核，避免打断长连接 ----
    [Test]
    public async Task PoolChangeReloadsOnlyWhenActiveNodeGone()
    {
        var items = new[]
        {
            new ServiceLib.Models.Entities.ProfileItem { IndexId = "a" },
            new ServiceLib.Models.Entities.ProfileItem { IndexId = "b" },
        };
        await KeeperService.ShouldReloadCoreForPoolChange("a", items).Should().BeFalse();
        await KeeperService.ShouldReloadCoreForPoolChange("b", items).Should().BeFalse();
        await KeeperService.ShouldReloadCoreForPoolChange("gone", items).Should().BeTrue();
        await KeeperService.ShouldReloadCoreForPoolChange("", items).Should().BeTrue();
    }

    // ---- 节点全死（探测全失败）且连续两轮换血无解才回退直连；只是慢或短暂抖动不回退 ----
    [Test]
    public async Task AllDeadFallsBackToDirectAfterTwoFailedRounds()
    {
        await KeeperService.ShouldFallbackAllDead(false, 1).Should().BeFalse();
        await KeeperService.ShouldFallbackAllDead(false, 2).Should().BeTrue();
        await KeeperService.ShouldFallbackAllDead(true, 5).Should().BeFalse();
    }

    // ---- 驱逐下限：剔完至少留 MinAlive 条；池子本来就不足时一条都不剔 ----
    [Test]
    public async Task EvictionBudgetKeepsMinAlive()
    {
        await KeeperService.EvictionBudget(12, 3).Should().BeEqualTo(9);
        await KeeperService.EvictionBudget(3, 3).Should().BeEqualTo(0);
        await KeeperService.EvictionBudget(2, 3).Should().BeEqualTo(0);
        await KeeperService.EvictionBudget(10, 0).Should().BeEqualTo(10);
    }

    // ---- 刷新失败指数退避：15 分钟起翻倍，封顶为换血周期，最长不超过 24 小时 ----
    [Test]
    public async Task RefreshRetryBacksOff()
    {
        var interval = TimeSpan.FromHours(12);
        await KeeperService.RefreshRetryDelay(1, interval).Should().BeEqualTo(TimeSpan.FromMinutes(15));
        await KeeperService.RefreshRetryDelay(3, interval).Should().BeEqualTo(TimeSpan.FromMinutes(60));
        await KeeperService.RefreshRetryDelay(10, interval).Should().BeEqualTo(interval);
        await KeeperService.RefreshRetryDelay(1, TimeSpan.FromMinutes(1)).Should().BeEqualTo(TimeSpan.FromMinutes(15));
        await KeeperService.RefreshRetryDelay(20, TimeSpan.FromHours(100)).Should().BeEqualTo(TimeSpan.FromHours(24));
    }

    // ---- DNS 过滤：只留公网地址；全是内网地址返回空 ----
    [Test]
    public async Task DnsKeepsPublicAddressesOnly()
    {
        var kept = KeeperService.OnlyPublicAddresses([IPAddress.Parse("8.8.8.8"), IPAddress.Parse("192.168.1.1")]);
        await kept.Length.Should().BeEqualTo(1);
        await kept[0].Should().BeEqualTo(IPAddress.Parse("8.8.8.8"));
        await KeeperService.OnlyPublicAddresses([IPAddress.Parse("10.0.0.1")]).Length.Should().BeEqualTo(0);
    }

    // ---- 连接被拒 / 超时属瞬态，不打堆栈；其它异常照常记录 ----
    [Test]
    public async Task TransientConnectErrorsAreQuiet()
    {
        var refused = new HttpRequestException("refused", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        await KeeperService.IsTransientConnectError(refused).Should().BeTrue();
        await KeeperService.IsTransientConnectError(new InvalidOperationException("x")).Should().BeFalse();
    }

    // ---- 内核在连接状态下意外退出才回退直连；启动宽限期内不误判 ----
    [Test]
    public async Task FallbackToDirectOnlyWhenCoreDiesWhileConnected()
    {
        static bool Fallback(ESysProxyType type, bool running, double seconds, double grace)
            => KeeperService.ShouldFallbackToDirect(type, running, seconds, grace);

        await Fallback(ESysProxyType.ForcedChange, false, 40, 30).Should().BeTrue();
        await Fallback(ESysProxyType.ForcedChange, true, 40, 30).Should().BeFalse();
        await Fallback(ESysProxyType.ForcedChange, false, 10, 30).Should().BeFalse();
        await Fallback(ESysProxyType.ForcedClear, false, 40, 30).Should().BeFalse();
        await Fallback(ESysProxyType.Unchanged, false, 40, 30).Should().BeFalse();
    }

    // ---- 控制端口只在首次解析时算一次，TUN 状态变化不得让 Keeper 与内核配置读到不同端口 ----
    [Test]
    public async Task ControllerPortOffsetIsResolvedOnce()
    {
        await ServiceLib.Manager.AppManager.ResolveControllerPort(10813, false).Should().BeEqualTo(10813);
        await ServiceLib.Manager.AppManager.ResolveControllerPort(10813, true).Should().BeEqualTo(10814);
    }

    // ---- 节点全死挂起态：一律不拉内核，避免 TUN 重建让整机断网；解除后才按连接/常驻恢复 ----
    [Test]
    public async Task ProxySuspendedNeverStartsCore()
    {
        // 挂起态：无论连接还是常驻，都不拉内核
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedChange, true, true).Should().BeFalse();
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear, true, true).Should().BeFalse();
        // 解除挂起：连接态 / 常驻态恢复拉内核，纯断开态不拉
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedChange, false, false).Should().BeTrue();
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear, true, false).Should().BeTrue();
        await KeeperService.CoreShouldRun(ESysProxyType.ForcedClear, false, false).Should().BeFalse();
    }

    // ---- TUN 只是连接方式：开关仍是「连接」；开启需要管理员权限 ----
    [Test]
    public async Task TunModeSwitchDecisions()
    {
        await ServiceLib.Handler.TunModeSwitch.StartupAction(false, false).Should().BeEqualTo(ServiceLib.Handler.TunStartupAction.None);
        await ServiceLib.Handler.TunModeSwitch.StartupAction(true, true).Should().BeEqualTo(ServiceLib.Handler.TunStartupAction.None);
        await ServiceLib.Handler.TunModeSwitch.StartupAction(true, false).Should().BeEqualTo(ServiceLib.Handler.TunStartupAction.RequestElevation);

        var config = new Config { TunModeItem = new TunModeItem { EnableTun = false } };
        // 管理员：直接开启
        await ServiceLib.Handler.TunModeSwitch.Apply(config, true, true, null).Should().BeEqualTo(ServiceLib.Handler.TunSwitchResult.Applied);
        await config.TunModeItem.EnableTun.Should().BeTrue();
        await ServiceLib.Handler.TunModeSwitch.Apply(config, true, true, null).Should().BeEqualTo(ServiceLib.Handler.TunSwitchResult.Unchanged);
        // 非管理员 + 同意提权：保持开启，由调用方以管理员身份重启
        config.TunModeItem.EnableTun = false;
        await ServiceLib.Handler.TunModeSwitch.Apply(config, true, false, () => true).Should().BeEqualTo(ServiceLib.Handler.TunSwitchResult.Restarting);
        await config.TunModeItem.EnableTun.Should().BeTrue();
        // 关闭
        await ServiceLib.Handler.TunModeSwitch.Apply(config, false, false, null).Should().BeEqualTo(ServiceLib.Handler.TunSwitchResult.Applied);
        // 非管理员 + 拒绝提权：回退为关闭
        await ServiceLib.Handler.TunModeSwitch.Apply(config, true, false, () => false).Should().BeEqualTo(ServiceLib.Handler.TunSwitchResult.Denied);
        await config.TunModeItem.EnableTun.Should().BeFalse();
    }

    // ---- 新安装默认开启 TUN（可在设置里关闭）----
    [Test]
    public async Task TunDefaultOnForFreshInstall()
    {
        await (new TunModeItem().EnableTun).Should().BeTrue();
    }

    // ---- 防抖：全池都超延迟上限时，在用节点不明显差于最优（≤1.3 倍）就保持；差太多或有达标节点才切 ----
    [Test]
    public async Task RankAutoKeepsCurrentWhenWholePoolOverCapAndComparable()
    {
        await Rank(Rows(("current", 3, 1, 1, 1017), ("best", 3, 1, 1, 1012)), "current", null, 800).Should().BeEqualTo("current");
        await Rank(Rows(("current", 3, 1, 1, 1300), ("best", 3, 1, 1, 1000)), "current", null, 800).Should().BeEqualTo("current");
        await Rank(Rows(("current", 3, 1, 1, 1500), ("best", 3, 1, 1, 1000)), "current", null, 800).Should().BeEqualTo("best");
        await Rank(Rows(("current", 3, 1, 1, 1017), ("best", 3, 1, 1, 700)), "current", null, 800).Should().BeEqualTo("best");
    }

    // ---- 稳定评级只用可核对的记录：出现天数 + 实测通过率；记录不足 1 天不能证明稳定 ----
    [Test]
    public async Task StabilityGradeUsesDaysAndPassRate()
    {
        var now = new DateTime(2026, 11, 24, 18, 0, 0);
        static string Grade(KeeperQuality? quality, DateTime at) => ((ValueTuple<string, string>)InvokeStatic("EvaluateStability", quality, at)!).Item1;
        var solid = new KeeperQuality { FirstSeen = now.AddDays(-5), Samples = [100, 120, 110, 130] };
        await Grade(solid, now).Should().BeEqualTo("高");
        var shaky = new KeeperQuality { FirstSeen = now.AddDays(-5), Samples = [100, 0, 110, 0, 130, 0] };
        await Grade(shaky, now).Should().BeEqualTo("低");
        var fresh = new KeeperQuality { FirstSeen = now.AddHours(-3), Samples = [100, 120] };
        await Grade(fresh, now).Should().BeEqualTo("低");
        var young = new KeeperQuality { FirstSeen = now.AddDays(-2), Samples = [100, 0, 110, 120] };
        await Grade(young, now).Should().BeEqualTo("中");
        await Grade(null, now).Should().BeEqualTo("低");
    }

    // ---- 启动一律未连接：上次保存的「已连接」不随启动恢复 ----
    [Test]
    public async Task StartupAlwaysDisconnected()
    {
        foreach (var saved in new[] { ESysProxyType.ForcedClear, ESysProxyType.ForcedChange, ESysProxyType.Unchanged, ESysProxyType.Pac })
        {
            await ServiceLib.Handler.ConfigHandler.StartupSysProxyType(saved).Should().BeEqualTo(ESysProxyType.ForcedClear);
        }
    }
}
