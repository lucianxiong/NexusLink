using NexusLink.Manager;

namespace NexusLink.Views;

public partial class StatusBarView
{
    private static Config _config;

    public StatusBarView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;

        menuExit.Click += menuExit_Click;
        txtRunningServerDisplay.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;
        txtRunningInfoDisplay.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;

        this.WhenActivated(disposables =>
        {
            //tray menu
            tbNotify.ToolTipText = "畅连";
            this.OneWayBind(ViewModel, vm => vm.NotifyLeftClickCmd, v => v.tbNotify.LeftClickCommand).DisposeWith(disposables);

            //status bar
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.cmbRoutings2.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.cmbRoutings2.Visibility).DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(interaction =>
            {
                var strData = interaction.Input;
                WindowsUtils.SetClipboardData(strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Application.Current?.Dispatcher.Invoke(async () => await RefreshIcon(), DispatcherPriority.Normal);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });

        _ = RefreshIcon();
    }

    private async Task RefreshIcon()
    {
        tbNotify.Icon ??= new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "tray-bolt.ico"));
        await Task.CompletedTask;
        Application.Current.MainWindow?.Icon = WindowsManager.Instance.GetAppIcon(_config);
    }

    // 托盘菜单只呈现应用真实具备的能力：连接/断开、刷新与测速、打开窗口、退出。
    // 节点切换统一在「节点管理」页完成（经策略组，失效可兜底）；托盘不再直接改激活服务器。
    private void TrayMenu_Opened(object sender, RoutedEventArgs e)
    {
        var connected = _config.SystemProxyItem.SysProxyType == ServiceLib.Enums.ESysProxyType.ForcedChange;
        menuTrayConnect.Header = connected ? "断开连接" : "立即连接";
        var st = ServiceLib.Services.KeeperService.Instance.State;
        // 托盘状态行只显示应用自己检测到的出口（国旗 国家 城市）
        var place = $"{st.ExitCountry} {st.ExitCity}".Trim();
        menuTrayStatus.Header = connected
        ? "畅连 · 已连接 · " + (place.Length > 0 ? (ServiceLib.Services.KeeperService.FlagOf(st.ExitCode) + " " + place).Trim() : st.ExitNode)
        : "畅连 · 未连接（直连）";
    }

    private async void TrayConnect_Click(object sender, RoutedEventArgs e)
    {
        var connected = _config.SystemProxyItem.SysProxyType == ServiceLib.Enums.ESysProxyType.ForcedChange;
        await ServiceLib.Services.KeeperService.Instance.SetConnectedAsync(!connected);
    }

    private void TrayRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = ServiceLib.Services.KeeperService.Instance.RefreshAsync();
    }

    private void TrayShowMain_Click(object sender, RoutedEventArgs e)
    {
        var win = Application.Current?.MainWindow;
        if (win == null)
        {
            return;
        }
        win.Show();
        if (win.WindowState == WindowState.Minimized)
        {
            win.WindowState = WindowState.Normal;
        }
        win.Activate();
    }

    private async void menuExit_Click(object sender, RoutedEventArgs e)
    {
        tbNotify.Dispose();
        await AppManager.Instance.AppExitAsync(true);
    }

    private void txtRunningInfoDisplay_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}


