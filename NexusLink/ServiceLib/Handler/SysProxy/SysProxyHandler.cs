namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";
    private static readonly Lazy<PacManager> _pacManager = new(() => new PacManager());

    private static string? _activeWindowsPacUrl;

    internal static bool HasExternalWindowsProxy(Config config)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var settings = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        var (expected, _) = GetWindowsProxyString(config, port);
        return (settings?.GetValue("AutoConfigURL") is string { Length: > 0 } pacUrl
                && !string.Equals(pacUrl, _activeWindowsPacUrl, StringComparison.Ordinal))
            || settings?.GetValue("ProxyEnable") is 1
                && !string.Equals(settings.GetValue("ProxyServer") as string, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable)
    {
        var type = config.SystemProxyItem.SysProxyType;

        if (forceDisable && type != ESysProxyType.Unchanged)
        {
            type = ESysProxyType.ForcedClear;
        }

        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port <= 0)
            {
                return false;
            }
            if (OperatingSystem.IsWindows() && type is ESysProxyType.ForcedChange or ESysProxyType.ForcedClear or ESysProxyType.Pac
                && HasExternalWindowsProxy(config))
            {
                config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged;
                return false;
            }
            switch (type)
            {
                case ESysProxyType.ForcedChange when Utils.IsWindows():
                    var (strProxy, strExceptions) = GetWindowsProxyString(config, port);
                    ProxySettingWindows.SetProxy(strProxy, strExceptions, 2);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsLinux():
                    var exceptions = SanitizeExceptions(config);
                    await ProxySettingLinux.SetProxy(Global.Loopback, port, exceptions);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsMacOS():
                    var exceptions2 = SanitizeExceptions(config);
                    await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions2);
                    break;

                case ESysProxyType.ForcedClear when Utils.IsWindows():
                    ProxySettingWindows.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsLinux():
                    await ProxySettingLinux.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsMacOS():
                    await ProxySettingOSX.UnsetProxy();
                    break;

                case ESysProxyType.Pac when Utils.IsWindows():
                    return await SetWindowsProxyPac(config, port);
            }

            if (type != ESysProxyType.Pac && Utils.IsWindows())
            {
                _pacManager.Value.Stop();
                _activeWindowsPacUrl = null;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return true;
    }

    private static string SanitizeExceptions(Config config)
    {
        var exceptions = config.SystemProxyItem.SystemProxyExceptions;
        if (exceptions.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var items = exceptions
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Replace(" ", string.Empty))
            .Where(item => item.Length > 0);

        return string.Join(',', items);
    }

    private static (string strProxy, string strExceptions) GetWindowsProxyString(Config config, int port)
    {
        var strExceptions = config.SystemProxyItem.SystemProxyExceptions.Replace(" ", "");
        if (config.SystemProxyItem.NotProxyLocalAddress)
        {
            strExceptions = $"<local>;{strExceptions}";
        }

        var strProxy = string.Empty;
        if (config.SystemProxyItem.SystemProxyAdvancedProtocol.IsNullOrEmpty())
        {
            strProxy = $"{Global.Loopback}:{port}";
        }
        else
        {
            strProxy = config.SystemProxyItem.SystemProxyAdvancedProtocol
                .Replace("{ip}", Global.Loopback)
                .Replace("{http_port}", port.ToString())
                .Replace("{socks_port}", port.ToString());
        }

        return (strProxy, strExceptions);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<bool> SetWindowsProxyPac(Config config, int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        await _pacManager.Value.StartAsync(port, portPac);
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        if (HasExternalWindowsProxy(config))
        {
            config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged;
            _pacManager.Value.Stop();
            return false;
        }
        var applied = ProxySettingWindows.SetProxy(strProxy, "", 4);
        _activeWindowsPacUrl = strProxy;
        return applied;
    }
}
