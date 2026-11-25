# 畅连 NexusLink

Windows 代理客户端，内置「节点管家」：自动从公益节点池深度抓取节点、逐个实测，安全和速度达标的直接入池；线路断掉或明显变差时自动换线。打开应用，点「立即连接」即可上网。

> 本项目是 [v2rayN](https://github.com/2dust/v2rayN) 7.25.6（作者 [2dust](https://github.com/2dust)）的修改版：界面与节点管理逻辑重做，以 [GPL-3.0](LICENSE) 发布。

## 使用前必读

- 节点**全部来自公益节点池**，由陌生志愿者无偿提供，来源无法核实，随时可能失效。
- 你的流量会经过这些服务器。节点方看不到 HTTPS 内容，但能看到你连接了哪些域名、你的真实 IP。
- **不要在公益节点上登录网银、支付、主力邮箱**；重要账号请开启两步验证。
- 本版本不提供添加自有订阅或节点的功能。

## 下载与运行

仅支持 Windows 10 / 11 x64，无需安装 .NET。

1. 到 [Releases](../../releases/latest) 下载 `NexusLink-win-x64-<版本>.zip` 和同名 `.sha256`，核对 SHA-256 后解压到任意目录。
2. 运行 `NexusLink.exe`，点「立即连接」。

压缩包自带 sing-box 与 mihomo 内核（`bin\` 目录），请整个目录一起保留。

## 功能

- **一键连接 / 断开**：打开时默认未连接；默认「绕过大陆」路由，国内网站直连。断开或退出后内核进程全部结束。
- **TUN 模式**（默认开启，可在设置里关闭）：所有程序的网络都经畅连出网，终端、命令行 agent 等不遵循系统代理的程序也能用。TUN 需要管理员权限：启动时会弹出授权请求，拒绝则自动关闭 TUN。
- **节点管家**：深度抓取 → 安全检查 → 实测 → 入池 → 自动选路，全程自动；安全且实测延迟低于上限的节点直接入池，延迟超标的不入池；选路顺序为 安全 > 稳定 > 带宽 > 延迟。
- **手动指定**：节点页点「用这个节点」锁定线路，随时可「恢复自动」。
- **设置**（首页右上角齿轮）：TUN 模式、开机自动启动、启动后隐藏窗口、自动更新分流数据。

## 节点准入（内置，不可关闭）

- 只收海外节点；弱加密、跳过证书校验的节点一律拒绝。
- 入池前用 mihomo 对 Google、Cloudflare 做真实连通测试，并经节点实测出口归属。
- 安全且实测延迟低于上限的节点直接入池，不分主池和备用池；延迟超标的安全节点不入池。
- 每个节点显示实测出口、安全等级、稳定等级、延迟和下行速度，名称按实测出口生成。

## 节点来源

内置的公益节点池（都是 GitHub 上公开的分享列表，更新频繁、质量不保证）：

- [MatinGhanbari/v2ray-configs](https://github.com/MatinGhanbari/v2ray-configs)
- [ripaojiedian/freenode](https://github.com/ripaojiedian/freenode)
- [mfuu/v2ray](https://github.com/mfuu/v2ray)
- [MhdiTaheri/V2rayCollector](https://github.com/MhdiTaheri/V2rayCollector)
- [ALIILAPRO/v2rayNG-Config](https://github.com/ALIILAPRO/v2rayNG-Config)
- [ermaozi/get_subscribe](https://github.com/ermaozi/get_subscribe)
- [free-nodes/v2rayfree](https://github.com/free-nodes/v2rayfree)
- [Leon406/SubCrawler](https://github.com/Leon406/SubCrawler)
- [Pawdroid/Free-servers](https://github.com/Pawdroid/Free-servers)
- [peasoft/NoMoreWalls](https://github.com/peasoft/NoMoreWalls)
- [ts-sf/fly](https://github.com/ts-sf/fly)

## 隐私

本应用没有遥测或统计上报。运行时的对外请求只有：

- 下载公益节点池的节点列表（直连）
- 经节点进行的测速（Google、Cloudflare）
- 经节点查询出口归属（ip-api.com）
- 下载分流规则数据

## 与上游 v2rayN 的差异

- 界面重做为「首页 / 节点管理 / 自动优化 / 设置」。
- 新增节点管家（`NexusLink/ServiceLib/Services/KeeperService.cs`）。
- 只随附 sing-box 与 mihomo 两个内核。上游的 Xray 配置生成代码仍在仓库中，但应用只为 sing-box 生成配置，mihomo 只用于节点实测。
- 已删除上游的这些功能及其代码：订阅管理、手动添加与编辑节点、路由规则与 DNS 设置窗口、自定义配置模板、备份还原、二维码识别、Clash 面板、检查更新、批量测速。

## 从源码构建

需要 .NET 10 SDK（Windows x64）：

```powershell
dotnet publish NexusLink\NexusLink\NexusLink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-win
dotnet test NexusLink\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
```

内核（`bin\sing_box\`、`bin\mihomo\`）和 `bin\srss\` 规则集不在仓库中，需自行放入。

## 内核与第三方组件

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [sing-box](https://github.com/SagerNet/sing-box) | 代理内核 | GPL-3.0 |
| [mihomo](https://github.com/MetaCubeX/mihomo) | 节点实测内核 | GPL-3.0 |
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 界面样式 | MIT |
| [ReactiveUI](https://github.com/reactiveui/ReactiveUI) | 界面数据绑定 | MIT |
| [2dust/sing-box-rules](https://github.com/2dust/sing-box-rules) | 内置 rule-set 数据源 | 见该项目 |

其余依赖见各项目文件。

## 免责声明

本项目仅供学习网络技术使用。使用者需遵守当地法律法规，并对自己的网络行为负责；使用公益节点的风险由使用者自行承担。
