using System.Windows.Controls;
using System.Windows.Threading;
using ServiceLib.Services;

namespace NexusLink.Views;

public partial class KeeperHomeView
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly KeeperService _keeper = KeeperService.Instance;
    private List<KeeperNode>? _nodes;
    private List<KeeperNode>? _backupNodes;
    private Dictionary<string, KeeperQuality>? _qualityHistory;
    private bool _syncing;
    private string _rowsSignature = string.Empty; // 节点表行内容的依赖快照（在用节点 / 延迟上限 / 内核测速）

    public KeeperHomeView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateStatus();
        Loaded += (_, _) =>
        {
            UpdateStatus();
            _timer.Start();
        };
        Nodes.SelectionChanged += (_, _) => UseNodeButton.IsEnabled = Nodes.SelectedItem != null;
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        if (PageTitle == null || HomePage == null || NodesPage == null || AutoPage == null || SettingsPage == null
            || sender is not RadioButton { Tag: string tag, Content: string title })
        {
            return;
        }
        PageTitle.Text = title;
        var pages = new FrameworkElement[] { HomePage, NodesPage, AutoPage, SettingsPage };
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = tag == i.ToString() ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        NodesRefreshButton.IsEnabled = false;
        await _keeper.RefreshAsync();
        UpdateStatus();
    }

    // 手动切换：选中一行后点「用这个节点」，或直接双击该行
    private async void UseSelectedNode(object sender, RoutedEventArgs e) => await PinSelectedAsync();

    private async void NodesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Nodes, source) is DataGridRow)
        {
            await PinSelectedAsync();
        }
    }

    private async Task PinSelectedAsync()
    {
        if (Nodes.SelectedItem is not { } row
            || row.GetType().GetProperty("IndexId")?.GetValue(row) is not string indexId || indexId.Length == 0)
        {
            return;
        }
        UseNodeButton.IsEnabled = false;
        var ok = await _keeper.PinNodeAsync(indexId);
        NoticeManager.Instance.SendMessage(ok
            ? $"畅连：已切换到「{NodeNameOf(_keeper.State.PinnedIndexId, _keeper.State.PinnedRemarks)}」"
            : "畅连：切换失败，该节点当前不可用，请换一个试试");
        UpdateStatus();
    }

    // 恢复自动：交还给 安全 > 稳定 > 延迟 的自动选路
    private async void UseAutoSelect(object sender, RoutedEventArgs e)
    {
        AutoSelectButton.IsEnabled = false;
        await _keeper.UnpinAsync();
        NoticeManager.Instance.SendMessage("畅连：已恢复自动选路");
        UpdateStatus();
    }

    private void LatencyLimitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || !IsLoaded)
        {
            return;
        }
        _keeper.SetMaxLatency(LatencyLimit.SelectedIndex switch { 0 => 500, 2 => 1200, _ => 800 });
        UpdateNodes();
    }

    private async void SwitchChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            await _keeper.SetAutoSwitchAsync(AutoSwitch.IsChecked == true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Keeper auto switch", ex);
            NoticeManager.Instance.SendMessage("自动切换设置未能应用，请查看运行日志。");
        }
        UpdateStatus();
    }

    private void RefreshChanged(object sender, RoutedEventArgs e)
    {
        _keeper.SetDailyRefresh(AutoRefresh.IsChecked == true);
        UpdateStatus();
    }

    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        await AppManager.Instance.WindowDialog.ShowDialogAsync(new ServiceLib.ViewModels.OptionSettingViewModel());
        UpdateStatus();
    }

    // 界面上显示的节点名 = 应用自己检测到的出口 + 协议（不采用节点池写的名称）
    private string NodeNameOf(string indexId, string fallback)
        => _keeper.State.Nodes.FirstOrDefault(t => t.IndexId.Length > 0 && t.IndexId == indexId)?.Name ?? fallback;

    private void PoolChanged(object sender, SelectionChangedEventArgs e) => UpdateNodes();

    private void SearchChanged(object sender, TextChangedEventArgs e) => UpdateNodes();

    // 内核最近一次测速（每次状态轮询同步）：-1 = 内核无记录，0 = 测速未通过
    private int KernelDelay(string indexId)
        => indexId.Length > 0 && _keeper.State.KernelDelays.TryGetValue(indexId, out var delay) ? delay : -1;

    private string KernelTime(string indexId)
        => _keeper.State.KernelDelayTimes.TryGetValue(indexId, out var time) && time.Length > 0 ? "（" + time + "）" : string.Empty;

    private void UpdateNodes()
    {
        if (_nodes == null || _backupNodes == null || Nodes == null || Search == null || PoolFilter == null || NodeSummary == null)
        {
            return;
        }
        var search = Search.Text.Trim();
        var backup = PoolFilter.SelectedIndex == 1;
        var pool = backup ? _backupNodes : _nodes;
        var rows = pool.Where(node => node.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || node.Remarks.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || node.Host.Contains(search, StringComparison.OrdinalIgnoreCase) || node.ExitIp.Contains(search, StringComparison.OrdinalIgnoreCase) || node.Source.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var quality = _keeper.State.QualityHistory.GetValueOrDefault(node.QualityKey);
                var latest = quality?.Samples.LastOrDefault();
                return new
                {
                    node.IndexId,
                    Name = node.Name, NameSub = node.ExitIp, node.Host, node.Port,
                    Exit = node.Exit.Length > 0 ? node.Exit : "—",
                    NetType = node.NetType.Length > 0 ? node.NetType : "—",
                    Stability = node.Stability.Length > 0 ? node.Stability : "—",
                    Source = node.Source.Length > 0 ? node.Source : "—",
                    Security = node.Security.Length > 0 ? node.Security : "—",
                    State = node.IndexId.Length > 0 && node.IndexId == _keeper.State.ActiveIndexId
                        ? (_keeper.IsConnected ? "使用中" : "当前线路")
                        : string.Empty,
                    LatencyMs = KernelDelay(node.IndexId) > 0 ? KernelDelay(node.IndexId) : latest > 0 ? latest : null,
                    Speed = node.Speed.Length > 0 ? node.Speed : "—",
                    Tip = string.Join(" · ", new[]
                    {
                        node.Remarks.Length > 0 ? "节点池原名：" + node.Remarks : string.Empty,
                        node.Isp,
                        node.ProxyFlag ? "已知代理出口" : string.Empty,
                        quality is { Samples.Count: > 0 } ? $"通过率 {quality.Samples.Count(sample => sample > 0) * 100 / quality.Samples.Count}%" : string.Empty,
                        node.ExitIp.Length > 0 ? "出口 IP " + node.ExitIp : string.Empty,
                        node.Security.Length > 0 ? "安全 " + node.Security + (node.SecurityReason.Length > 0 ? "（" + node.SecurityReason + "）" : string.Empty) : string.Empty,
                        node.Stability.Length > 0 ? "稳定 " + node.Stability + "：" + node.StabilityReason : string.Empty,
                        node.NameNote,
                        KernelDelay(node.IndexId) switch
                        {
                            > 0 => "内核测速 " + KernelDelay(node.IndexId) + " ms" + KernelTime(node.IndexId),
                            0 => "内核最近一次测速未通过",
                            _ => latest > 0 ? "上次换血实测 " + latest + " ms" : string.Empty,
                        },
                        node.Host + ":" + node.Port,
                    }.Where(part => part.Length > 0)),
                };
            }).ToList();
        // 重建行之前记住用户选中的节点，重建后还原，避免内核测速一更新就把选中行冲掉
        var selectedId = Nodes.SelectedItem is { } selectedRow
            ? selectedRow.GetType().GetProperty("IndexId")?.GetValue(selectedRow) as string
            : null;
        Nodes.ItemsSource = rows;
        if (!string.IsNullOrEmpty(selectedId))
        {
            Nodes.SelectedItem = rows.FirstOrDefault(row => row.GetType().GetProperty("IndexId")?.GetValue(row) as string == selectedId);
        }
        var audit = _keeper.State.SecurityAudit;
        var auditLine = audit != null && audit.At.Length > 0
            ? $" · 最近审查 {audit.At}：候选 {audit.Candidates}，拦截 {audit.Rejected} 个不安全节点" + (audit.Evicted > 0 ? $"，出池 {audit.Evicted} 个" : string.Empty)
            : string.Empty;
        NodeSummary.Text = backup && pool.Count == 0 && _keeper.State.BackupCount > 0
            ? $"{_keeper.State.BackupCount} 个安全节点实测延迟超过上限，未入池" + auditLine
            : $"显示 {rows.Count} / {pool.Count} 个节点 · 延迟上限 {_keeper.State.MaxLatencyMs} ms" + auditLine;
    }

    private void UpdateStatus()
    {
        var state = _keeper.State;
        NodesRefreshButton.IsEnabled = !_keeper.IsRunning;
        AutoSwitch.IsChecked = state.AutoSwitch;
        AutoRefresh.IsChecked = state.DailyRefresh;
        Interval.Text = $"每 {state.RefreshIntervalHours} 小时更新节点来源并测速。";
        SelectModeText.Text = state.SelectMode.Length > 0 ? "选路：" + state.SelectMode : string.Empty;
        AutoSelectButton.IsEnabled = state.PinnedIndexId.Length > 0;
        if (!_syncing)
        {
            _syncing = true;
            LatencyLimit.SelectedIndex = state.MaxLatencyMs switch { 500 => 0, 1200 => 2, _ => 1 };
            _syncing = false;
        }
        Busy.Visibility = _keeper.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        TaskStatus.Text = state.ReachableSources > 0
            ? $"{_keeper.RefreshStatus} · {state.ReachableSources} 个来源有候选节点"
            : _keeper.RefreshStatus;

        // 行内容（使用中 / 延迟）依赖内核状态，每次轮询都核对；不能只在节点列表对象变化时才重建
        var rowsSignature = state.ActiveIndexId + "|" + state.MaxLatencyMs + "|"
                            + string.Join(",", state.KernelDelays.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        if (!ReferenceEquals(_nodes, state.Nodes) || !ReferenceEquals(_backupNodes, state.BackupNodes)
            || !ReferenceEquals(_qualityHistory, state.QualityHistory) || rowsSignature != _rowsSignature)
        {
            _nodes = state.Nodes;
            _backupNodes = state.BackupNodes;
            _qualityHistory = state.QualityHistory;
            _rowsSignature = rowsSignature;
            UpdateNodes();
        }
    }

}

