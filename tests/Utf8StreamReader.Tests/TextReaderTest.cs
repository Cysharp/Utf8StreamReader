using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utf8StreamReaderTests;

public class TextReaderTest
{
    [Fact]
    public async Task ReadLine()
    {
        var ms = new FakeMemoryStream();
        ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                GetBytes(new string('a', 30000) + "\r\nb"),
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
        );

        var expected = await StreamReaderResultAsync(ms);

        ms.Restart();

        var actual = await Utf8TextReaderResultAsync(ms);

        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task ReadToEnd()
    {
        var ms = new FakeMemoryStream();
        ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                GetBytes(new string('a', 30000) + "\r\nb"),
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
        );

        using var sr = new StreamReader(ms, leaveOpen: true);
        var expected = await sr.ReadToEndAsync();

        ms.Restart();

        using var usr = new Utf8StreamReader(ms).AsTextReader();
        var actual = await usr.ReadToEndAsync();

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task ReadToEndLeftOver()
    {
        var ms = new FakeMemoryStream();

        var hiragana = Encoding.UTF8.GetBytes("あ"); // 3 byte

        ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                new byte[] { hiragana[0] },
                new byte[] { hiragana[1] },
                new byte[] { hiragana[2] },
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
        );

        using var sr = new StreamReader(ms, leaveOpen: true);
        var expected = await sr.ReadToEndAsync();

        ms.Restart();

        using var usr = new Utf8StreamReader(ms).AsTextReader();
        var actual = await usr.ReadToEndAsync();

        actual.Should().Be(expected);
    }

    static async Task<string[]> Utf8TextReaderResultAsync(Stream ms)
    {
        using var reader = new Utf8StreamReader(ms).AsTextReader();
        var l = new List<string>();
        await foreach (var item in reader.ReadAllLinesAsync())
        {
            l.Add(item.ToString());
        }
        return l.ToArray();
    }

    static async Task<string[]> StreamReaderResultAsync(Stream ms)
    {
        var reader = new StreamReader(ms);
        var l = new List<string>();
        string? s;
        while ((s = (await reader.ReadLineAsync())) != null)
        {
            l.Add(s);
        }
        return l.ToArray();
    }
    static string GetString(ReadOnlyMemory<byte> x)
    {
        return Encoding.UTF8.GetString(x.Span);
    }

    static byte[] GetBytes(string x)
    {
        return Encoding.UTF8.GetBytes(x);
    }
}
