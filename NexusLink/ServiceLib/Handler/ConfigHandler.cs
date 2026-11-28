using System.Data;

namespace ServiceLib.Handler;

public static class ConfigHandler
{
    private static readonly string _configRes = Global.ConfigFileName;
    private static readonly string _tag = "ConfigHandler";

    #region ConfigHandler

    /// <summary>
    /// Load the application configuration file
    /// If the file exists, deserialize it from JSON
    /// If not found, create a new Config object with default settings
    /// Initialize default values for missing configuration sections
    /// </summary>
    /// <returns>Config object containing application settings or null if there's an error</returns>
    public static Config? LoadConfig()
    {
        Config? config = null;
        var result = EmbedUtils.LoadResource(Utils.GetConfigPath(_configRes));
        if (result.IsNotEmpty())
        {
            config = JsonUtils.Deserialize<Config>(result);
        }
        else
        {
            if (File.Exists(Utils.GetConfigPath(_configRes)))
            {
                Logging.SaveLog("LoadConfig Exception");
                return null;
            }
        }

        config ??= new Config();

        config.CoreBasicItem ??= new()
        {
            LogEnabled = false,
            Loglevel = "warning",
        };

        if (config.Inbound == null)
        {
            config.Inbound = [];
            InItem inItem = new()
            {
                Protocol = nameof(EInboundProtocol.socks),
                LocalPort = 10808,
                UdpEnabled = true,
                SniffingEnabled = true,
                RouteOnly = false,
            };

            config.Inbound.Add(inItem);
        }
        else
        {
            if (config.Inbound.Count > 0)
            {
                config.Inbound.First().Protocol = nameof(EInboundProtocol.socks);
            }
        }

        config.RoutingBasicItem ??= new();
        if (config.RoutingBasicItem.DomainStrategy.IsNullOrEmpty())
        {
            config.RoutingBasicItem.DomainStrategy = Global.DomainStrategies.First();
        }

        config.KcpItem ??= new KcpItem
        {
            Mtu = 1350,
            Tti = 50,
            UplinkCapacity = 12,
            DownlinkCapacity = 100,
            CwndMultiplier = 1,
            MaxSendingWindow = 2 * 1024 * 1024,
        };
        config.KcpItem.CwndMultiplier = config.KcpItem.CwndMultiplier <= 0 ? 1 : config.KcpItem.CwndMultiplier;
        config.KcpItem.MaxSendingWindow = config.KcpItem.MaxSendingWindow <= 0 ? (2 * 1024 * 1024) : config.KcpItem.MaxSendingWindow;
        config.GrpcItem ??= new GrpcItem
        {
            IdleTimeout = 60,
            HealthCheckTimeout = 20,
            PermitWithoutStream = false,
            InitialWindowsSize = 0,
        };
        config.TunModeItem ??= new TunModeItem
        {
            EnableTun = true,
            Mtu = 9000,
            IcmpRouting = Global.TunIcmpRoutingPolicies.First(),
            EnableLegacyProtect = true,
        };
        config.GuiItem ??= new();
        if (!Global.RootCertProviders.Contains(config.GuiItem.RootCertProvider))
        {
            config.GuiItem.RootCertProvider = Global.RootCertProviders.First();
        }
        config.MsgUIItem ??= new();

        config.UiItem ??= new();
        config.UiItem.MainColumnItem ??= [];
        config.UiItem.WindowSizeItem ??= [];

        if (config.UiItem.CurrentLanguage.IsNullOrEmpty())
        {
            config.UiItem.CurrentLanguage = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.CurrentCultureIgnoreCase)
                ? Global.Languages.First()
                : Global.Languages[2];
        }

        config.ConstItem ??= new ConstItem();

        config.SimpleDNSItem ??= InitBuiltinSimpleDNS();
        config.SimpleDNSItem.BlockAAAAQuery ??= false;
        config.SimpleDNSItem.FakeIPRange ??= Global.FakeIPRanges.FirstOrDefault();
        config.SimpleDNSItem.GlobalFakeIp ??= true;
        config.SimpleDNSItem.BootstrapDNS ??= Global.DomainPureIPDNSAddress.FirstOrDefault();
        config.SimpleDNSItem.ServeStale ??= false;
        config.SimpleDNSItem.ParallelQuery ??= false;
        config.SimpleDNSItem.EnableHappyEyeballs ??= false;

        config.SpeedTestItem ??= new();
        if (config.SpeedTestItem.SpeedTestTimeout < 10)
        {
            config.SpeedTestItem.SpeedTestTimeout = 10;
        }
        if (config.SpeedTestItem.SpeedTestUrl.IsNullOrEmpty())
        {
            config.SpeedTestItem.SpeedTestUrl = Global.SpeedTestUrls.First();
        }
        if (config.SpeedTestItem.SpeedPingTestUrl.IsNullOrEmpty())
        {
            config.SpeedTestItem.SpeedPingTestUrl = Global.SpeedPingTestUrls.First();
        }
        config.SpeedTestItem.MixedConcurrencyCount = Math.Max(config.SpeedTestItem.MixedConcurrencyCount, Global.SpeedTestConcurrencyCountMin);
        if (config.SpeedTestItem.UdpTestTarget.IsNullOrEmpty())
        {
            config.SpeedTestItem.UdpTestTarget = Global.UdpTestTargets.First();
        }

        config.Mux4RayItem ??= new()
        {
            Concurrency = 8,
            XudpConcurrency = 16,
            XudpProxyUDP443 = "reject"
        };

        config.Mux4SboxItem ??= new()
        {
            Protocol = Global.SingboxMuxs.First(),
            MaxConnections = 8
        };

        config.HysteriaItem ??= new()
        {
            UpMbps = 100,
            DownMbps = 100
        };
        config.ClashUIItem ??= new();
        config.ClashUIItem.ConnectionsColumnItem ??= [];
        config.SystemProxyItem ??= new();
        config.WebDavItem ??= new();
        config.CheckUpdateItem ??= new();
        config.Fragment4RayItem ??= new()
        {
            Packets = "tlshello",
        };
        config.Fragment4RayItem.MaxSplit ??= "0";

        config.HappyEyeballs4RayItem ??= new()
        {
            TryDelayMs = 250,
            PrioritizeIPv6 = false,
            Interleave = 1,
            MaxConcurrentTry = 4,
        };
        if ((config.Fragment4RayItem.Lengths ?? []).Count == 0)
        {
            config.Fragment4RayItem.Lengths = [config.Fragment4RayItem.Length ?? "50-100"];
        }
        if ((config.Fragment4RayItem.Delays ?? []).Count == 0)
        {
            config.Fragment4RayItem.Delays = [config.Fragment4RayItem.Interval ?? "10-20"];
        }
        config.GlobalHotkeys ??= [];

        if (config.SystemProxyItem.SystemProxyExceptions.IsNullOrEmpty())
        {
            config.SystemProxyItem.SystemProxyExceptions = Utils.IsWindows() ? Global.SystemProxyExceptionsWindows : Global.SystemProxyExceptionsLinux;
        }

        // 每次启动一律未连接：「已连接」只是上次运行中的状态，不随下次启动恢复，点「连接」才连
        config.SystemProxyItem.SysProxyType = StartupSysProxyType(config.SystemProxyItem.SysProxyType);

        return config;
    }

    /// <summary>
    /// 启动与退出时的连接状态：恒为未连接。启动不自动连；退出后再打开同样未连接
    /// </summary>
    public static ESysProxyType StartupSysProxyType(ESysProxyType saved)
    {
        return ESysProxyType.ForcedClear;
    }

    /// <summary>
    /// Save the configuration to a file
    /// First writes to a temporary file, then replaces the original file
    /// </summary>
    /// <param name="config">Configuration object to be saved</param>
    /// <returns>0 if successful, -1 if failed</returns>
    private static readonly SemaphoreSlim _configSaveLock = new(1, 1);

    public static async Task<int> SaveConfig(Config config)
    {
        // 定时保存 / 退出保存 / 状态轮询保存可能同时进来：串行化 + 每次独立的临时文件，避免互相覆盖出截断的 JSON
        await _configSaveLock.WaitAsync();
        var tempPath = string.Empty;
        try
        {
            //save temp file
            var resPath = Utils.GetConfigPath(_configRes);
            tempPath = $"{resPath}.{Utils.GetGuid(false)}.tmp";

            var content = JsonUtils.Serialize(config, true, true);
            if (content.IsNullOrEmpty())
            {
                return -1;
            }
            await File.WriteAllTextAsync(tempPath, content);
            if (JsonUtils.Deserialize<Config>(await File.ReadAllTextAsync(tempPath)) == null)
            {
                Logging.SaveLog($"{_tag} save config: refusing to replace config with unparsable content");
                return -1;
            }

            //rename
            File.Move(tempPath, resPath, true);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return -1;
        }
        finally
        {
            if (tempPath.IsNotEmpty())
            {
                try { File.Delete(tempPath); } catch { }
            }
            _configSaveLock.Release();
        }

        return 0;
    }

    #endregion ConfigHandler

    #region Server


    /// <summary>
    /// Add or edit a VMess server
    /// Validates and processes VMess-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">VMess profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddVMessServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.VMess;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
        {
            VmessSecurity = profileItem.GetProtocolExtra().VmessSecurity?.TrimEx()
        });
        profileItem.Network = profileItem.Network.TrimEx();
        profileItem.StreamSecurity = profileItem.StreamSecurity.TrimEx();

        if (!Global.VmessSecurities.Contains(profileItem.GetProtocolExtra().VmessSecurity))
        {
            return -1;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Remove multiple servers from the configuration
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="indexes">List of server profiles to remove</param>
    /// <returns>0 if successful</returns>
    public static async Task<int> RemoveServers(Config config, List<ProfileItem> indexes)
    {
        var subid = "TempRemoveSubId";
        foreach (var item in indexes)
        {
            item.Subid = subid;
        }

        await SQLiteHelper.Instance.UpdateAllAsync(indexes);
        await RemoveServersViaSubid(config, subid, false);

        return 0;
    }


    /// <summary>
    /// Set the default server by its index ID
    /// Updates the configuration to use the specified server as default
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="indexId">Index ID of the server to set as default</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> SetDefaultServerIndex(Config config, string? indexId)
    {
        if (indexId.IsNullOrEmpty())
        {
            return -1;
        }

        config.IndexId = indexId;

        await SaveConfig(config);

        return 0;
    }


    /// <summary>
    /// Get the current default server profile
    /// If the current default is invalid, selects a new default
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <returns>The default profile item or null if none exists</returns>
    public static async Task<ProfileItem?> GetDefaultServer(Config config)
    {
        var item = await AppManager.Instance.GetProfileItem(config.IndexId);
        if (item is null)
        {
            var item2 = await SQLiteHelper.Instance.TableAsync<ProfileItem>().FirstOrDefaultAsync();
            await SetDefaultServerIndex(config, item2?.IndexId);
            return item2;
        }

        return item;
    }


    /// <summary>
    /// Add a custom server configuration from a file
    /// Copies the configuration file to the app's config directory
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Profile item with the file path in Address</param>
    /// <param name="blDelete">Whether to delete the source file after copying</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddCustomServer(Config config, ProfileItem profileItem, bool blDelete)
    {
        var fileName = profileItem.Address;
        if (!File.Exists(fileName))
        {
            return -1;
        }
        var ext = Path.GetExtension(fileName);
        var newFileName = $"{Utils.GetGuid()}{ext}";
        //newFileName = Path.Combine(Utile.GetTempPath(), newFileName);

        try
        {
            File.Copy(fileName, Utils.GetConfigPath(newFileName));
            if (blDelete)
            {
                File.Delete(fileName);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return -1;
        }

        profileItem.Address = newFileName;
        profileItem.ConfigType = EConfigType.Custom;
        if (profileItem.Remarks.IsNullOrEmpty())
        {
            profileItem.Remarks = $"import custom@{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}";
        }

        await AddServerCommon(config, profileItem, true);

        return 0;
    }

    public static async Task<int> AddCustomOutboundServer(Config config, ProfileItem profileItem, bool blDelete, bool toFile = true)
    {
        var fileName = profileItem.Address;
        if (!File.Exists(fileName))
        {
            return -1;
        }
        var ext = Path.GetExtension(fileName);
        var newFileName = $"{Utils.GetGuid()}{ext}";
        //newFileName = Path.Combine(Utile.GetTempPath(), newFileName);

        try
        {
            File.Copy(fileName, Utils.GetConfigPath(newFileName));
            if (blDelete)
            {
                File.Delete(fileName);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return -1;
        }

        profileItem.Address = newFileName;
        profileItem.ConfigType = EConfigType.Outbound;
        if (profileItem.Remarks.IsNullOrEmpty())
        {
            profileItem.Remarks = $"import custom outbound@{DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")}";
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }


    /// <summary>
    /// Add or edit a Shadowsocks server
    /// Validates and processes Shadowsocks-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Shadowsocks profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddShadowsocksServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.Shadowsocks;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
        {
            SsMethod = profileItem.GetProtocolExtra().SsMethod?.TrimEx()
        });

        if (!AppManager.Instance.GetShadowsocksSecurities(profileItem).Contains(profileItem.GetProtocolExtra().SsMethod))
        {
            return -1;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit a SOCKS server
    /// Processes SOCKS-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">SOCKS profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddSocksServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.SOCKS;

        profileItem.Address = profileItem.Address.TrimEx();

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit an HTTP server
    /// Processes HTTP-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">HTTP profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddHttpServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.HTTP;

        profileItem.Address = profileItem.Address.TrimEx();

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit a Trojan server
    /// Validates and processes Trojan-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Trojan profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddTrojanServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.Trojan;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit a Hysteria2 server
    /// Validates and processes Hysteria2-specific settings
    /// Sets the core type to sing_box as required by Hysteria2
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Hysteria2 profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddHysteria2Server(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.Hysteria2;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Fingerprint = string.Empty;
        profileItem.Alpn = string.Empty;
        //profileItem.Alpn = "h3";
        profileItem.Network = string.Empty;

        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }
        var protocolExtra = profileItem.GetProtocolExtra();
        profileItem.SetProtocolExtra(protocolExtra with
        {
            SalamanderPass = protocolExtra.SalamanderPass?.TrimEx(),
            HopInterval = protocolExtra.HopInterval?.TrimEx(),
        });

        if (!protocolExtra.Hy2RealmUrl.IsNullOrEmpty())
        {
            var realmResult = HyRealm.TryParse(protocolExtra.Hy2RealmUrl, out var realm);
            if (!realmResult || realm is null)
            {
                return -1;
            }
            if (realm.StunList.Count == 0)
            {
                realm = realm with
                {
                    StunList = Global.DefaultRealmStunList,
                };
            }
            profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
            {
                Hy2RealmUrl = realm.ToUri(),
            });
        }
        var isGecko = !protocolExtra.GeckoMinPacketSize.IsNullOrEmpty() || !protocolExtra.GeckoMaxPacketSize.IsNullOrEmpty();
        if (isGecko)
        {
            var minPacketSize = protocolExtra.GeckoMinPacketSize.ToInt();
            var maxPacketSize = protocolExtra.GeckoMaxPacketSize.ToInt();
            if (minPacketSize <= 0
                || minPacketSize > maxPacketSize
                || maxPacketSize > 2048)
            {
                minPacketSize = 512;
                maxPacketSize = 1200;
            }
            profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
            {
                GeckoMinPacketSize = minPacketSize.ToString(),
                GeckoMaxPacketSize = maxPacketSize.ToString(),
            });
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit a TUIC server
    /// Validates and processes TUIC-specific settings
    /// Sets the core type to sing_box as required by TUIC
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">TUIC profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddTuicServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.TUIC;
        profileItem.CoreType = ECoreType.sing_box;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Username = profileItem.Username.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Network = string.Empty;
        profileItem.Fingerprint = string.Empty;

        var congestionControl = profileItem.GetProtocolExtra().CongestionControl;
        if (!Global.TuicCongestionControls.Contains(congestionControl))
        {
            congestionControl = Global.TuicCongestionControls.FirstOrDefault()!;
        }
        profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with { CongestionControl = congestionControl });

        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Alpn.IsNullOrEmpty())
        {
            profileItem.Alpn = "h3";
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit a WireGuard server
    /// Validates and processes WireGuard-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">WireGuard profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddWireguardServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.WireGuard;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        var wgReserved = profileItem.GetProtocolExtra().WgReserved?.TrimEx();
        if (!wgReserved.IsNullOrEmpty()
            && !wgReserved.Contains(','))
        {
            // Base64 format, convert to standard format
            try
            {
                var bytes = Convert.FromBase64String(wgReserved);
                var reserved = new byte[3];
                Array.Copy(bytes, reserved, Math.Min(bytes.Length, 3));

                wgReserved = string.Join(", ", reserved);
            }
            catch
            {
                // If conversion fails, keep the original value
            }
        }
        profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
        {
            WgPublicKey = profileItem.GetProtocolExtra().WgPublicKey?.TrimEx(),
            WgPresharedKey = profileItem.GetProtocolExtra().WgPresharedKey?.TrimEx(),
            WgInterfaceAddress = profileItem.GetProtocolExtra().WgInterfaceAddress?.TrimEx(),
            WgReserved = wgReserved,
            WgMtu = profileItem.GetProtocolExtra().WgMtu is null or <= 0 ? Global.TunMtus.First() : profileItem.GetProtocolExtra().WgMtu,
            WgDns = profileItem.GetProtocolExtra().WgDns?.TrimEx(),
        });

        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }

    /// <summary>
    /// Add or edit an Anytls server
    /// Validates and processes Anytls-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Anytls profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddAnytlsServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.Anytls;
        profileItem.CoreType = ECoreType.sing_box;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Network = string.Empty;
        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }
        await AddServerCommon(config, profileItem, toFile);
        return 0;
    }

    /// <summary>
    /// Add or edit a Naive server
    /// Validates and processes Naive-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Naive profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddNaiveServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.Naive;
        profileItem.CoreType = ECoreType.sing_box;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Username = profileItem.Username.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Fingerprint = string.Empty;
        profileItem.Alpn = string.Empty;
        profileItem.Network = string.Empty;
        profileItem.AllowInsecure = string.Empty;
        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }
        await AddServerCommon(config, profileItem, toFile);
        return 0;
    }

    public static async Task<int> AddMasqueServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.MASQUE;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Username = profileItem.Username.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Fingerprint = string.Empty;
        profileItem.Alpn = string.Empty;
        profileItem.Network = string.Empty;
        profileItem.AllowInsecure = string.Empty;
        if (profileItem.StreamSecurity.IsNullOrEmpty())
        {
            profileItem.StreamSecurity = Global.StreamSecurity;
        }
        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }
        await AddServerCommon(config, profileItem, toFile);
        return 0;
    }


    /// <summary>
    /// Add or edit a VLESS server
    /// Validates and processes VLESS-specific settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">VLESS profile to add</param>
    /// <param name="toFile">Whether to save to file</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddVlessServer(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigType = EConfigType.VLESS;

        profileItem.Address = profileItem.Address.TrimEx();
        profileItem.Password = profileItem.Password.TrimEx();
        profileItem.Network = profileItem.Network.TrimEx();
        profileItem.StreamSecurity = profileItem.StreamSecurity.TrimEx();

        var vlessEncryption = profileItem.GetProtocolExtra().VlessEncryption?.TrimEx();
        var flow = profileItem.GetProtocolExtra().Flow?.TrimEx() ?? string.Empty;
        profileItem.SetProtocolExtra(profileItem.GetProtocolExtra() with
        {
            VlessEncryption = vlessEncryption.IsNullOrEmpty() ? Global.None : vlessEncryption,
            Flow = Global.Flows.Contains(flow) ? flow : Global.Flows.First(),
        });

        if (profileItem.Password.IsNullOrEmpty())
        {
            return -1;
        }

        await AddServerCommon(config, profileItem, toFile);

        return 0;
    }


    /// <summary>
    /// Common server addition logic used by all server types
    /// Sets common properties and handles sorting and persistence
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="profileItem">Profile item to add</param>
    /// <param name="toFile">Whether to save to database</param>
    /// <returns>0 if successful</returns>
    public static async Task<int> AddServerCommon(Config config, ProfileItem profileItem, bool toFile = true)
    {
        profileItem.ConfigVersion = 4;

        if (profileItem.StreamSecurity.IsNotEmpty())
        {
            if (profileItem.StreamSecurity is not Global.StreamSecurity
                 and not Global.StreamSecurityReality)
            {
                profileItem.StreamSecurity = string.Empty;
            }
            else
            {
                if (profileItem.Fingerprint.IsNullOrEmpty() && profileItem.StreamSecurity == Global.StreamSecurityReality)
                {
                    profileItem.Fingerprint = config.CoreBasicItem.DefFingerprint;
                }
            }
        }

        if (profileItem.Network.IsNotEmpty() && !Global.Networks.Contains(profileItem.Network))
        {
            profileItem.Network = Global.DefaultNetwork;
        }

        var maxSort = -1;
        if (profileItem.IndexId.IsNullOrEmpty())
        {
            profileItem.IndexId = Utils.GetGuid(false);
            maxSort = ProfileExManager.Instance.GetMaxSort();
        }
        if (!toFile && maxSort < 0)
        {
            maxSort = ProfileExManager.Instance.GetMaxSort();
        }
        if (maxSort > 0)
        {
            ProfileExManager.Instance.SetSort(profileItem.IndexId, maxSort + 1);
        }

        if (toFile)
        {
            //profileItem.SetProtocolExtra();
            profileItem.SetProtocolExtra(profileItem.GetProtocolExtra());
            await SQLiteHelper.Instance.ReplaceAsync(profileItem);
        }
        return 0;
    }

    /// <summary>
    /// Compare two profile items to determine if they represent the same server
    /// Used for deduplication and server matching
    /// </summary>
    /// <param name="o">First profile item</param>
    /// <param name="n">Second profile item</param>
    /// <param name="remarks">Whether to compare remarks</param>
    /// <returns>True if the profiles match, false otherwise</returns>
    private static bool CompareProfileItem(ProfileItem? o, ProfileItem? n, bool remarks)
    {
        if (o == null || n == null)
        {
            return false;
        }

        var oProtocolExtra = o.GetProtocolExtra();
        var nProtocolExtra = n.GetProtocolExtra();
        var oTransport = o.GetTransportExtra();
        var nTransport = n.GetTransportExtra();

        return o.ConfigType == n.ConfigType
               && AreEqual(o.Address, n.Address)
               && o.Port == n.Port
               && AreEqual(o.Password, n.Password)
               && AreEqual(o.Username, n.Username)
               && AreEqual(oProtocolExtra.VlessEncryption, nProtocolExtra.VlessEncryption)
               && AreEqual(oProtocolExtra.SsMethod, nProtocolExtra.SsMethod)
               && AreEqual(oProtocolExtra.VmessSecurity, nProtocolExtra.VmessSecurity)
               && AreEqual(o.Network, n.Network)
               && AreEqual(oTransport.RawHeaderType, nTransport.RawHeaderType)
               && AreEqual(oTransport.Host, nTransport.Host)
               && AreEqual(oTransport.Path, nTransport.Path)
               && AreEqual(oTransport.XhttpMode, nTransport.XhttpMode)
               && AreEqual(oTransport.XhttpExtra, nTransport.XhttpExtra)
               && AreEqual(oTransport.GrpcAuthority, nTransport.GrpcAuthority)
               && AreEqual(oTransport.GrpcServiceName, nTransport.GrpcServiceName)
               && AreEqual(oTransport.GrpcMode, nTransport.GrpcMode)
               && AreEqual(oTransport.KcpHeaderType, nTransport.KcpHeaderType)
               && AreEqual(oTransport.KcpSeed, nTransport.KcpSeed)
               && (o.ConfigType == EConfigType.Trojan || o.StreamSecurity == n.StreamSecurity)
               && AreEqual(oProtocolExtra.Flow, nProtocolExtra.Flow)
               && AreEqual(oProtocolExtra.SalamanderPass, nProtocolExtra.SalamanderPass)
               && AreEqual(o.Sni, n.Sni)
               && AreEqual(o.Alpn, n.Alpn)
               && AreEqual(o.Fingerprint, n.Fingerprint)
               && AreEqual(o.PublicKey, n.PublicKey)
               && AreEqual(o.ShortId, n.ShortId)
               && AreEqual(o.Finalmask, n.Finalmask)
               && (!remarks || o.Remarks == n.Remarks);

        static bool AreEqual(string? a, string? b)
        {
            return string.Equals(a, b) || (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b));
        }
    }

    /// <summary>
    /// Searches the specified collection for a profile item that matches the target profile item based on a series of
    /// criteria.
    /// </summary>
    /// <remarks>The method attempts to find a match by comparing the target's remarks, address, port, and
    /// password in various combinations. The search is performed in order of specificity, starting with the most
    /// detailed comparison. If no match is found at any stage, the method returns null.</remarks>
    /// <param name="source">An enumerable collection of profile items to search. This parameter can be null.</param>
    /// <param name="target">The profile item to match against items in the source collection. This parameter can be null.</param>
    /// <returns>A profile item from the source collection that matches the target item according to defined criteria; otherwise,
    /// null if no match is found or if either parameter is null.</returns>
    private static ProfileItem? FindMatchedProfileItem(IEnumerable<ProfileItem>? source, ProfileItem? target)
    {
        if (source == null || target == null)
        {
            return null;
        }

        var matchedItem = source.FirstOrDefault(t => CompareProfileItem(t, target, true));
        if (matchedItem != null)
        {
            return matchedItem;
        }

        if (target.Remarks.IsNotEmpty())
        {
            matchedItem = source.FirstOrDefault(t => t.Remarks == target.Remarks);
            if (matchedItem != null)
            {
                return matchedItem;
            }
        }

        if (target.Address.IsNotEmpty() && target.Port > 0 && target.Password.IsNotEmpty())
        {
            matchedItem = source.FirstOrDefault(t =>
                IsSameText(t.Address, target.Address) &&
                t.Port == target.Port &&
                IsSameText(t.Password, target.Password));
            if (matchedItem != null)
            {
                return matchedItem;
            }
        }

        if (target.Address.IsNotEmpty() && target.Port > 0)
        {
            matchedItem = source.FirstOrDefault(t =>
                IsSameText(t.Address, target.Address) &&
                t.Port == target.Port);
            if (matchedItem != null)
            {
                return matchedItem;
            }
        }

        if (target.Address.IsNotEmpty())
        {
            matchedItem = source.FirstOrDefault(t => IsSameText(t.Address, target.Address));
            if (matchedItem != null)
            {
                return matchedItem;
            }
        }

        return null;

        static bool IsSameText(string? left, string? right)
        {
            if (left.IsNullOrEmpty() || right.IsNullOrEmpty())
            {
                return false;
            }

            return string.Equals(left.TrimEx(), right.TrimEx(), StringComparison.OrdinalIgnoreCase);
        }
    }




    private static readonly Dictionary<string, string> PolicyGroupRegionFilters = new()
    {
        { "JP", "日本|\\b[Jj][Pp]\\b|🇯🇵|[Jj]apan" },
        { "US", "美国|\\b[Uu][Ss]\\b|🇺🇸|[Uu]nited [Ss]tates|\\b[Uu][Ss][Aa]\\b" },
        { "HK", "香港|\\b[Hh][Kk]\\b|🇭🇰|[Hh]ong ?[Kk]ong" },
        { "TW", "台湾|台灣|\\b[Tt][Ww]\\b|🇹🇼|[Tt]aiwan" },
        { "KR", "韩国|\\b[Kk][Rr]\\b|🇰🇷|[Kk]orea" },
        { "SG", "新加坡|\\b[Ss][Gg]\\b|🇸🇬|[Ss]ingapore" },
        { "DE", "德国|\\b[Dd][Ee]\\b|🇩🇪|[Gg]ermany" },
        { "FR", "法国|\\b[Ff][Rr]\\b|🇫🇷|[Ff]rance" },
        { "GB", "英国|\\b[Gg][Bb]\\b|🇬🇧|[Uu]nited [Kk]ingdom|[Bb]ritain" },
        { "CA", "加拿大|🇨🇦|[Cc]anada" },
        { "AU", "澳大利亚|\\b[Aa][Uu]\\b|🇦🇺|[Aa]ustralia" },
        { "RU", "俄罗斯|\\b[Rr][Uu]\\b|🇷🇺|[Rr]ussia" },
        { "BR", "巴西|\\b[Bb][Rr]\\b|🇧🇷|[Bb]razil" },
        { "IN", "印度|🇮🇳|[Ii]ndia" },
        { "VN", "越南|\\b[Vv][Nn]\\b|🇻🇳|[Vv]ietnam" },
        { "ID", "印度尼西亚|\\b[Ii][Dd]\\b|🇮🇩|[Ii]ndonesia" },
        { "MX", "墨西哥|\\b[Mm][Xx]\\b|🇲🇽|[Mm]exico" }
    };


    /// <summary>
    /// Get a SOCKS server profile for pre-SOCKS functionality
    /// Used when TUN mode is enabled or when a custom config has a pre-SOCKS port
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="node">Server node that might need pre-SOCKS</param>
    /// <param name="coreType">Core type being used</param>
    /// <returns>A SOCKS profile item or null if not needed</returns>
    public static ProfileItem? GetPreSocksItem(Config config, ProfileItem node, ECoreType coreType)
    {
        ProfileItem? itemSocks = null;
        var enableLegacyProtect = config.TunModeItem.EnableLegacyProtect;
        if (node.ConfigType != EConfigType.Custom
            && coreType != ECoreType.sing_box
            && config.TunModeItem.EnableTun
            && enableLegacyProtect)
        {
            itemSocks = new ProfileItem()
            {
                CoreType = ECoreType.sing_box,
                ConfigType = EConfigType.SOCKS,
                Address = Global.Loopback,
                Port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks)
            };
        }
        else if (node.ConfigType == EConfigType.Custom
            && node.PreSocksPort is > 0 and <= 65535)
        {
            var customPreCoreType = AppManager.Instance.GetCoreType(null, EConfigType.Custom);
            var preCoreType = (enableLegacyProtect && config.TunModeItem.EnableTun) ? ECoreType.sing_box : customPreCoreType;
            itemSocks = new ProfileItem()
            {
                CoreType = preCoreType,
                ConfigType = EConfigType.SOCKS,
                Address = Global.Loopback,
                Port = node.PreSocksPort.Value,
            };
        }
        return itemSocks;
    }


    #endregion Server

    #region Batch add servers

    /// <summary>
    /// Add multiple servers from string data (common protocols)
    /// Parses the string data into server profiles
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="strData">String data containing server information</param>
    /// <param name="subid">Subscription ID to associate with the servers</param>
    /// <param name="isSub">Whether this is from a subscription</param>
    /// <returns>Number of successfully imported servers or -1 if failed</returns>
    private static async Task<int> AddBatchServersCommon(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }

        var subFilter = string.Empty;
        if (isSub && subid.IsNotEmpty())
        {
            subFilter = (await AppManager.Instance.GetSubItem(subid))?.Filter ?? "";
        }

        var countServers = 0;
        List<ProfileItem> lstAdd = [];
        var arrData = strData.Split(Environment.NewLine.ToCharArray()).Where(t => !t.IsNullOrEmpty());
        if (isSub)
        {
            arrData = arrData.Distinct();
        }
        foreach (var str in arrData)
        {
            //maybe sub
            if (!isSub && (str.StartsWith(Global.HttpsProtocol) || str.StartsWith(Global.HttpProtocol)))
            {
                if (await AddSubItem(config, str) == 0)
                {
                    countServers++;
                }
                continue;
            }
            var profileItem = FmtHandler.ResolveConfig(str, out var msg);
            if (profileItem is null)
            {
                continue;
            }

            //exist sub items //filter
            if (isSub && subid.IsNotEmpty() && subFilter.IsNotEmpty())
            {
                if (!Utils.IsRegexMatch(profileItem.Remarks, subFilter))
                {
                    continue;
                }
            }
            profileItem.Subid = subid;
            profileItem.IsSub = isSub;

            var addStatus = profileItem.ConfigType switch
            {
                EConfigType.VMess => await AddVMessServer(config, profileItem, false),
                EConfigType.Shadowsocks => await AddShadowsocksServer(config, profileItem, false),
                EConfigType.SOCKS => await AddSocksServer(config, profileItem, false),
                EConfigType.Trojan => await AddTrojanServer(config, profileItem, false),
                EConfigType.VLESS => await AddVlessServer(config, profileItem, false),
                EConfigType.Hysteria2 => await AddHysteria2Server(config, profileItem, false),
                EConfigType.TUIC => await AddTuicServer(config, profileItem, false),
                EConfigType.WireGuard => await AddWireguardServer(config, profileItem, false),
                EConfigType.Anytls => await AddAnytlsServer(config, profileItem, false),
                EConfigType.Naive => await AddNaiveServer(config, profileItem, false),
                EConfigType.MASQUE => await AddMasqueServer(config, profileItem, false),
                _ => -1,
            };

            if (addStatus == 0)
            {
                countServers++;
                lstAdd.Add(profileItem);
            }
        }

        if (lstAdd.Count > 0)
        {
            await SQLiteHelper.Instance.InsertAllAsync(lstAdd);
        }

        await SaveConfig(config);
        return countServers;
    }

    /// <summary>
    /// Add servers from custom configuration formats (sing-box, v2ray, etc.)
    /// Handles various configuration formats and imports them as custom configs
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="strData">String data containing server information</param>
    /// <param name="subid">Subscription ID to associate with the servers</param>
    /// <param name="isSub">Whether this is from a subscription</param>
    /// <returns>Number of successfully imported servers or -1 if failed</returns>
    private static async Task<int> AddBatchServers4Custom(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }

        var subItem = await AppManager.Instance.GetSubItem(subid);

        if (subItem?.CustomCoreType is null)
        {
            return await AddBatchServersDefaultCustom(config, strData, subid, isSub, subItem);
        }

        return await AddBatchServersSpecificCustom(config, strData, subid, isSub, subItem);
    }

    private static async Task<int> AddBatchServersDefaultCustom(
        Config config,
        string strData,
        string subid,
        bool isSub,
        SubItem? subItem)
    {
        var subRemarks = subItem?.Remarks;
        // Prioritize using complete custom parsing, followed by custom outbound parsing.
        var lstProfiles = V2rayFmt.ResolveToCustom(strData, subRemarks);
        if (lstProfiles.Count == 0)
        {
            lstProfiles = SingboxFmt.ResolveToCustom(strData, subRemarks);
        }
        if (lstProfiles.Count == 0)
        {
            lstProfiles = V2rayFmt.ResolveToCustomOutbound(strData, subRemarks);
        }
        if (lstProfiles.Count == 0)
        {
            lstProfiles = SingboxFmt.ResolveToCustomOutbound(strData, subRemarks);
        }
        if (lstProfiles.Count > 0)
        {
            var count = await AddBatchCustomServers(config, lstProfiles, subid, isSub, subItem);
            if (count > 0)
            {
                return count;
            }
        }

        if (HtmlPageFmt.IsHtmlPage(strData))
        {
            return -1;
        }

        var profileItem = ClashFmt.ResolveFull(strData, subRemarks)
            ?? Hysteria2Fmt.ResolveFull2(strData, subRemarks);

        if (profileItem == null)
        {
            return -1;
        }

        profileItem.Subid = subid;
        profileItem.IsSub = isSub;
        profileItem.PreSocksPort = subItem?.PreSocksPort;

        return await AddCustomServer(config, profileItem, true) == 0 ? 1 : -1;
    }

    private static async Task<int> AddBatchServersSpecificCustom(
        Config config,
        string strData,
        string subid,
        bool isSub,
        SubItem subItem)
    {
        var subRemarks = subItem.Remarks;
        var customCoreType = subItem.CustomCoreType!.Value;

        var lstProfiles = customCoreType switch
        {
            ECoreType.Xray => V2rayFmt.ResolveToCustom(strData, subRemarks),
            ECoreType.sing_box => SingboxFmt.ResolveToCustom(strData, subRemarks),
            _ => null,
        };

        if (lstProfiles?.Count > 0)
        {
            var count = await AddBatchCustomServers(config, lstProfiles, subid, isSub, subItem);
            if (count > 0)
            {
                return count;
            }
        }

        return await SaveCustomRawFileServer(config, strData, subid, isSub, subItem, customCoreType);
    }

    private static async Task<int> AddBatchCustomServers(
        Config config,
        List<ProfileItem> lstProfiles,
        string subid,
        bool isSub,
        SubItem? subItem)
    {
        var count = 0;
        foreach (var it in lstProfiles)
        {
            it.Subid = subid;
            it.IsSub = isSub;

            if (it.ConfigType == EConfigType.Custom)
            {
                it.PreSocksPort = subItem?.PreSocksPort;
                if (await AddCustomServer(config, it, true) == 0)
                {
                    count++;
                }
            }
            else
            {
                if (await AddCustomOutboundServer(config, it, true) == 0)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static async Task<int> SaveCustomRawFileServer(
        Config config,
        string strData,
        string subid,
        bool isSub,
        SubItem subItem,
        ECoreType customCoreType)
    {
        var ext = DetectFileExtension(strData);
        var fileName = Utils.GetTempPath($"{Utils.GetGuid(false)}{ext}");
        await File.WriteAllTextAsync(fileName, strData);

        var profileItem = new ProfileItem
        {
            CoreType = customCoreType,
            ConfigType = EConfigType.Custom,
            Address = fileName,
            Remarks = subItem.Remarks ?? customCoreType.ToString(),
            Subid = subid,
            IsSub = isSub,
            PreSocksPort = subItem.PreSocksPort,
        };

        return await AddCustomServer(config, profileItem, true) == 0 ? 1 : -1;

        static string DetectFileExtension(string data)
        {
            var trimmed = data.AsSpan().TrimStart();
            if (trimmed.IsEmpty)
            {
                return string.Empty;
            }

            if (trimmed[0] is '{' or '[')
            {
                return ".json";
            }

            if (trimmed.StartsWith("---"))
            {
                return ".yaml";
            }

            foreach (var line in trimmed.EnumerateLines())
            {
                var lineTrimmed = line.TrimStart();
                if (lineTrimmed.IsEmpty || lineTrimmed.StartsWith("#"))
                {
                    continue;
                }

                var colonIndex = lineTrimmed.IndexOf(':');
                if (colonIndex > 0)
                {
                    var keySpan = lineTrimmed[..colonIndex];
                    if (!keySpan.Contains(' ') && !keySpan.Contains('\t'))
                    {
                        if (colonIndex == lineTrimmed.Length - 1 || lineTrimmed[colonIndex + 1] is ' ' or '\t' or '\r' or '\n')
                        {
                            return ".yaml";
                        }
                    }
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Add Shadowsocks servers from SIP008 format
    /// SIP008 is a JSON-based format for Shadowsocks servers
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="strData">String data in SIP008 format</param>
    /// <param name="subid">Subscription ID to associate with the servers</param>
    /// <param name="isSub">Whether this is from a subscription</param>
    /// <returns>Number of successfully imported servers or -1 if failed</returns>
    private static async Task<int> AddBatchServers4SsSIP008(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }

        var lstSsServer = ShadowsocksFmt.ResolveSip008(strData);
        if (lstSsServer?.Count > 0)
        {
            var counter = 0;
            foreach (var ssItem in lstSsServer)
            {
                ssItem.Subid = subid;
                ssItem.IsSub = isSub;
                if (await AddShadowsocksServer(config, ssItem) == 0)
                {
                    counter++;
                }
            }
            await SaveConfig(config);
            return counter;
        }

        return -1;
    }

    private static async Task<int> AddBatchServers4Wireguard(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }
        if (!(strData.Contains("[Interface]", StringComparison.OrdinalIgnoreCase)
              && strData.Contains("[Peer]", StringComparison.OrdinalIgnoreCase)))
        {
            return -1;
        }
        var lstServer = WireguardFmt.ResolveConfig(strData);
        if (lstServer?.Count > 0)
        {
            var counter = 0;
            foreach (var item in lstServer)
            {
                item.Subid = subid;
                item.IsSub = isSub;
                if (await AddWireguardServer(config, item) == 0)
                {
                    counter++;
                }
            }
            await SaveConfig(config);
            return counter;
        }
        return -1;
    }

    private static async Task<int> AddBatchServers4InnerUri(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }

        var lstServer = InnerFmt.Resolve(strData, subid);
        if (lstServer?.Count > 0)
        {
            var counter = 0;
            List<ProfileItem> lstAdd = [];
            foreach (var profileItem in lstServer)
            {
                profileItem.Subid = subid;
                profileItem.IsSub = isSub;

                var addStatus = profileItem.ConfigType switch
                {
                    EConfigType.VMess => await AddVMessServer(config, profileItem, false),
                    EConfigType.Shadowsocks => await AddShadowsocksServer(config, profileItem, false),
                    EConfigType.HTTP => await AddHttpServer(config, profileItem, false),
                    EConfigType.SOCKS => await AddSocksServer(config, profileItem, false),
                    EConfigType.Trojan => await AddTrojanServer(config, profileItem, false),
                    EConfigType.VLESS => await AddVlessServer(config, profileItem, false),
                    EConfigType.Hysteria2 => await AddHysteria2Server(config, profileItem, false),
                    EConfigType.TUIC => await AddTuicServer(config, profileItem, false),
                    EConfigType.WireGuard => await AddWireguardServer(config, profileItem, false),
                    EConfigType.Anytls => await AddAnytlsServer(config, profileItem, false),
                    EConfigType.Naive => await AddNaiveServer(config, profileItem, false),
                    EConfigType.PolicyGroup or EConfigType.ProxyChain => await AddServerCommon(config, profileItem, false),
                    EConfigType.Outbound => await AddCustomOutboundServer(config, profileItem, true, false),
                    _ => -1,
                };
                if (addStatus == 0)
                {
                    counter++;
                    lstAdd.Add(profileItem);
                }
            }
            if (lstAdd.Count > 0)
            {
                await SQLiteHelper.Instance.InsertAllAsync(lstAdd);
            }
            await SaveConfig(config);
            return counter;
        }

        return -1;
    }

    /// <summary>
    /// Main entry point for adding batch servers from various formats
    /// Tries different parsing methods to import as many servers as possible
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="strData">String data containing server information</param>
    /// <param name="subid">Subscription ID to associate with the servers</param>
    /// <param name="isSub">Whether this is from a subscription</param>
    /// <returns>Number of successfully imported servers or -1 if failed</returns>
    public static async Task<int> AddBatchServers(Config config, string strData, string subid, bool isSub)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }
        List<ProfileItem>? lstOriSub = null;
        ProfileItem? activeProfile = null;
        if (isSub && subid.IsNotEmpty())
        {
            lstOriSub = await AppManager.Instance.ProfileItems(subid);
            activeProfile = lstOriSub?.FirstOrDefault(t => t.IndexId == config.IndexId);
            await RemoveServersViaSubid(config, subid, true);
        }

        var counter = 0;
        if (Utils.IsBase64String(strData))
        {
            counter = await AddBatchServersCommon(config, Utils.Base64Decode(strData), subid, isSub);
        }
        if (counter < 1)
        {
            counter = await AddBatchServersCommon(config, strData, subid, isSub);
        }
        if (counter < 1)
        {
            counter = await AddBatchServersCommon(config, Utils.Base64Decode(strData), subid, isSub);
        }

        if (counter < 1)
        {
            counter = await AddBatchServers4SsSIP008(config, strData, subid, isSub);
        }

        //maybe wireguard config
        if (counter < 1)
        {
            counter = await AddBatchServers4Wireguard(config, strData, subid, isSub);
        }

        //May be standard uri mixed with internal uri
        var innerUriCount = 0;
        if (Utils.IsBase64String(strData))
        {
            innerUriCount = await AddBatchServers4InnerUri(config, Utils.Base64Decode(strData), subid, isSub);
        }
        if (innerUriCount < 1)
        {
            innerUriCount = await AddBatchServers4InnerUri(config, strData, subid, isSub);
        }
        if (innerUriCount < 1)
        {
            innerUriCount = await AddBatchServers4InnerUri(config, Utils.Base64Decode(strData), subid, isSub);
        }
        if (innerUriCount > 0)
        {
            if (counter > 0)
            {
                counter += innerUriCount;
            }
            else
            {
                counter = innerUriCount;
            }
        }

        //maybe other sub
        if (counter < 1)
        {
            counter = await AddBatchServers4Custom(config, strData, subid, isSub);
        }

        // 订阅刷新"先删后解析"的兜底：源返回错误页 / 格式变了 / 网络抖动导致一条都没解析出来时，
        // 把删掉的旧节点原样放回去，不让一次失败把订阅清空（自定义配置文件已删的无法恢复，会记日志）
        if (counter < 1 && isSub && subid.IsNotEmpty() && lstOriSub is { Count: > 0 })
        {
            var restorable = lstOriSub.Where(t => t.ConfigType != EConfigType.Custom && t.ConfigType != EConfigType.Outbound).ToList();
            foreach (var item in restorable)
            {
                try
                {
                    await SQLiteHelper.Instance.InsertAsync(item);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(_tag, ex);
                }
            }
            Logging.SaveLog($"subscription update for '{subid}' parsed 0 nodes; restored {restorable.Count}/{lstOriSub.Count} previous nodes");
        }

        //Select active node
        if (activeProfile != null)
        {
            var lstSub = await AppManager.Instance.ProfileItems(subid);
            var existItem = FindMatchedProfileItem(lstSub, activeProfile);
            if (existItem != null)
            {
                await ConfigHandler.SetDefaultServerIndex(config, existItem.IndexId);
            }
        }


        return counter;
    }

    #endregion Batch add servers

    #region Sub & Group

    /// <summary>
    /// Add a subscription item from URL
    /// Creates a new subscription with default settings
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="url">Subscription URL</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddSubItem(Config config, string url)
    {
        //already exists
        var count = await SQLiteHelper.Instance.TableAsync<SubItem>().CountAsync(e => e.Url == url);
        if (count > 0)
        {
            return 0;
        }
        SubItem subItem = new()
        {
            Id = string.Empty,
            Url = url
        };

        var uri = Utils.TryUri(url);
        if (uri == null)
        {
            return -1;
        }
        //Do not allow http protocol
        if (url.StartsWith(Global.HttpProtocol) && !Utils.IsPrivateNetwork(uri.IdnHost))
        {
            //TODO Temporary reminder to be removed later
            NoticeManager.Instance.Enqueue(ResUI.InsecureUrlProtocol);
            //return -1;
        }

        var queryVars = Utils.ParseQueryString(uri.Query);
        subItem.Remarks = queryVars["remarks"] ?? "import_sub";

        return await AddSubItem(config, subItem);
    }

    /// <summary>
    /// Add or update a subscription item
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="subItem">Subscription item to add or update</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddSubItem(Config config, SubItem subItem)
    {
        var item = await AppManager.Instance.GetSubItem(subItem.Id);
        if (item is null)
        {
            item = subItem;
        }
        else
        {
            item.Remarks = subItem.Remarks;
            item.Url = subItem.Url;
            item.MoreUrl = subItem.MoreUrl;
            item.Enabled = subItem.Enabled;
            item.AutoUpdateInterval = subItem.AutoUpdateInterval;
            item.UserAgent = subItem.UserAgent;
            item.RequestHeaders = subItem.RequestHeaders;
            item.Sort = subItem.Sort;
            item.Filter = subItem.Filter;
            item.UpdateTime = subItem.UpdateTime;
            item.ConvertTarget = subItem.ConvertTarget;
            item.PrevProfile = subItem.PrevProfile;
            item.NextProfile = subItem.NextProfile;
            item.PreSocksPort = subItem.PreSocksPort;
            item.Memo = subItem.Memo;
            item.CustomCoreType = subItem.CustomCoreType;
        }

        if (item.Id.IsNullOrEmpty())
        {
            item.Id = Utils.GetGuid(false);

            if (item.Sort <= 0)
            {
                var maxSort = 0;
                if (await SQLiteHelper.Instance.TableAsync<SubItem>().CountAsync() > 0)
                {
                    var lstSubs = await AppManager.Instance.SubItems();
                    maxSort = lstSubs.LastOrDefault()?.Sort ?? 0;
                }
                item.Sort = maxSort + 1;
            }
        }
        if (await SQLiteHelper.Instance.ReplaceAsync(item) > 0)
        {
            return 0;
        }
        else
        {
            return -1;
        }
    }

    /// <summary>
    /// Remove servers associated with a subscription ID
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="subid">Subscription ID</param>
    /// <param name="isSub">Whether to only remove servers marked as subscription items</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> RemoveServersViaSubid(Config config, string subid, bool isSub)
    {
        if (subid.IsNullOrEmpty())
        {
            return -1;
        }
        var customProfile = await SQLiteHelper.Instance.TableAsync<ProfileItem>().Where(t => t.Subid == subid && (t.ConfigType == EConfigType.Custom || t.ConfigType == EConfigType.Outbound)).ToListAsync();
        if (isSub)
        {
            await SQLiteHelper.Instance.ExecuteAsync($"delete from ProfileItem where isSub = 1 and subid = '{subid}'");
        }
        else
        {
            await SQLiteHelper.Instance.ExecuteAsync($"delete from ProfileItem where subid = '{subid}'");
        }
        foreach (var item in customProfile)
        {
            File.Delete(Utils.GetConfigPath(item.Address));
        }

        return 0;
    }



    #endregion Sub & Group

    #region Routing


    /// <summary>
    /// Add multiple routing rules to a routing item
    /// </summary>
    /// <param name="routingItem">Routing item to add rules to</param>
    /// <param name="strData">JSON string containing rules data</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> AddBatchRoutingRules(RoutingItem routingItem, string strData)
    {
        if (strData.IsNullOrEmpty())
        {
            return -1;
        }

        var lstRules = JsonUtils.Deserialize<List<RulesItem>>(strData);
        if (lstRules == null)
        {
            return -1;
        }

        foreach (var item in lstRules)
        {
            item.Id = Utils.GetGuid(false);
        }
        routingItem.RuleNum = lstRules.Count;
        routingItem.RuleSet = JsonUtils.Serialize(lstRules, false);

        if (routingItem.Id.IsNullOrEmpty())
        {
            routingItem.Id = Utils.GetGuid(false);
        }

        if (await SQLiteHelper.Instance.ReplaceAsync(routingItem) > 0)
        {
            return 0;
        }
        else
        {
            return -1;
        }
    }


    /// <summary>
    /// Set the default routing configuration
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="routingItem">Routing item to set as default</param>
    /// <returns>0 if successful</returns>
    public static async Task<int> SetDefaultRouting(Config config, RoutingItem routingItem)
    {
        var items = await AppManager.Instance.RoutingItems();
        if (items.Any(t => t.Id == routingItem.Id && t.IsActive == true))
        {
            return -1;
        }

        foreach (var item in items)
        {
            if (item.Id == routingItem.Id)
            {
                item.IsActive = true;
            }
            else
            {
                item.IsActive = false;
            }
        }

        await SQLiteHelper.Instance.UpdateAllAsync(items);

        return 0;
    }

    /// <summary>
    /// Get the current default routing configuration
    /// If no default is set, selects the first available routing item
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <returns>The default routing item</returns>
    public static async Task<RoutingItem> GetDefaultRouting(Config config)
    {
        var item = await SQLiteHelper.Instance.TableAsync<RoutingItem>().FirstOrDefaultAsync(it => it.IsActive == true);
        if (item is null)
        {
            var item2 = await SQLiteHelper.Instance.TableAsync<RoutingItem>().FirstOrDefaultAsync();
            await SetDefaultRouting(config, item2);
            return item2;
        }

        return item;
    }



    /// <summary>
    /// Initialize built-in routing rules
    /// Creates default routing configurations (whitelist, blacklist, global)
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="blImportAdvancedRules">Whether to import advanced rules</param>
    /// <returns>0 if successful</returns>
    public static async Task<int> InitBuiltinRouting(Config config, bool blImportAdvancedRules = false)
    {
        var ver = "V4-";
        var items = await AppManager.Instance.RoutingItems();

        //TODO Temporary code to be removed later
        var lockItem = items?.FirstOrDefault(t => t.Locked == true);
        if (lockItem != null)
        {
            await ConfigHandler.RemoveRoutingItem(lockItem);
            items = await AppManager.Instance.RoutingItems();
        }

        if (!blImportAdvancedRules && items.Count() > 0) // items.Count(u => u.Remarks.StartsWith(ver)) > 0)
        {
            //migrate
            //TODO Temporary code to be removed later
            if (config.RoutingBasicItem.RoutingIndexId.IsNotEmpty())
            {
                var item = items.FirstOrDefault(t => t.Id == config.RoutingBasicItem.RoutingIndexId);
                if (item != null)
                {
                    await SetDefaultRouting(config, item);
                }
                config.RoutingBasicItem.RoutingIndexId = string.Empty;
            }

            return 0;
        }

        var maxSort = items.Count;
        //Bypass the mainland
        var item2 = new RoutingItem()
        {
            Remarks = $"{ver}绕过大陆(Whitelist)",
            Url = string.Empty,
            Sort = maxSort + 1,
        };
        await AddBatchRoutingRules(item2, EmbedUtils.GetEmbedText(Global.CustomRoutingFileName + "white"));

        //Blacklist
        var item3 = new RoutingItem()
        {
            Remarks = $"{ver}黑名单(Blacklist)",
            Url = string.Empty,
            Sort = maxSort + 2,
        };
        await AddBatchRoutingRules(item3, EmbedUtils.GetEmbedText(Global.CustomRoutingFileName + "black"));

        //Global
        var item1 = new RoutingItem()
        {
            Remarks = $"{ver}全局(Global)",
            Url = string.Empty,
            Sort = maxSort + 3,
        };
        await AddBatchRoutingRules(item1, EmbedUtils.GetEmbedText(Global.CustomRoutingFileName + "global"));

        if (!blImportAdvancedRules)
        {
            await SetDefaultRouting(config, item2);
        }
        return 0;
    }

    /// <summary>
    /// Remove a routing item from the database
    /// </summary>
    /// <param name="routingItem">Routing item to remove</param>
    public static async Task RemoveRoutingItem(RoutingItem routingItem)
    {
        await SQLiteHelper.Instance.DeleteAsync(routingItem);
    }

    #endregion Routing

    #region DNS

    /// <summary>
    /// Initialize built-in DNS configurations
    /// Creates default DNS items for V2Ray and sing-box
    /// Also checks existing DNS items and disables those with empty NormalDNS
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <returns>0 if successful</returns>
    public static async Task<int> InitBuiltinDNS(Config config)
    {
        var items = await AppManager.Instance.DNSItems();

        // Check existing DNS items and disable those with empty NormalDNS
        var needsUpdate = false;
        foreach (var existingItem in items)
        {
            if (existingItem.NormalDNS.IsNullOrEmpty() && existingItem.Enabled)
            {
                existingItem.Enabled = false;
                needsUpdate = true;
            }
        }

        // Update items if any changes were made
        if (needsUpdate)
        {
            await SQLiteHelper.Instance.UpdateAllAsync(items);
        }

        if (items.Count <= 0)
        {
            var item = new DNSItem()
            {
                Remarks = "V2ray",
                CoreType = ECoreType.Xray,
                Enabled = false,
            };
            await SaveDNSItems(config, item);

            var item2 = new DNSItem()
            {
                Remarks = "sing-box",
                CoreType = ECoreType.sing_box,
                Enabled = false,
            };
            await SaveDNSItems(config, item2);
        }

        return 0;
    }

    /// <summary>
    /// Save a DNS item to the database
    /// </summary>
    /// <param name="config">Current configuration</param>
    /// <param name="item">DNS item to save</param>
    /// <returns>0 if successful, -1 if failed</returns>
    public static async Task<int> SaveDNSItems(Config config, DNSItem item)
    {
        if (item == null)
        {
            return -1;
        }

        if (item.Id.IsNullOrEmpty())
        {
            item.Id = Utils.GetGuid(false);
        }

        if (await SQLiteHelper.Instance.ReplaceAsync(item) > 0)
        {
            return 0;
        }
        else
        {
            return -1;
        }
    }


    #endregion DNS

    #region Simple DNS

    public static SimpleDNSItem InitBuiltinSimpleDNS()
    {
        return new SimpleDNSItem()
        {
            UseSystemHosts = false,
            AddCommonHosts = true,
            FakeIP = false,
            GlobalFakeIp = true,
            BlockBindingQuery = true,
            DirectDNS = Global.DomainDirectDNSAddress.FirstOrDefault(),
            RemoteDNS = Global.DomainRemoteDNSAddress.FirstOrDefault(),
            BootstrapDNS = Global.DomainPureIPDNSAddress.FirstOrDefault(),
        };
    }


    #endregion Simple DNS

    #region Custom Config



    #endregion Custom Config

    #region Regional Presets


    #endregion Regional Presets

    #region UIItem

    public static WindowSizeItem? GetWindowSizeItem(Config config, string typeName)
    {
        var sizeItem = config?.UiItem?.WindowSizeItem?.FirstOrDefault(t => t.TypeName == typeName);
        if (sizeItem == null || sizeItem.Width <= 0 || sizeItem.Height <= 0)
        {
            return null;
        }

        return sizeItem;
    }

    public static int SaveWindowSizeItem(Config config, string typeName, double width, double height)
    {
        var sizeItem = config?.UiItem?.WindowSizeItem?.FirstOrDefault(t => t.TypeName == typeName);
        if (sizeItem == null)
        {
            sizeItem = new WindowSizeItem { TypeName = typeName };
            config.UiItem.WindowSizeItem.Add(sizeItem);
        }

        sizeItem.Width = (int)width;
        sizeItem.Height = (int)height;

        return 0;
    }


    #endregion UIItem
}
