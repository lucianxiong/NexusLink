namespace ServiceLib.Handler;

/// <summary>
// TUN 模式开关的决策逻辑（纯逻辑，便于测试）。TUN 需要管理员权限才能创建虚拟网卡。
// </summary>
public static class TunModeSwitch
{
    /// <summary>启动时：配置里 TUN 已开启，但当前不是管理员 → 需要请求以管理员身份重启</summary>
    public static TunStartupAction StartupAction(bool tunEnabled, bool isAdmin)
    {
        return !tunEnabled || isAdmin ? TunStartupAction.None : TunStartupAction.RequestElevation;
    }

    /// <summary>
    /// 切换 TUN 模式：只修改 config.TunModeItem.EnableTun 并返回下一步，保存配置与后续动作由调用方完成。
    /// 非管理员开启时：先置为开启并请求提权（以管理员重启后生效）；被拒绝则回退为关闭。
    /// </summary>
    public static TunSwitchResult Apply(Config config, bool enable, bool isAdmin, Func<bool>? requestElevation)
    {
        if (config.TunModeItem.EnableTun == enable)
        {
            return TunSwitchResult.Unchanged;
        }
        if (!enable || isAdmin)
        {
            config.TunModeItem.EnableTun = enable;
            return TunSwitchResult.Applied;
        }
        config.TunModeItem.EnableTun = true;
        if (requestElevation?.Invoke() == true)
        {
            return TunSwitchResult.Restarting;
        }
        config.TunModeItem.EnableTun = false;
        return TunSwitchResult.Denied;
    }
}

public enum TunStartupAction
{
    None,
    RequestElevation,
}

public enum TunSwitchResult
{
    Unchanged,
    Applied,
    Restarting,
    Denied,
}
