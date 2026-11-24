using System.Drawing;
using System.Windows.Media.Imaging;

namespace NexusLink.Manager;

public sealed class WindowsManager
{
    private static readonly Lazy<WindowsManager> instance = new(() => new());
    public static WindowsManager Instance => instance.Value;
    private static readonly string _tag = "WindowsHandler";


    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        var index = (int)config.SystemProxyItem.SysProxyType + 1;
        return BitmapFrame.Create(new Uri($"pack://application:,,,/Resources/NotifyIcon{index}.ico", UriKind.RelativeOrAbsolute));
    }


    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
