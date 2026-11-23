# 畅连安全基线（Security Baseline）

| 项 | 内容 |
| --- | --- |
| 文档版本 | v1.0 |
| 日期 | 2026-11-21 |
| 适用对象 | 畅连（Windows 翻墙节点管家，NexusLink 的 GPL fork，sing-box 内核） |
| 用途 | 以五类权威来源为底座，驱动畅连安全防护功能实现；所有落点均在客户端侧 |
| 来源访问日期 | 除单独注明外均为 2026-11-21 |

---

## 0. 范围与威胁模型

### 0.1 产品形态

畅连是本地 Windows 客户端，工作流：从公开免费节点池抓取 vmess/vless/ss/trojan 分享链接 → 实测连通性 → 本地 sing-box urltest 自动优选 → 设置系统代理。畅连**不控制任何服务器端**，能控制的只有客户端侧的**准入、校验、路由与故障行为**——因此本基线的全部落地面都在客户端。

### 0.2 威胁模型

| 编号 | 威胁 | 典型路径 |
| --- | --- | --- |
| T1 | 恶意/钓鱼节点 | 出口节点实施 MITM、内容注入、凭据收割、劫持门户（captive portal） |
| T2 | DNS 泄漏 | 明文 UDP/53 直连 ISP、多宿主并行解析、应用绕过受控 DNS |
| T3 | 弱加密被识别 | TLS 1.0/1.1、CBC/流式旧加密、非 AEAD 的 vmess/ss、可指纹特征 |
| T4 | 订阅源被劫持 | http:// 订阅、中间人替换节点列表、钓鱼订阅 URL |
| T5 | 全节点失效时流量泄漏 | urltest 无可用节点后回退直连、系统代理被静默清除后裸奔 |

### 0.3 基线四原则

1. **默认安全**：一切校验开关默认"严格"，放宽必须用户显式确认（五类来源的一致要求，见 §1–§5）。
2. **故障关闭（fail closed）**：任何失效场景（节点全挂、订阅校验失败、DNS 不可用）的兜底行为是"阻断"，不是"直连"（NIST §4.5.1 "路径校验失败即断连"的同构原则）。
3. **信任边界最小化**：订阅内容、分享链接、拨测响应一律按不可信输入处理（OWASP 输入校验准则）。
4. **出口唯一性**：任何流量要么可归因到"经当前选中节点出站"，要么被阻断；不存在第三态"静默直连"。

---

## 1. Cloudflare —— TLS/HTTPS 强制与加密 DNS

### 1.1 来源

1. [SSL/TLS encryption modes — Cloudflare Docs](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes)
2. [Deprecating TLS 1.0 and 1.1 on api.cloudflare.com — Cloudflare Blog，2018-03-12](https://blog.cloudflare.com/deprecating-old-tls-versions-on-cloudflare-dashboard-and-api)
3. [1.1.1.1 Encryption（DoH/DoT/ODoH）— Cloudflare Docs](https://developers.cloudflare.com/1.1.1.1/encryption/)

### 1.2 核心准则（原文摘录）

- **强烈建议全链路加密 + 证书校验**："If possible, Cloudflare strongly recommends using Full or Full (strict) modes to prevent malicious connections to your origin."（来源 1）
- **"加密但不校验证书"被明确定性为弱配置**：Full 模式 "uses HTTPS without validating the origin's certificate. This mode is common for origins that use self-signed or otherwise invalid certificates"；Full (strict) 则 "with added validation of the origin server's certificate"。（来源 1）
- **TLS 版本下限 1.2**："TLS versions below 1.2 will no longer be supported as of June 4, 2018"；"PCI … recommends [TLS 1.2], and NIST requires at least TLS 1.2"；并提供 "Minimum TLS Version" 控制项按版本强制执行。（来源 2）
- **明文 DNS 可被监听/篡改/追踪**："Traditionally, these queries are sent in plaintext — unencrypted and readable by anyone on the network path. Unencrypted DNS queries can be monitored, modified, or used for tracking by ISPs, network operators, or malicious actors."；对策：DoT（专用 TLS 连接，端口 853）、DoH（封装在 443 端口 HTTPS 内）、ODoH。（来源 3）

### 1.3 对畅连的可执行落地面

| 落地点 | 实现方式 |
| --- | --- |
| 订阅抓取 = 客户端版 "Full (strict)" | 订阅 URL 仅接受 https://；拉取时完整执行证书链 + 域名 + 有效期校验；产品内不提供"跳过证书校验"入口；节点配置携带 `allowInsecure=true` 时该节点标红禁用 |
| 节点 TLS "Minimum Version" | 拨测阶段记录节点实际协商出的 TLS 版本；< 1.2 自动置为不可用并标注原因（镜像 Cloudflare 的 Minimum TLS Version 控制项） |
| 拨测 URL 强制 HTTPS | 连通性/延迟测试一律走 https://（gstatic generate_204 类），不接受用户配置 http:// 测试地址 |
| DNS 走 DoH/DoT | sing-box DNS 上游默认 DoH（1.1.1.1 / dns.google 等）且经代理出口；任何生成的配置中不允许出现明文 UDP/53 直连上游 |

---

## 2. Mozilla —— Server Side TLS 指南与信息安全准则

### 2.1 来源

1. [Security/Guidelines/Web Security（Web Security Cheat Sheet）— MozillaWiki](https://wiki.mozilla.org/Security/Guidelines/Web_Security)，由 Mozilla 企业信息安全（EIS）团队维护
2. [Server Side TLS — 官方迁移公告（mozilla.github.io）](https://mozilla.github.io/server-side-tls)：原 wiki 公共指南已移交社区工作组项目 [TLSRef](https://docs.tlsref.org/)，SSL 配置生成器（ssl-config.mozilla.org）同步迁移

> 取证说明：原 wiki.mozilla.org/Security/Server_Side_TLS 页面当前设有反爬限制，无法直接抓取；本基线以 Mozilla 官方 GitHub Pages 迁移公告确认其现行位置（TLSRef），并以 Mozilla EIS 维护的 Web Security 准则作为准则引用主体。

### 2.2 核心准则（原文摘录）

- **所有通信强制安全协议**：HTTPS 条目为 Mandatory——"Sites should use HTTPS (or other secure protocols) for all communications"；"Websites must redirect to HTTPS, API endpoints should disable HTTP entirely"。（来源 1）
- **TLS 配置分档**：TLS Configuration 为 Mandatory——"Use the most secure Mozilla TLS configuration for your user base, typically Intermediate"；面向现代客户端可用 Modern 档（Firefox 27/Chrome 22/IE11 起）。（来源 1）
- **资源加载不允许明文**："All resources — whether on the same origin or not — should be loaded over secure channels."（来源 1）
- **HSTS**：最低六个月（15768000），推荐两年（63072000）。（来源 1）
- **pinning 高风险、需极端谨慎**："Due to the risk of knocking yourself off the internet, HPKP must be implemented with extreme care … it is _not recommended_ for the majority websites to implement HPKP."（来源 1）

### 2.3 对畅连的可执行落地面

- **自身外联零明文**：订阅、内核/资源更新、拨测、遥测（若有）全部 https，无任何 http 回退路径（"API endpoints should disable HTTP entirely" 的客户端同构）。
- **默认取 Modern 档语义**：畅连只面向现代 Windows（无需兼容旧浏览器），TLS 能力直接按 Modern 档收敛：仅 TLS 1.2/1.3 + AEAD 套件；具体套件表以 TLSRef 现行版本为准。
- **订阅 pinning 若实现，必须带逃生门**：备份 pin + 一键清除，否则一次误配置就是 "knocking yourself off the internet"（对应清单 #24）。
- **HSTS 思想的客户端化**：对已知订阅域名记住"仅 https"，用户后续粘贴/导入 http:// 同域名 URL 时直接拒绝并提示历史记录。

---

## 3. OWASP —— Cheat Sheet Series（传输安全 + 输入校验）

### 3.1 来源

1. [Transport Layer Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html)（注：常被引用的 Transport Layer Protection Cheat Sheet 已标注 **DEPRECATED**，其现行继任者即本篇）
2. [Input Validation Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html)

### 3.2 核心准则（原文摘录）

- **版本底线**："Web applications must default to TLS 1.3 and may support TLS 1.2 for compatibility. TLS 1.0 and TLS 1.1 are formally deprecated by RFC 8996 (March 2021) and must be disabled. … SSLv2 and SSLv3 must always be disabled."（来源 1）
- **套件底线**："For TLS 1.3, use the standard AEAD cipher suites (AES‑GCM or ChaCha20‑Poly1305)."；TLS 1.2 "prefer AEAD‑based suites there as well and avoid CBC‑mode ciphers"；Null / 匿名 / EXPORT / 无前向保密的 RSA 密钥传输与静态 DH 套件一律禁用。（来源 1）
- **禁用 TLS 压缩**（CRIME 侧信道）、**及时给加密库打补丁**。（来源 1）
- **厚客户端适合 pinning**："public key pinning can still provide security benefits for mobile applications, thick clients and server-to-server communication."（来源 1）
- **证书域名匹配**："The domain name (or subject) of the certificate must match the fully qualified name of the server"；证书不应包含 IP 地址。（来源 1）
- **输入校验 = 白名单**："Define what the application accepts and reject values outside those rules."；"Apply request size limits before buffering or parsing input"；JSON 校验需 "explicitly configure required and additional properties"——未知字段必须拒收；"Reject invalid requests with a clear error; do not continue with partially validated data."（来源 2）
- **信任边界**："Validate data from internal APIs, partner feeds, queues, and stored records when it crosses a trust boundary."（来源 2）
- **日志不落原文**："Record the failure and relevant metadata without secrets or full request bodies."（来源 2）

### 3.3 对畅连的可执行落地面

| 落地点 | 实现方式 |
| --- | --- |
| 分享链接解析器 = 信任边界 | 订阅返回的每行内容、剪贴板导入的每条链接均按不可信输入：先限长（单行 ≤ 4KB、订阅体 ≤ 5MB）再解析；vmess（base64 JSON）按字段白名单、ss 严格 SIP002、vless/trojan 按 URI 参数白名单；未知字段/畸形值丢弃该节点并计入"拒收统计"，绝不"部分通过" |
| 端口与地址校验 | port ∈ [1, 65535]；地址为域名时校验合法字符与长度，为 IP 时校验格式；ws path / Host 头字段拒绝 CR/LF 注入 |
| 弱加密 = 非 AEAD 一律拒收 | SS 只收 AEAD 套件；vmess 只收 AEAD（alterId=0）——与 OWASP "avoid CBC/Null/EXPORT" 的禁用逻辑同构 |
| 日志脱敏 | 节点 uuid/password、订阅 URL 在日志与崩溃报告中默认脱敏 |

---

## 4. NIST —— SP 800-52 Rev.2（TLS 选型与 FIPS 批准套件范围）

### 4.1 来源

1. [SP 800-52 Rev. 2, Guidelines for the Selection, Configuration, and Use of TLS Implementations — CSRC 出版页](https://csrc.nist.gov/pubs/sp/800/52/r2/final)（2019-08 发布；注意：页面标注 2026-05-07 起处于复审中）
2. 同文档 PDF 全文（72 页）：[NIST.SP.800-52r2.pdf](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-52r2.pdf)（本基线引用 §2.6、§3.1、§3.3、§3.4.3、§3.6、§4.1、§4.4.3、§4.5.1、§4.6、§4.8）

### 4.2 核心准则（原文摘录）

- **客户端版本要求（§4.1）**："The client shall be configured to use TLS 1.2 and should be configured to use TLS 1.3 … The client shall not be configured to use SSL 2.0 or SSL 3.0."（服务器侧 §3.1："shall not use TLS 1.0, SSL 3.0, or SSL 2.0"）
- **FIPS 批准的 TLS 1.3 套件（§3.3.1.2）**：TLS_AES_128_GCM_SHA256、TLS_AES_256_GCM_SHA384、TLS_AES_128_CCM_SHA256、TLS_AES_128_CCM_8_SHA256 —— **清单内不含 ChaCha20-Poly1305**（NIST 批准算法族为 AES-GCM/CCM；ChaCha20 按 OWASP/Mozilla 通用实践可用，但不在 NIST 批准范围内）。
- **TLS 1.2 批准套件（§3.3.1.1）**：ECDHE + AES-GCM 一族，如 TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256（0xC0, 0x2F）、TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384（0xC0, 0x30）等。
- **木桶原则（§3.3.2）**："The security guarantees of the cryptography are limited to the weakest cipher suite supported by the configuration."
- **路径校验失败必须断连（§4.5.1）**："The client shall terminate the TLS connection if path validation fails."
- **0-RTT 禁令（§4.4.3 / §4.6）**：Early Data Indication 列入 "Discouraged TLS Extensions"；"clients using TLS 1.3 should not send 0-RTT data"；"TLS 1.2 clients shall not use False Start."（§2.6：TLS 自带防重放保护，"except when 0-RTT data … is sent in TLS 1.3"）
- **压缩禁用（§3.7 / §4.7）**：仅允许 null compression，其余全部禁用。
- **补丁要求（§4.8）**："The client and associated platform shall be kept up-to-date in terms of security patches."
- **CT/SCT（§3.4.2.15）**：Certificate Transparency 通过公开证书日志、监控与审计降低证书类威胁。

### 4.3 对畅连的可执行落地面

- **节点 TLS 准入条款（以 §4.1 为蓝本）**：客户端 TLS 栈配置 ≥ TLS 1.2、优先 1.3、禁 SSLv2/v3；节点侧拨测协商出 < 1.2 即拒绝。
- **加密白名单分级**：FIPS 对齐优先 AES-GCM（vless/trojan 的 TLS 套件偏好）；chacha20-ietf-poly1305 作为通用可接受项保留（OWASP 档）；SS 的 2022-blake3-aes-* 为最高档。
- **木桶原则 → 池级水位**：节点池加密水位由最弱节点决定 UI 呈现（如"池内仍有 N 个弱加密节点"横幅），弱节点默认踢出而非共存。
- **0-RTT / early data 默认关**：vless 的 early data（max-early-data / pbk 参数）与 TLS 1.3 0-RTT 一律默认不启用。
- **路径校验失败 = 断连**：订阅、更新、拨测三类外联的 TLS 失败行为统一为"终止 + 报错"，代码路径上不存在"继续但忽略校验"分支。

---

## 5. Google —— 证书信任与透明度

### 5.1 来源

1. [Certificate Transparency in Chrome — Chrome CT Policy](https://googlechrome.github.io/CertificateTransparency/ct_policy.html)
2. [Chrome Root Program Policy v1.8（2026-02-05）](https://googlechrome.github.io/chromerootprogram/crp/policy)

### 5.2 核心准则（原文摘录）

- **公网证书必须 CT 合规**："In CT-enforcing versions of Chrome, all publicly-trusted TLS certificates are required to be CT Compliant to successfully validate."；嵌入 SCT 要求：证书有效期 ≤ 180 天需来自 ≥ 2 个不同 log operator 的日志，> 180 天需 3 个。（来源 1）
- **信任库是受治理的封闭集合**："Google Chrome relies on Certification Authority systems … to issue certificates to websites"；根证书入选依据公开可验证信息与持续审查；"Certificates included in the Chrome Root Store must provide value to Chrome end users that exceeds the risk of their continued inclusion."（来源 2）
- **透明度依赖新鲜度**：Chrome 若 70 天未能更新 CT 日志列表则停止强制 CT——强制能力本身依赖及时更新。（来源 1）

### 5.3 对畅连的可执行落地面

- **不自建信任、不装根证书**：畅连绝不为任何"解密审计"功能安装自定义根 CA（本地 MITM）；证书校验只依赖 Windows 系统信任库 + 系统补丁（"受治理信任库"原则的客户端同构）。
- **订阅源证书自检（增强）**：对高价值订阅域名可检测证书 SCT 存在性作为弱信号；异常（无 SCT、新近签发、签发者突变）时提示用户。
- **校验新鲜度提示**：长期离线会使依赖更新数据的校验退化（CT 70 天机制的启示），UI 显示"最后在线校验时间"。

---

## 6. 可实现检查清单（共 24 条：必做 17 / 建议 7）

**威胁覆盖映射**：T1 恶意/钓鱼节点 → #8、#11、#12、#13、#23、#24；T2 DNS 泄漏 → #15、#16、#18；T3 弱加密被识别 → #5、#6、#7、#9、#10；T4 订阅源被劫持 → #1、#2、#4；T5 全节点失效泄漏 → #14、#15、#16、#17。

### 订阅与节点导入（T4 / T1）

1. 【必做】订阅仅允许 https:// 且拉取时执行完整证书校验（链 + 域名 + 有效期），失败即中止报错；产品不提供任何"跳过证书校验"入口（镜像 Cloudflare Full (strict)；NIST §4.5.1）。
2. 【必做】订阅与分享链接按不可信输入处理：限长后白名单解析（vmess JSON 字段表 / ss SIP002 / vless、trojan URI 参数表），未知字段、端口 ∉ [1,65535]、超长值一律丢弃该节点并计入拒收统计，绝不部分通过（OWASP 输入校验）。
3. 【必做】节点地址解析命中私网/环回/链路本地段（RFC1918、127/8、169.254/16、IPv6 ULA 与 ::1）即拒收并告警，防 SSRF 与恶意节点探测内网。
4. 【建议】订阅支持绑定指纹（证书公钥指纹或订阅内容哈希），节点列表突变（如一次性替换 > 50%）需用户确认后生效。

### 协议与加密准入（T3）

5. 【必做】客户端 TLS 栈与节点准入下限 1.2、优先 1.3：拨测记录节点实际协商版本，< 1.2 或 SSLv3 一律置为不可用并注明原因（NIST §4.1/§3.1；OWASP）。
6. 【必做】Shadowsocks 仅接受 AEAD（aes-128/256-gcm、chacha20-ietf-poly1305、2022-blake3-* 家族），rc4/cfb/ctr 等旧加密一律拒收；FIPS 对齐场景优先 AES-GCM（NIST §3.3.1.2 批准清单无 ChaCha20）。
7. 【必做】vmess 仅接受 AEAD（alterId=0 且启用 AEAD 头），legacy 非 AEAD 拒收；UI 优先引导 vless+REALITY / trojan 等更抗探测形态。
8. 【必做】trojan/vless 载荷必须 TLS 或 REALITY 承载，明文或自签且 allowInsecure 的组合标红禁用；节点配置携带 `allowInsecure=true` 默认拒绝导入（Cloudflare 对 Full 不校验证书的定性：弱配置）。
9. 【必做】默认禁用 0-RTT / early data（vless max-early-data、TLS 1.3 0-RTT），仅用户显式开启并附带回放风险提示（NIST §4.6："clients using TLS 1.3 should not send 0-RTT data"）。
10. 【建议】拨测采集节点 TLS 协商参数（版本/套件/证书链/SNI 一致性）存为节点元数据，优选评分纳入"加密质量"权重，弱配置自动降权。

### 拨测与优选（T1）

11. 【必做】每个候选节点的连通性测试必须经该节点出站（sing-box 内按节点建立独立出站，拨测 https://…/generate_204 类 URL），禁止直连测速，防测速失真与真实 IP 泄漏。
12. 【必做】拨测校验响应完整性：状态码与响应体特征比对，非预期响应（劫持门户、注入内容、非 204）自动标记"可疑"并剔除本轮优选。
13. 【建议】定期经节点拉取固定已知资源比对哈希（内容完整性抽检），连续异常的节点标记"疑似恶意出口"并全局提示。

### 故障关闭与流量防护（T5 / T2）

14. 【必做】全部节点失效时 fail closed：系统代理保持指向本地端口且本地端口返回拒绝（阻断流量）并弹窗告警；绝不静默回退直连，任何"恢复直连"操作需用户手动确认。
15. 【必做】DNS 接管：TUN/劫持模式下将全部 53 端口（UDP/TCP）流量劫持进 sing-box 受控解析，上游仅 DoH/DoT 且经代理出口（Cloudflare 对明文 DNS 的定性）；系统代理模式提供"防火墙阻断非内核进程直连 53"的一键开关；不生成任何明文 UDP DNS 直连配置。
16. 【必做】IPv6 泄漏检测与接管：检测系统裸 IPv6 出口，TUN 模式接管 v6 路由，无法接管时显著提示用户处置，防应用经 v6 直连绕过。
17. 【必做】系统代理绕过列表默认仅 localhost 与 RFC1918 私网段，且私网目标绝不经节点转发（防恶意出口探测内网）；UWP 应用 loopback 受限导致的绕行给出修复入口与提示。
18. 【建议】内置泄漏自检页（出口 IP、DNS 出口一致性、IPv6/WebRTC 检测），一键生成报告，不一致项直接关联到对应设置项。

### 应用自身与数据安全

19. 【必做】程序更新、sing-box 内核与 geoip/geosite 资源仅经 https 下载并校验哈希/签名（Release 资产比对），默认经代理通道获取，防供应链劫持（NIST §4.8 补丁要求）。
20. 【必做】日志与崩溃报告脱敏：订阅 URL、节点 uuid/password、服务器地址默认脱敏，不落明文（OWASP 日志准则）。
21. 【建议】凭据存储加固：节点配置中的密码/uuid 用 Windows DPAPI 加密存储，导出配置默认脱敏。
22. 【建议】系统时钟偏差检测：vmess 请求头依赖时间戳防重放，偏差超阈值时拒连 vmess 并提示校时。

### 信任与透明度（T1 证书层）

23. 【必做】不安装任何自定义根证书、不提供本地 MITM 解密功能；证书校验完全依赖 Windows 系统信任库并随系统更新（Chrome Root Program 受治理信任库原则）。
24. 【建议】可选订阅源证书公钥 pinning（OWASP：pinning 适合 thick client），必须带备份 pin 与一键清除逃生门（Mozilla HPKP 风险教训）。

---

## 7. 待证项与后续跟进

1. **sing-box 版本对齐**：本基线对 DNS 劫持 / fakeip / urltest / 按节点拨测的描述以 sing-box 1.11+ 为背景；其 DNS 配置结构在 1.11 起有重构，实现时以所用版本文档为准，落地前逐条核对配置键名。
2. **REALITY 指纹参数（fp）合规范围**：uTLS 指纹模拟的检测面与合规性建议以 Xray/sing-box 官方文档与社区实测为准，本基线未展开。
3. **长期离线下的校验退化**：CT 70 天日志时效机制的启示——畅连长期离线时订阅证书自检会退化，实现清单 #5.3 时补"最后在线校验时间"提示。
4. **Windows 多宿主名称解析（SMHNR）**：Win10+ 已收紧并行 DNS 查询，但第三方网络工具可能复活该路径，建议纳入清单 #18 的泄漏自检覆盖。

---

## 附：来源一览

| 类别 | 来源 | 性质 |
| --- | --- | --- |
| Cloudflare | [SSL/TLS encryption modes](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes) | 官方文档 |
| Cloudflare | [Deprecating TLS 1.0 and 1.1 on api.cloudflare.com](https://blog.cloudflare.com/deprecating-old-tls-versions-on-cloudflare-dashboard-and-api)（2018-03-12） | 官方博客 |
| Cloudflare | [1.1.1.1 Encryption](https://developers.cloudflare.com/1.1.1.1/encryption/) | 官方文档 |
| Mozilla | [Security/Guidelines/Web Security](https://wiki.mozilla.org/Security/Guidelines/Web_Security) | 官方 Wiki（EIS 维护） |
| Mozilla | [Server Side TLS 迁移公告](https://mozilla.github.io/server-side-tls) → [TLSRef](https://docs.tlsref.org/) | 官方公告 / 继任项目 |
| OWASP | [Transport Layer Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html)（前 Transport Layer Protection CS 已弃用） | 官方 Cheat Sheet |
| OWASP | [Input Validation Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html) | 官方 Cheat Sheet |
| NIST | [SP 800-52 Rev.2](https://csrc.nist.gov/pubs/sp/800/52/r2/final)（CSRC 页 + [PDF 全文](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-52r2.pdf)） | 官方标准 |
| Google | [Chrome CT Policy](https://googlechrome.github.io/CertificateTransparency/ct_policy.html) | 官方政策 |
| Google | [Chrome Root Program Policy v1.8](https://googlechrome.github.io/chromerootprogram/crp/policy)（2026-02-05） | 官方政策 |
