using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.Startup;

/// <summary>
/// 启动路径冒烟测试：按 App.OnStartup 的顺序创建主窗口视图模型。
/// 这条路径上任何一个视图模型的构造函数抛异常，应用都会打不开（没有主窗口、也没有提示）。
/// 配置用真实启动时的 LoadConfig（出厂默认值），不用测试专用的精简配置。
/// </summary>
public class StartupPathTests
{
    [Test]
    public async Task MainWindowViewModel_CanBeCreated_LikeOnStartup()
    {
        var config = ConfigHandler.LoadConfig();
        await config.Should().NotBeNull();
        CoreConfigTestFactory.BindAppManagerConfig(config!);

        // 与 App.OnStartup 一致：InitApp 之后直接创建主窗口视图模型
        SQLiteHelper.Instance.CreateTable<SubItem>();
        SQLiteHelper.Instance.CreateTable<ProfileItem>();
        SQLiteHelper.Instance.CreateTable<RoutingItem>();
        SQLiteHelper.Instance.CreateTable<ProfileExItem>();
        SQLiteHelper.Instance.CreateTable<DNSItem>();
        SQLiteHelper.Instance.CreateTable<FullConfigTemplateItem>();

        // 未连接：启动时不应拉起内核
        config!.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;

        var vm = new MainWindowViewModel();

        await Task.Delay(300);

        await StatusBarViewModel.Instance.Should().NotBeNull();
        await StatusBarViewModel.Instance.NotifyLeftClickCmd.Should().NotBeNull();
        await vm.StatusBarViewModel.Should().BeSameReferenceAs(StatusBarViewModel.Instance);
    }
}
