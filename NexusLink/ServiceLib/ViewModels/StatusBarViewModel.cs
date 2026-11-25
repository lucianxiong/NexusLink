namespace ServiceLib.ViewModels;

/// <summary>
/// 托盘与系统代理的视图模型：托盘左键、显示或隐藏主窗口、切换系统代理（全局热键）。
/// </summary>
public partial class StatusBarViewModel : MyReactiveObject
{
    public Interaction<RxVoid, RxVoid> DispatcherRefreshIconInteraction { get; } = new();
    public EventChannel<bool?> ShowHideWindowRequested { get; } = new();
    public EventChannel<RxVoid> ReloadRequested { get; } = new();

    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new());
    public static StatusBarViewModel Instance => _instance.Value;

    public ReactiveCommand<RxVoid, RxVoid> NotifyLeftClickCmd { get; }

    public StatusBarViewModel()
    {
        _config = AppManager.Instance.Config;

        // TUN 需要管理员权限；没有权限时关闭，避免内核启动失败
        if (_config.TunModeItem.EnableTun && AllowEnableTun() == false)
        {
            _config.TunModeItem.EnableTun = false;
        }

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await SetListenerType(result));

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigHandler.InitBuiltinRouting(_config);
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, false);
    }

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        await SysProxyHandler.UpdateSysProxy(_config, false);
        if (blChange)
        {
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }
}
