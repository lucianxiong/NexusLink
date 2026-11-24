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

    public KeeperHomeView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateStatus();
        Loaded += (_, _) =>
        {
            UpdateStatus();
            _timer.Start();
        };
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

    private void PoolChanged(object sender, SelectionChangedEventArgs e) => UpdateNodes();

    private void SearchChanged(object sender, TextChangedEventArgs e) => UpdateNodes();

    private void UpdateNodes()
    {
        if (_nodes == null || _backupNodes == null || Nodes == null || Search == null || PoolFilter == null || NodeSummary == null)
        {
            return;
        }
        var search = Search.Text.Trim();
        var backup = PoolFilter.SelectedIndex == 1;
        var pool = backup ? _backupNodes : _nodes;
        var rows = pool.Where(node => node.Remarks.Contains(search, StringComparison.OrdinalIgnoreCase)
                                   || node.Host.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var quality = _keeper.State.QualityHistory.GetValueOrDefault(node.QualityKey);
                var latest = quality?.Samples.LastOrDefault();
                return new
                {
                    node.Remarks, node.Host, node.Port,
                    Source = node.Source.Length > 0 ? node.Source : "—",
                    Security = node.Security.Length > 0 ? node.Security : "—",
                    LatencyMs = latest > 0 ? latest : null,
                    SuccessRate = quality is { Samples.Count: > 0 }
                        ? (double?)quality.Samples.Count(sample => sample > 0) / quality.Samples.Count : null,
                    TestedAt = (DateTime?)quality?.TestedAt,
                };
            }).ToList();
        Nodes.ItemsSource = rows;
        var audit = _keeper.State.SecurityAudit;
        var auditLine = audit != null && audit.At.Length > 0
            ? $" · 最近审查 {audit.At}：候选 {audit.Candidates}，拦截 {audit.Rejected} 个不安全节点" + (audit.Evicted > 0 ? $"，出池 {audit.Evicted} 个" : string.Empty)
            : string.Empty;
        NodeSummary.Text = backup && pool.Count == 0 && _keeper.State.BackupCount > 0
            ? $"已有 {_keeper.State.BackupCount} 个备用节点，刷新后显示明细" + auditLine
            : $"显示 {rows.Count} / {pool.Count} 个节点" + auditLine;
    }

    private void UpdateStatus()
    {
        var state = _keeper.State;
        NodesRefreshButton.IsEnabled = !_keeper.IsRunning;
        AutoSwitch.IsChecked = state.AutoSwitch;
        AutoRefresh.IsChecked = state.DailyRefresh;
        Interval.Text = $"每 {state.RefreshIntervalHours} 小时更新节点来源并测速。";
        Busy.Visibility = _keeper.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        TaskStatus.Text = state.ReachableSources > 0
            ? $"{_keeper.RefreshStatus} · {state.ReachableSources} 个来源有候选节点"
            : _keeper.RefreshStatus;

        if (!ReferenceEquals(_nodes, state.Nodes) || !ReferenceEquals(_backupNodes, state.BackupNodes)
            || !ReferenceEquals(_qualityHistory, state.QualityHistory))
        {
            _nodes = state.Nodes;
            _backupNodes = state.BackupNodes;
            _qualityHistory = state.QualityHistory;
            UpdateNodes();
        }
    }

}

