namespace ServiceLib.Tests.Services;

public class KeeperAdmissionTests
{
    // ---- 候选闸：低安全节点不进候选（换血与巡检补位共用这道闸）----
    [Test]
    [Arguments("trojan://secret@example.com:443?security=tls&sni=example.com#safe", true)]
    [Arguments("trojan://secret@example.com:443?sni=example.com#no-tls", false)] // 不写 security=tls：sing-box 出站无 TLS，口令明文
    [Arguments("trojan://secret@example.com:443?security=tls&sni=example.com&allowInsecure=1#unsafe", false)]
    [Arguments("trojan://secret@example.com:443?security=tls&sni=example.com&insecure=1#unsafe", false)]
    [Arguments("trojan://secret@127.0.0.1:443?security=tls#local", false)]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=none&type=tcp#plain", false)]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=tls&type=tcp&sni=example.com#tls", true)]
    [Arguments("ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#aead", true)]
    [Arguments("ss://cmM0LW1kNTpwYXNz@1.2.3.4:8388#weak", false)] // rc4-md5：无完整性校验的弱加密
    public async Task PublicPoolRequiresSafeConfiguration(string link, bool allowed)
    {
        var method = typeof(KeeperService).GetMethod("ParsePoolCandidate", BindingFlags.Static | BindingFlags.NonPublic);
        await (method?.Invoke(null, [link]) != null).Should().BeEqualTo(allowed);
    }

    // ---- 安全分级：只看节点自身属性（协议 / 加密 / 传输 / 地址）----
    [Test]
    [Arguments("ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#aead", "中")]
    [Arguments("ss://cmM0LW1kNTpwYXNz@1.2.3.4:8388#weak", "低")]
    [Arguments("ss://MjAyMi1ibGFrZTMtYWVzLTEyOC1nY206cGFzcw==@1.2.3.4:8388#ss2022", "中")]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=tls&type=tcp&sni=example.com#tls", "高")]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=none&type=tcp#plain", "低")]
    [Arguments("trojan://secret@example.com:443?security=tls&sni=example.com#safe", "高")]
    [Arguments("trojan://secret@example.com:443?sni=example.com#no-tls", "低")]
    [Arguments("trojan://secret@example.com:443?security=tls&sni=example.com&allowInsecure=1#unsafe", "低")]
    public async Task EvaluateSecurityClassifiesByObjectiveAttributes(string link, string expectedLevel)
    {
        var profile = FmtHandler.ResolveConfig(link, out _);
        var method = typeof(KeeperService).GetMethod("EvaluateSecurity", BindingFlags.Static | BindingFlags.NonPublic);
        var level = ((ValueTuple<string, string>)method?.Invoke(null, [profile!])!).Item1;
        await level.Should().BeEqualTo(expectedLevel);
    }

    [Test]
    public async Task PoolShortNameUsesOwnerSlashRepoForGithubRaw()
    {
        var method = typeof(KeeperService).GetMethod("PoolShortName", BindingFlags.Static | BindingFlags.NonPublic);
        var shortName = (string)method!.Invoke(null, ["https://raw.githubusercontent.com/free-nodes/v2rayfree/main/sub"])!;
        await ((string)method!.Invoke(null, ["https://example.com/list.txt"])!).Should().BeEqualTo("example.com");
    }
}
