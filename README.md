# 畅连 NexusLink

Windows 代理客户端，内置「节点管家」：自动从公益节点池获取节点、逐个实测，只把能用的装进来；线路断掉或明显变差时自动换线。打开应用，点「立即连接」即可上网，不需要懂任何概念。

> **来源与许可**：本项目是 [v2rayN](https://github.com/2dust/v2rayN) 7.25.6（作者 [2dust](https://github.com/2dust)，GPL-3.0）的修改版，应用更名为「畅连 NexusLink」，界面与节点管理逻辑重做，并以 [GPL-3.0](LICENSE) 发布。修改内容见下文「与上游的差异」。

## 必读：节点从哪来

- 默认节点**全部来自公益节点池**：由陌生志愿者无偿提供，来源不可核实，随时可能失效。
- 你的流量会经过这些服务器。网页内容本身由 HTTPS 加密，但节点方能看到你连接了哪些域名、你的真实 IP 和流量特征。
- **请不要在公益节点上登录网银、支付、主力邮箱等敏感账号**；重要账号请开启两步验证。
- 当前版本**只使用内置公益节点池，没有添加自有订阅 / 节点的界面入口**（相关代码保留，界面未开放）。

## 主要功能

- **一键连接 / 断开**：打开畅连默认未连接，点「连接」才连（系统代理模式）；默认「绕过大陆」路由，国内网站直连、不经节点
- **断开即停内核**：断开连接或退出应用后，内核进程（sing-box / mihomo）全部结束，后台不留代理进程
- **TUN 模式**（设置里开启，需要管理员权限）：开启后所有程序的网络都经畅连出网，终端、命令行 agent 等不遵循系统代理的程序也能用。「连接 / 断开」仍是总开关，未连接时 TUN 不生效；开启时畅连会以管理员身份重启，之后每次启动也会请求管理员权限
- **节点管家**：抓取 → 安全检查 → 实测 → 入池 → 自动选路，全程自动
- **选路顺序**：安全 > 稳定 > 带宽 > 延迟；在用的线路只要没问题就不切换
- **手动指定**：节点页点「用这个节点」锁定线路，随时「恢复自动」
- **延迟上限**：500 / 800 / 1200 毫秒可选（默认 1200），实测延迟超过上限的节点只留作备用
- **节点信息实测可得**：每个节点显示实测出口（国家 / 城市 / 机房或家宽）、安全等级、稳定等级、延迟、下行速度；节点名称直接按实测出口生成（国旗 国家 城市 · 协议），不采用节点池写的名称，节点池原名只在悬停提示里可见
- **参数设置**：TUN 模式、开机自动启动、启动后隐藏窗口、分流规则自动更新（首页右上角齿轮）

## 节点准入规则（内置，不可关闭）

- **只收海外节点**：名称标注中国大陆 / 港澳台的节点不进池；实测出口在中国大陆 / 港澳台的同样不进池
- **弱加密一律拒绝**：明文传输、老旧加密方式、跳过证书校验（allowInsecure / insecure）的节点不入池
- **安全等级**：高 = 带 TLS / Reality 加密传输；中 = 已加密但流量特征可被识别（如 Shadowsocks 直连）；低于「中」的拒绝
- **实测准入**：先用 mihomo 对两个 HTTPS 目标（Google、Cloudflare）真测连通与延迟，通过的才进主池

## 节点来源

内置的公益节点池（全部是 GitHub 上公开的分享列表，更新频繁、质量不保证）：

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

## 下载与运行

仅提供 **Windows x64** 版本（Windows 10 / 11），自包含单文件，无需安装 .NET 运行时：

1. 打开 [Releases](../../releases/latest)，下载 `NexusLink-win-x64-<版本>.zip` 和同名的 `.sha256` 校验文件
2. 校验压缩包 SHA-256 与 `.sha256` 文件一致后解压到任意目录
3. 运行 `NexusLink.exe`，点「立即连接」

压缩包内自带 sing-box 与 mihomo 内核（`bin\` 目录），请整个目录一起保留。

## 隐私

本应用不包含遥测或统计上报。运行时的对外网络请求只有：

- 从上面的公益节点池下载节点列表（直连）
- 经节点进行的测速：Google / Cloudflare 的 204 探测、Cloudflare 的下载测速文件
- 查询节点出口归属：ip-api.com（查询对象是节点的出口 IP，请求经该节点发出）
- 内核启动时按需下载规则数据（geo / rule-set）

## 从源码构建

需要 .NET 10 SDK，Windows x64：

```powershell
dotnet publish NexusLink\NexusLink\NexusLink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-win
```

单元测试（含节点准入、选路、稳定等级等 180 余项）：

```powershell
dotnet test NexusLink\ServiceLib.Tests\ServiceLib.Tests.csproj -c Release
```

内核二进制（`bin\sing_box\`、`bin\mihomo\`）与 `bin\srss\` 规则集不随源码仓库分发，需自行从各内核项目的 Release 获取并放入对应目录。

## 内核与第三方组件

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| [sing-box](https://github.com/SagerNet/sing-box) | 主代理内核 | GPL-3.0 |
| [mihomo](https://github.com/MetaCubeX/mihomo) | 节点管家测速内核 | GPL-3.0 |
| [MaterialDesignInXamlToolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 界面样式 | MIT |
| [ReactiveUI](https://github.com/reactiveui/ReactiveUI) | 界面数据绑定 | MIT |
| [2dust/sing-box-rules](https://github.com/2dust/sing-box-rules) | 内置 rule-set 数据源 | 见该项目 |

其余 NuGet 依赖见各项目文件，许可以各项目为准。

## 与上游 v2rayN 的差异

- 界面精简为「首页总览 / 节点管理 / 自动优化 / 设置」四页
- 新增节点管家（`NexusLink/ServiceLib/Services/KeeperService.cs`）：公益池抓取、安全与海外双重准入、实测、自动选路与防抖、出口归属与带宽标注
- 随附内核只保留 sing-box 与 mihomo，移除 Xray；移除 Avalonia 跨平台界面与 Linux / macOS 打包脚本；移除应用自更新检查
- 设置窗口只有一页，只保留在本应用里真正生效的选项：TUN 模式、开机自动启动、启动后隐藏窗口、自动更新分流数据；流量统计、去重、字体、根证书、测速地址、Geo 来源等上游设置项已删除。本地端口、系统代理例外、TUN 的高级参数（栈 / MTU / 严格路由等）等上游设置页在当前界面**没有入口**（代码保留，沿用默认值；TUN 开关本身在参数设置里）；订阅管理、路由规则、DNS 设置等上游高级窗口同样**没有入口**

## 免责声明

本项目仅供学习网络技术使用。使用者需遵守当地法律法规，并对自己的网络行为负责；使用公益节点带来的风险由使用者自行承担。
