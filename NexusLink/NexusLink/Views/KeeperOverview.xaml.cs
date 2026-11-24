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
            var place = $"{st.ExitCountry} {st.ExitCity}".Trim();
            HeroRoute.Text = string.IsNullOrEmpty(st.ExitNode)
                ? (place.Length > 0 ? place : "已连接（线路信息加载中…）")
                : $"{place} · {st.ExitNode}";
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
            HeroRoute.Text = "系统代理已开，但线路不通 —— 可刷新并测试候选节点";
            HeroMeta.Text = "";
            SetPill(DelayPill, DelayText, _gray, "延迟 --");
            SetPill(SpeedPill, SpeedText, _gray, "网速 --");
        }
        else
        {
            SetDot(_gray, 0);
            HeroTitle.Text = "未连接";
            HeroRoute.Text = "浏览器当前直连（不走任何节点）—— 点下面按钮一键连上";
            HeroMeta.Text = "";
            SetPill(DelayPill, DelayText, _gray, "延迟 --");
            SetPill(SpeedPill, SpeedText, _gray, "网速 --");
        }

        // ---- 连接按钮 ----
        BtnConnect.Content = sysOn ? "断开连接（恢复直连）" : "立即连接";

        // ---- 开关 ----
        TbAutoSwitch.IsChecked = st.AutoSwitch;
        TbDaily.IsChecked = st.DailyRefresh;
        DailyDesc.Text = st.DailyRefresh ? $"已开启 · 每 {st.RefreshIntervalHours} 小时刷新" : "已关闭";

        // ---- 线路 ----
        BtnConnect.Style = (Style)FindResource(sysOn ? "BtnAccent" : "BtnPrimary");
        CountBadge.Text = $"{st.Nodes.Count} 主池 / {st.BackupCount} 备用";
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

    // 线路列表行：节点名 + 来源 · 安全等级（标注随每轮审查刷新）
    private static string NodeRow(KeeperNode t)
    {
        var meta = new List<string>();
        if (t.Source.Length > 0)
        {
            meta.Add($"来源 {t.Source}");
        }
        if (t.Security.Length > 0)
        {
            meta.Add($"安全 {t.Security}");
        }
        return meta.Count > 0 ? $"{t.Remarks}    {string.Join(" · ", meta)}" : t.Remarks;
    }
}

