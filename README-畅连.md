# 畅连 NexusLink

免费节点获取与连接工具，基于 v2rayN 7.25.6 改名和重做界面，内置「节点管家」。

## 这是什么

一个打开就能用的代理客户端：首页点「立即连接」即可出网；节点管家自动从公益节点池抓取节点、
逐个实测，只把能用的装进来，并在线路变慢或断掉时切到最快的可用线路。你不需要懂任何概念。

## 节点从哪来（请务必了解）

- **默认节点全部来自公益池**：由陌生志愿者无偿提供，来源不可核实。
- 你的流量会经过这些服务器。虽然 HTTPS 的内容本身是加密的，但对方能看到你连接了哪些域名、你的真实 IP 和流量特征。
- **请不要在公益节点上登录网银、支付、主力邮箱等敏感账号**；重要账号请开启两步验证。
- 更稳妥的做法是在「参数设置 → 订阅」里添加自己的付费订阅，然后在节点列表里选用自己的节点。

## 节点管家做了什么

- **实测准入**：TCP 预筛后，再用 mihomo 对两个 HTTPS 目标真测速，通过的才入池
- **自动切换**：基于策略组（LeastPing），线路变慢或断掉时自动跳线
- **定时刷新**：按设定间隔从公益池增量补位，不盲目替换现有可用线路
- **安全准入**：Shadowsocks 只接受 AEAD / 2022 系强加密；跳过证书校验的节点一律拒绝（详见 docs/security-baseline.md）
- 国内网站走「绕过大陆」路由直连，不经过节点

## 内核

| 内核 | 作用 |
| --- | --- |
| sing-box | 主代理内核，承载日常连接 |
| mihomo | 节点管家测速用，带 clash API |

xray 及其地理数据已移除：当前配置下没有任何协议使用它。

## 高级功能

订阅、路由、DNS、TUN、热键、WebDAV 备份全部保留在代码里，通过首页右上角的齿轮进入「参数设置」。

## 本地构建

需要 .NET 10 SDK（本机位于 `D:\dotnet-sdk10\dotnet.exe`，不在 PATH）：

```powershell
dotnet publish NexusLink\NexusLink\NexusLink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-win
```

`NexusLink.sln` 与 `NexusLink.slnx` 内容一致，均只含 4 个可构建项目。

## 致谢与许可（GPL-3.0）

本程序是 [v2rayN](https://github.com/2dust/v2rayN)（作者 2dust，GPL-3.0，tag 7.25.6）的修改版。
感谢 v2rayN 及其上游项目 Xray / sing-box / mihomo 的全部贡献者。完整许可见 [LICENSE](LICENSE)。

修改内容：应用更名与界面重做、内置节点管家（`NexusLink/ServiceLib/Services/KeeperService.cs`）。

## 免责声明

本项目仅供学习网络技术使用，使用者需遵守当地法律法规。
