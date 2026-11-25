using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ServiceLib.Services;

namespace NexusLink.Views;

public partial class KeeperOverview
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SolidColorBrush _green = new(Color.FromRgb(0xEE, 0xEE, 0xEE));
    private readonly SolidColorBrush _orange = new(Color.FromRgb(0xC8, 0xC8, 0xC8));
    private readonly SolidColorBrush _red = new(Color.FromRgb(0xEF, 0xEF, 0xEF));
    private readonly SolidColorBrush _gray = new(Color.FromRgb(0xA3, 0xA3, 0xA3));
    private bool _uiLock;
    private List<string> _nodeCache = [];

    public KeeperOverview()
    {
        InitializeComponent();

        BtnConnect.Click += async (_, _) =>
        {
            if (_uiLock)
            {
                return;
            }
            BtnConnect.IsEnabled = false;
            await KeeperService.Instance.SetConnectedAsync(!IsSysProxyOn());
            BtnConnect.IsEnabled = true;
            RefreshUi();
        };
        BtnRefresh.Click += async (_, _) =>
        {
            BtnRefresh.IsEnabled = false;
            await KeeperService.Instance.RefreshAsync();
            BtnRefresh.IsEnabled = true;
            RefreshUi();
        };
        BtnSettings.Click += async (_, _) =>
        {
            var vm = new ServiceLib.ViewModels.OptionSettingViewModel();
            var ret = await ServiceLib.Manager.AppManager.Instance.WindowDialog.ShowDialogAsync(vm);
            if (ret == true)
            {
                NoticeManager.Instance.SendMessage("畅连：设置已保存");
                RefreshUi();
            }
        };
        TbAutoSwitch.Click += async (_, _) =>
        {
            if (_uiLock)
            {
                return;
            }
            await KeeperService.Instance.SetAutoSwitchAsync(TbAutoSwitch.IsChecked == true);
            RefreshUi();
        };
        TbDaily.Click += (_, _) =>
        {
            if (_uiLock)
            {
                return;
            }
            KeeperService.Instance.SetDailyRefresh(TbDaily.IsChecked == true);
            RefreshUi();
        };

        _timer.Tick += (_, _) => RefreshUi();
        _timer.Start();
        RefreshUi();
    }

    private static bool IsSysProxyOn()
    {
        return AppManager.Instance.Config?.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange;
    }

    private void SetDot(SolidColorBrush c, double glow)
    {
        Dot.Fill = c;
        DotGlow.Color = c.Color;
        DotGlow.Opacity = glow;
    }

    private void SetPill(Border pill, TextBlock text, SolidColorBrush c, string content)
    {
        text.Text = content;
        text.Foreground = c;
        pill.BorderBrush = c;
        pill.Background = new SolidColorBrush(Color.FromArgb(0x14, c.Color.R, c.Color.G, c.Color.B));
    }

    private static string F(double b)
    {
        return b >= 1048576 ? $"{b / 1048576:F1} MB/s"
            : b >= 1024 ? $"{b / 1024:F0} KB/s"
            : $"{b:F0} B/s";
    }

    private void RefreshUi()
    {
        _uiLock = true;
        var st = KeeperService.Instance.State;
        var busy = KeeperService.Instance.IsRunning;
        BtnRefresh.IsEnabled = !busy;
        var sysOn = IsSysProxyOn();

        // ---- hero ----
        if (sysOn && st.Online)
        {
            SetDot(_green, 0.55);
            HeroTitle.Text = "已连接";
            // 路线行只显示应用自己检测到的出口（国旗 国家 城市），不显示节点池写的名称或内核内部标签
            var place = $"{st.ExitCountry} {st.ExitCity}".Trim();
            HeroRoute.Text = place.Length > 0
            ? (KeeperService.FlagOf(st.ExitCode) + " " + place).Trim()
            : (st.ExitNode.Length > 0 ? st.ExitNode : "已连接");
            var meta = new List<string>();
            if (st.ExitIp.IsNotEmpty())
            {
                meta.Add($"出口 IP {st.ExitIp}");
            }
            if (st.ExitIsp.IsNotEmpty())
            {
                meta.Add(st.ExitIsp);
            }
            if (st.PollAt.IsNotEmpty())
            {
                meta.Add($"{st.PollAt} 更新");
            }
            HeroMeta.Text = string.Join(" · ", meta);
            SetPill(DelayPill, DelayText,
                st.LatencyMs > 0 ? st.LatencyMs < 300 ? _green : st.LatencyMs < 800 ? _orange : _red : _gray,
                st.LatencyMs > 0 ? $"延迟 {st.LatencyMs} ms" : "延迟 --");
            SetPill(SpeedPill, SpeedText, _green, $"网速 ↓ {F(KeeperService.Instance.DownSpeed)} · ↑ {F(KeeperService.Instance.UpSpeed)}");
        }
        else if (sysOn && !st.Online)
        {
            SetDot(_orange, 0.3);
            HeroTitle.Text = "连接异常";
            HeroRoute.Text = "线路不通，点「刷新与测速」";
            HeroMeta.Text = "";
            SetPill(DelayPill, DelayText, _gray, "延迟 --");
            SetPill(SpeedPill, SpeedText, _gray, "网速 --");
        }
        else
        {
            SetDot(_gray, 0);
            HeroTitle.Text = "未连接";
            HeroRoute.Text = "点击连接";
            HeroMeta.Text = "";
            SetPill(DelayPill, DelayText, _gray, "延迟 --");
            SetPill(SpeedPill, SpeedText, _gray, "网速 --");
        }

        // ---- 连接按钮 ----
        BtnConnect.Content = sysOn ? "断开连接" : "立即连接";

        // ---- 开关 ----
        TbAutoSwitch.IsChecked = st.AutoSwitch;
        TbDaily.IsChecked = st.DailyRefresh;
        DailyDesc.Text = st.DailyRefresh ? $"已开启 · 每 {st.RefreshIntervalHours} 小时刷新" : "已关闭";

        // ---- 线路 ----
        BtnConnect.Style = (Style)FindResource(sysOn ? "BtnAccent" : "BtnPrimary");
        CountBadge.Text = st.BackupCount > 0 ? $"{st.Nodes.Count} 条线路 · {st.BackupCount} 个延迟超标未入池" : $"{st.Nodes.Count} 条线路";
        LastRefreshText.Text = st.LastRefresh.Length > 0 ? $"上次更新 {st.LastRefresh}" : "";
        var nodes = st.Nodes.Select(NodeRow).ToList();
        if (!nodes.SequenceEqual(_nodeCache))
        {
            _nodeCache = nodes;
            LvNodes.Items.Clear();
            foreach (var n in nodes)
            {
                LvNodes.Items.Add(new TextBlock
                {
                    Text = n,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 5, 0, 5),
                });
            }
        }

        _uiLock = false;
    }

    // 线路列表行：检测出来的名称（实测出口 + 协议）+ 使用中/当前线路 · 机房/家宽 · 实测速度 · 安全 · 稳定等级 · 来源（标注随每轮换血刷新）
    private static string NodeRow(KeeperNode t)
    {
        var meta = new List<string>();
        if (t.IndexId.Length > 0 && t.IndexId == KeeperService.Instance.State.ActiveIndexId)
        {
            meta.Add(KeeperService.Instance.IsConnected ? "使用中" : "当前线路");
        }
        if (t.NetType.Length > 0)
        {
        meta.Add(t.NetType);
        }
        if (t.Exit.Length > 0 && t.Speed.Length > 0)
        {
        meta.Add(t.Speed);
        }
        if (t.Security.Length > 0)
        {
            meta.Add($"安全 {t.Security}");
        }
        meta.Add("稳定 " + (t.Stability.Length > 0 ? t.Stability : "—"));
        if (t.Source.Length > 0)
        {
            meta.Add($"来源 {t.Source}");
        }
        return $"{t.Name}    {string.Join(" · ", meta)}";
    }
}

