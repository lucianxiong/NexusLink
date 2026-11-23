# NexusLink（畅连）

> **来源声明**：本仓库是 [v2rayN](https://github.com/2dust/v2rayN) **7.25.6**（作者 [2dust](https://github.com/2dust)，许可证 [GPL-3.0](LICENSE)）的修改版，应用更名为 NexusLink / 畅连，仅供本机自用，未公开发布。
>
> 修改内容：应用更名与界面重做、内置「节点管家」（`NexusLink/ServiceLib/Services/KeeperService.cs`）、移除 Avalonia 桌面版、AmazTool 以及 Linux / macOS 打包脚本。
>
> 下文的徽章、下载、Wiki、GPG 指纹均属**上游**，链接已指回 2dust/v2rayN。
> 上游源码：https://github.com/2dust/v2rayN （tag 7.25.6）

---

## 本地构建（Windows x64，单文件）

```powershell
dotnet publish NexusLink\NexusLink\NexusLink.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-win
```

解决方案只保留 4 个可构建项目：`NexusLink`（WPF 界面）、`ServiceLib`、`ServiceLib.UdpTest`（测速功能的依赖）、`ServiceLib.Tests`。

使用说明与节点来源说明见 [README-畅连.md](README-畅连.md)。

---
### A GUI client for Windows, Linux and macOS. Support [Xray](https://github.com/XTLS/Xray-core) and [sing-box](https://github.com/SagerNet/sing-box) and [others](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)

[![CodeFactor](https://www.codefactor.io/repository/github/2dust/v2rayN/badge)](https://www.codefactor.io/repository/github/2dust/v2rayN)
[![Release](https://img.shields.io/github/v/release/2dust/v2rayN?logo=github&label=Release)](https://github.com/2dust/v2rayN/releases)
[![Downloads](https://img.shields.io/github/downloads/2dust/v2rayN/latest/total?logo=github&label=Downloads)](https://github.com/2dust/v2rayN/releases)
 
[![Windows](https://img.shields.io/badge/Windows-supported-0078D6?logo=windows)](https://github.com/2dust/v2rayN) 
[![Linux](https://img.shields.io/badge/Linux-supported-FCC624?logo=linux&logoColor=000)](https://github.com/2dust/v2rayN) 
[![macOS](https://img.shields.io/badge/macOS-supported-000000?logo=apple)](https://github.com/2dust/v2rayN) 
[![GPG Signed](https://img.shields.io/badge/GPG-signed-4B32C3?logo=gnuprivacyguard)](https://github.com/2dust/v2rayN)


---

## Download / 下载

Download the latest release here:

在这里下载最新版本：

[https://github.com/2dust/v2rayN/releases](https://github.com/2dust/v2rayN/releases)


> [!TIP]
> NexusLink is the desktop version. For the mobile version, please visit the v2rayNG \
> NexusLink 是电脑版，手机版请访问 v2rayNG
>
> https://github.com/2dust/v2rayNG

---

## Documentation / 使用文档

Read the Wiki for usage guides and configuration details.

请阅读 Wiki 获取使用说明和配置教程。

[https://github.com/2dust/v2rayN/wiki](https://github.com/2dust/v2rayN/wiki)

---

## Supported Platforms / 支持平台

| Platform / 平台 | x64 | x86 | arm64 | riscv64 | loong64 |
| --- | --- | --- | --- | --- | --- |
| Windows | ✅ | ✅ | ✅ | - | - |
| Linux | ✅ | - | ✅ | ✅ | ✅ |
| macOS | ✅ | - | ✅ | - | - |

Minimum OS requirements: [Release files introduction](https://github.com/2dust/v2rayN/wiki/Release-files-introduction) / 最低系统要求：[发布文件介绍](https://github.com/2dust/v2rayN/wiki/Release-files-introduction)

---

## GPG Verification / GPG 签名校验

Release files are signed with GPG to verify authenticity and integrity, helping prevent mirror, ISP, or CDN hijacking.

发布文件已使用 GPG 签名，可用于校验文件真实性与完整性，预防镜像站、运营商或 CDN 劫持。

### Fingerprint / 公钥指纹

```text
7694 5E9F 3E9A 168F 8070 F195 805D 661C
134D FAF6 8903 C199 463C 31E5 AE90 3AE0
```

---

## Community / 社区

Telegram Channel / Telegram 频道：

[https://t.me/github_2dust](https://t.me/github_2dust)
