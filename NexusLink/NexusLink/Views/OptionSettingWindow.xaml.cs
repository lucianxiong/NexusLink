using System.Windows.Media;

namespace NexusLink.Views;

public partial class OptionSettingWindow
{
    private static Config _config;

    public OptionSettingWindow()
    {
        InitializeComponent();

        // 畅连极简版：只保留常用设置页
        tabCore.Visibility = Visibility.Collapsed;
        tabSysProxy.Visibility = Visibility.Collapsed;
        tabTun.Visibility = Visibility.Collapsed;
        tabCoreType.Visibility = Visibility.Collapsed;
        tabSettings.SelectedItem = tabParam;

        _config = AppManager.Instance.Config;

        clbdestOverride.SelectionChanged += ClbdestOverride_SelectionChanged;

        clbdestOverride.ItemsSource = Global.destOverrideProtocols;
        _config.Inbound.First().DestOverride?.ForEach(it =>
        {
            clbdestOverride.SelectedItems.Add(it);
        });

        cmbsystemProxyAdvancedProtocol.ItemsSource = Global.IEProxyProtocols;
        cmbloglevel.ItemsSource = Global.LogLevels;
        cmbdefFingerprint.ItemsSource = Global.Fingerprints;
        cmbdefUserAgent.ItemsSource = Global.UserAgent;
        cmbmux4SboxProtocol.ItemsSource = Global.SingboxMuxs;
        cmbMtu.ItemsSource = Global.TunMtus;
        cmbStack.ItemsSource = Global.TunStacks;
        cmbIcmpRoutingPolicy.ItemsSource = Global.TunIcmpRoutingPolicies;
        cmbIpv4Address.ItemsSource = Global.TunIPv4Address;
        cmbIpv6Address.ItemsSource = Global.TunIPv6Address;

        cmbCoreType1.ItemsSource = Global.CoreTypes;
        cmbCoreType2.ItemsSource = Global.CoreTypes;
        cmbCoreType3.ItemsSource = Global.CoreTypes;
        cmbCoreType4.ItemsSource = Global.CoreTypes;
        cmbCoreType5.ItemsSource = Global.CoreTypes;
        cmbCoreType6.ItemsSource = Global.CoreTypes;
        cmbCoreType7.ItemsSource = Global.CoreTypes;
        cmbCoreType9.ItemsSource = Global.CoreTypes;
        cmbCoreType14.ItemsSource = Global.CoreTypes;

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.LocalPort, v => v.txtlocalPort.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SecondLocalPortEnabled, v => v.togSecondLocalPortEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.UdpEnabled, v => v.togudpEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SniffingEnabled, v => v.togsniffingEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.RouteOnly, v => v.togrouteOnly.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AllowLANConn, v => v.togAllowLANConn.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.togNewPort4LAN.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.txtuser.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.NewPort4LAN, v => v.txtpass.IsEnabled).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.User, v => v.txtuser.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Pass, v => v.txtpass.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.LogEnabled, v => v.toglogEnabled.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Loglevel, v => v.cmbloglevel.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DefFingerprint, v => v.cmbdefFingerprint.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DefUserAgent, v => v.cmbdefUserAgent.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SendThrough, v => v.txtsendThrough.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.BindInterface, v => v.txtbindInterface.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Mux4SboxProtocol, v => v.cmbmux4SboxProtocol.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableCacheFile4Sbox, v => v.togenableCacheFile4Sbox.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HyUpMbps, v => v.txtUpMbps.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HyDownMbps, v => v.txtDownMbps.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableFragment, v => v.togenableFragment.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableFinalFragment, v => v.togenableFinalFragment.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.AutoRun, v => v.togAutoRun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togTun.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoHideStartup, v => v.togAutoHideStartup.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoUpdateInterval, v => v.txtautoUpdateInterval.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.NotProxyLocalAddress, v => v.tognotProxyLocalAddress.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SystemProxyAdvancedProtocol, v => v.cmbsystemProxyAdvancedProtocol.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SystemProxyExceptions, v => v.txtsystemProxyExceptions.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.TunAutoRoute, v => v.togAutoRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunStrictRoute, v => v.togStrictRoute.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunStack, v => v.cmbStack.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunMtu, v => v.cmbMtu.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunEnableIPv6Address, v => v.togEnableIPv6Address.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIcmpRouting, v => v.cmbIcmpRoutingPolicy.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunEnableLegacyProtect, v => v.togEnableLegacyProtect.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunRouteExcludeAddress, v => v.txtRouteExcludeAddress.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIPv4Address, v => v.cmbIpv4Address.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.TunIPv6Address, v => v.cmbIpv6Address.Text).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.CoreType1, v => v.cmbCoreType1.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType2, v => v.cmbCoreType2.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType3, v => v.cmbCoreType3.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType4, v => v.cmbCoreType4.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType5, v => v.cmbCoreType5.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType6, v => v.cmbCoreType6.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType7, v => v.cmbCoreType7.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType9, v => v.cmbCoreType9.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.CoreType14, v => v.cmbCoreType14.Text).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.SaveCmd, v => v.btnSave).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    private void ClbdestOverride_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.DestOverride = clbdestOverride.SelectedItems.Cast<string>().ToList();
        }
    }
}
