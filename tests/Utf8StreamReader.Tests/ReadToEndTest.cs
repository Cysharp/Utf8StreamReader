using System.Text;

namespace Utf8StreamReaderTests;

public class ReadToEndTest
{

    [Fact]
    public async Task AfterRead()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var all = await new Utf8StreamReader(ms).ReadToEndAsync();

        ms.Restart();

        var reader = new Utf8StreamReader(ms);
        await reader.ReadLineAsync();

        var expected = "def\r\nghij\nzklmno\r\n\n";

        var actual = await reader.ReadToEndAsync(resultSizeHint: all.Length);

        Encoding.UTF8.GetString(actual).Should().Be(expected);
    }

    [Fact]
    public async Task SmallHint()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var reader = new Utf8StreamReader(ms);

        var expected = "abc\ndef\r\nghij\nzklmno\r\n\n";

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var actual = await reader.ReadToEndAsync(resultSizeHint: Encoding.UTF8.GetByteCount(expected) - 2);
        });
    }

    [Fact]
    public async Task Just()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var reader = new Utf8StreamReader(ms);

        var expected = "abc\ndef\r\nghij\nzklmno\r\n\n";

        var actual = await reader.ReadToEndAsync(resultSizeHint: expected.Length);
        Encoding.UTF8.GetString(actual).Should().Be(expected);
    }

    static byte[] GetBytes(string x)
    {
        return Encoding.UTF8.GetBytes(x);
    }
}
