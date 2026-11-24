namespace ServiceLib.ViewModels;

public partial class MainWindowViewModel : MyReactiveObject
{
    public Interaction<bool?, RxVoid> ShowHideWindowInteraction { get; } = new();

    public StatusBarViewModel StatusBarViewModel { get; } = StatusBarViewModel.Instance;

    #region Init

    public MainWindowViewModel()
    {
        StatusBarViewModel.ReloadRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async _ => await Reload());

        StatusBarViewModel.ShowHideWindowRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async blShow =>
            {
                await ShowHideWindowInteraction.HandleSafe(blShow);
            });

        _ = Init();
    }

    private async Task Init()
    {
        AppManager.Instance.ShowInTaskbar = true;

        await ConfigHandler.InitBuiltinDNS(_config);
        await ProfileExManager.Instance.Init();
        await CoreManager.Instance.Init(_config, UpdateHandler);
        await CertPemManager.Instance.Init(_config);
        TaskManager.Instance.RegUpdateTask(_config, UpdateTaskHandler);

        await Reload();
    }

    #endregion Init

    #region Actions

    private async Task UpdateHandler(bool notify, string msg)
    {
        NoticeManager.Instance.SendMessage(msg);
        if (notify)
        {
            NoticeManager.Instance.Enqueue(msg);
        }
        await Task.CompletedTask;
    }

    private async Task UpdateTaskHandler(bool success, string msg)
    {
        NoticeManager.Instance.SendMessageEx(msg);
        if (!success)
        {
            return;
        }
        // 没有分组，或当前节点仍属于当前分组时，重新加载内核
        var profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (_config.SubIndexId.IsNullOrEmpty() || profile?.Subid == _config.SubIndexId)
        {
            await Reload();
        }
    }

    #endregion Actions

    #region core job

    private bool _hasNextReloadJob = false;
    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);

    public async Task Reload()
    {
        //If there are unfinished reload job, marked with next job.
        if (!await _reloadSemaphore.WaitAsync(0))
        {
            _hasNextReloadJob = true;
            return;
        }

        try
        {
            var profileItem = await ConfigHandler.GetDefaultServer(_config);
            if (profileItem == null)
            {
                NoticeManager.Instance.Enqueue(ResUI.CheckServerSettings);
                return;
            }
            var allResult = await CoreConfigContextBuilder.BuildAll(_config, profileItem);
            if (NoticeManager.Instance.NotifyValidatorResult(allResult.CombinedValidatorResult) && !allResult.Success)
            {
                return;
            }

            await Task.Run(async () =>
            {
                // 未连接时不加载内核：启动、断开状态下改设置只更新配置，点「连接」时才真正拉起内核
                if (ServiceLib.Services.KeeperService.CoreShouldRun(_config.SystemProxyItem.SysProxyType))
                {
                    await LoadCore(allResult.MainResult.Context, allResult.PreSocksResult?.Context);
                }
                await SysProxyHandler.UpdateSysProxy(_config, false);
                await Task.Delay(1000);
            });
            RxSchedulers.MainThreadScheduler.Schedule(async () =>
            {
                if (!ServiceLib.Services.KeeperService.CoreShouldRun(_config.SystemProxyItem.SysProxyType))
                {
                    return; // 内核没在运行，不做连通测试（否则每次启动都会提示一次失败）
                }
                await TestServerAvailability();
            });
        }
        finally
        {
            _reloadSemaphore.Release();
            //If there is a next reload job, execute it.
            if (_hasNextReloadJob)
            {
                _hasNextReloadJob = false;
                await Reload();
            }
        }
    }

    // 内核运行时做一次真实连通测试，把出口 IP 与延迟写进当前节点的实测信息
    private async Task TestServerAvailability()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            return;
        }
        var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck);
        var msg = string.Format(ResUI.TestMeOutput, result.Time, result.Ip);
        var ip = result.GetValidIp();
        if (ip.IsNotEmpty())
        {
            ProfileExManager.Instance.SetTestIpInfo(item.IndexId, ip);
        }
        if (result.Time > 0)
        {
            ProfileExManager.Instance.SetTestDelay(item.IndexId, result.Time);
        }
        NoticeManager.Instance.SendMessageEx(msg);
    }

    private async Task LoadCore(CoreConfigContext? mainContext, CoreConfigContext? preContext)
    {
        await CoreManager.Instance.LoadCore(mainContext, preContext);
    }

    #endregion core job
}
