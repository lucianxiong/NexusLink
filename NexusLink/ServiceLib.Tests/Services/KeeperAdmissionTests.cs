namespace ServiceLib.Tests.Services;

public class KeeperAdmissionTests
{
    [Test]
    [Arguments("trojan://secret@example.com:443?sni=example.com#safe", true)]
    [Arguments("trojan://secret@example.com:443?sni=example.com&allowInsecure=1#unsafe", false)]
    [Arguments("trojan://secret@127.0.0.1:443#local", false)]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=none&type=tcp#plain", false)]
    [Arguments("vless://b1220c56-798a-4af5-b61b-646d4dfe5f4e@example.com:443?security=tls&type=tcp&sni=example.com#tls", true)]
    public async Task PublicPoolRequiresSafeConfiguration(string link, bool allowed)
    {
        var method = typeof(KeeperService).GetMethod("ParsePoolCandidate", BindingFlags.Static | BindingFlags.NonPublic);
        await (method?.Invoke(null, [link]) != null).Should().BeEqualTo(allowed);
    }
}
