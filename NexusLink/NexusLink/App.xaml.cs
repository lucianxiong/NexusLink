using NexusLink.Manager;
using NexusLink.Views;

namespace NexusLink;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    public static EventWaitHandle ProgramStarted;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    /// <summary>
    /// Open only one process
    /// </summary>
    /// <param name="e"></param>
    protected override void OnStartup(StartupEventArgs e)
    {
        var exePathKey = Utils.GetMd5(Utils.GetExePath());

        var rebootas = e.Args.Any(t => t == Global.RebootAs);
        ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
        if (!rebootas && !bCreatedNew)
        {
            ProgramStarted.Set();
            Environment.Exit(0);
            return;
        }

        if (!AppManager.Instance.InitApp())
        {
            UI.Show($"Loading GUI configuration file is abnormal,please restart the application{Environment.NewLine}加载GUI配置文件异常,请重启应用");
            Environment.Exit(0);
            return;
        }

        // TUN 模式需要管理员权限：已开启但当前不是管理员 → 请求以管理员身份重启；被拒绝 → 关闭 TUN 并说明
        if (ServiceLib.Handler.TunModeSwitch.StartupAction(AppManager.Instance.Config.TunModeItem.EnableTun, Utils.IsAdministrator())
            == ServiceLib.Handler.TunStartupAction.RequestElevation)
        {
            if (ProcUtils.RebootAsAdmin())
            {
                _ = ServiceLib.Handler.SysProxy.SysProxyHandler.UpdateSysProxy(AppManager.Instance.Config, true);
                Logging.SaveLog("TUN mode: restarting as administrator");
                Shutdown();
                return;
            }
            AppManager.Instance.Config.TunModeItem.EnableTun = false;
            _ = ConfigHandler.SaveConfig(AppManager.Instance.Config);
            Logging.SaveLog("TUN mode: administrator permission declined, TUN disabled");
            UI.Show("未获得管理员权限，TUN 模式已关闭。需要时可在「设置」里重新开启。");
        }

        AppManager.Instance.WindowDialog = new WindowDialog();

        AppManager.Instance.InitComponents();

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithWpf()
            .BuildApp();

        base.OnStartup(e);

        var mainWindowViewModel = new MainWindowViewModel();
        var viewFor = SimpleViewLocator.Instance.ResolveView(mainWindowViewModel);
        viewFor!.ViewModel = mainWindowViewModel;

        var mainWindow = (MainWindow)viewFor;
        mainWindow.Show();
        MainWindow = mainWindow;

        _ = ServiceLib.Services.KeeperService.Instance.StartAsync(AppManager.Instance.Config, async () =>
        {
            await mainWindowViewModel.Reload();
        });
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.SaveLog("App_DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        // 被正常取消 / 释放的连接留下的"已中止 I/O"是良性噪声，不算错误
        if (e.Exception?.InnerExceptions.Count > 0
            && e.Exception.InnerExceptions.All(ex => ex is System.Net.Sockets.SocketException socket
                && socket.SocketErrorCode is System.Net.Sockets.SocketError.OperationAborted or System.Net.Sockets.SocketError.ConnectionAborted))
        {
            return;
        }
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.SaveLog("OnExit");
        base.OnExit(e);
        Process.GetCurrentProcess().Kill();
    }
}
