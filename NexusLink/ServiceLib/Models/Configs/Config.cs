namespace ServiceLib.Models.Configs;

[Serializable]
public class Config
{
    #region property

    public string IndexId { get; set; }
    public string SubIndexId { get; set; }

    #endregion property

    #region other entities

    public CoreBasicItem CoreBasicItem { get; set; }
    public TunModeItem TunModeItem { get; set; }
    public KcpItem KcpItem { get; set; }
    public GrpcItem GrpcItem { get; set; }
    public RoutingBasicItem RoutingBasicItem { get; set; }
    public GUIItem GuiItem { get; set; }
    public MsgUIItem MsgUIItem { get; set; }
    public UIItem UiItem { get; set; }
    public ConstItem ConstItem { get; set; }
    public SpeedTestItem SpeedTestItem { get; set; }
    public Mux4RayItem Mux4RayItem { get; set; }
    public Mux4SboxItem Mux4SboxItem { get; set; }
    public HysteriaItem HysteriaItem { get; set; }
    public ClashUIItem ClashUIItem { get; set; }
    public SystemProxyItem SystemProxyItem { get; set; }

    // Agent 智能分流：本地代理 127.0.0.1:10808 常驻，系统代理保持关闭。
    // 国内流量由 NO_PROXY / 分流规则直接出去，国外流量走 NexusLink 代理。
    public bool LocalProxyAlwaysOn { get; set; }

    public WebDavItem WebDavItem { get; set; }
    public CheckUpdateItem CheckUpdateItem { get; set; }
    public Fragment4RayItem? Fragment4RayItem { get; set; }
    public List<InItem> Inbound { get; set; }
    public List<KeyEventItem> GlobalHotkeys { get; set; }
    public List<CoreTypeItem> CoreTypeItem { get; set; }
    public SimpleDNSItem SimpleDNSItem { get; set; }
    public HappyEyeballs4RayItem HappyEyeballs4RayItem { get; set; }

    #endregion other entities
}
