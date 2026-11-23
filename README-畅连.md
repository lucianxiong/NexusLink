# 畅连 NexusLink

免费节点获取与连接工具 —— 改名自 NexusLink 7.25.6。

## 这是什么

一个开箱即用的翻墙客户端：打开就有一个「畅连」首页，点一下「立刻找新线路」，
它会自动从全球公益节点池抓取免费节点、逐个实测连通性，只把能用的装进来，
并且始终自动切换到最快的那条线路。你不需要懂任何概念。

## 核心能力

- **自动切换最快线路**：基于策略组（urltest/LeastPing），线路变慢或断掉秒级切换
- **每天自动换血**：每天 09:00 / 21:00 从 6 个全球公益池抓取新节点，实测后替换
- **安全准入**（参考 Cloudflare/Mozilla/OWASP/NIST 基线，见 docs/security-baseline.md）：
  - 只收录 TCP 实测可达的节点（死节点进不来）
  - Shadowsocks 仅允许 AEAD / 2022 系强加密（原生 IsValid 强制）
  - 节点池仅从 HTTPS 源抓取，镜像走 gh-proxy.com
  - 系统代理模式下核心不可达时连接失败（fail-closed），不会悄悄裸连
  - 国内网站 / 网银走「绕过大陆」路由直连，不经过任何节点
- **新节点隔离观察**：新抓取的节点先经 TCP 预检，不足 3 个可用时整轮放弃，保持原线路
- 高级功能（订阅、路由、DNS、TUN、测速…）全部保留，在左侧导航的其他标签页

## 致谢与许可（GPL-3.0）

本程序是 [NexusLink](https://github.com/2dust/NexusLink)（作者 2dust，GPL-3.0）的修改版 fork。
感谢 NexusLink 及其上游项目 Xray / sing-box 的全部贡献者。完整许可见 [LICENSE](LICENSE)。

修改内容：应用更名与界面调整、内置节点管家（ServiceLib/Services/KeeperService.cs）。
上游源码：https://github.com/2dust/NexusLink （tag 7.25.6）

## 免责声明

免费节点由全球志愿者维护，请勿通过其登录网银、支付或敏感账号（重要账号请开两步验证）。
本项目仅供学习网络技术使用，使用者需遵守当地法律法规。
