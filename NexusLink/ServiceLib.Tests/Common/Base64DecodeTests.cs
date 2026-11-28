namespace ServiceLib.Tests.Common;

/// <summary>
/// 订阅解码：坏行静默跳过、明文不误判、URL-safe Base64 正常解。
/// </summary>
public class Base64DecodeTests
{
    [Test]
    public async Task DecodesStandardAndUrlSafeBase64()
    {
        await Utils.Base64Decode("aGVsbG8=").Should().BeEqualTo("hello");
        await Utils.Base64Decode("aGVsbG8").Should().BeEqualTo("hello");
        await Utils.Base64Decode("aGVsbG8_").Should().BeEqualTo("hello?");
    }

    [Test]
    public async Task PlainTextPassesThrough()
    {
        var link = "ss://YWVzLTI1Ni1nY206cGFzcw==@1.2.3.4:8388#US-01";
        await Utils.Base64Decode(link).Should().BeEqualTo(link);
        await Utils.Base64Decode("{\"a\":1}").Should().BeEqualTo("{\"a\":1}");
    }

    [Test]
    public async Task InvalidInputReturnsEmptyWithoutThrowing()
    {
        await Utils.Base64Decode("!!!!").Should().BeEqualTo(string.Empty);
        await Utils.Base64Decode(null).Should().BeEqualTo(string.Empty);
        await Utils.Base64Decode("").Should().BeEqualTo(string.Empty);
    }
}
