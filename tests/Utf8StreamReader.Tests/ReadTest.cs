using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utf8StreamReaderTests;

public class ReadTest
{
    [Fact]
    public async Task ReadToEndAsync()
    {
        // with bom
        {
            var bom = Encoding.UTF8.GetPreamble();

            var ms = new FakeMemoryStream();

            ms.AddMemory(
                new byte[] { bom[0] },
                new byte[] { bom[1] },
                new byte[] { bom[2], (byte)'Z' },
                GetBytes("a"),
                GetBytes("bc\n"),
                GetBytes("def\r\n"),
                GetBytes("ghij\n"),
                GetBytes("zklmno\r\n\n"));

            var sr = new Utf8StreamReader(ms);
            var result = await sr.ReadToEndAsync(disableBomCheck: false);

            var expected = "Zabc\ndef\r\nghij\nzklmno\r\n\n";
            var actual = ToString(result);

            actual.Should().Be(expected);
        }
        // no bom
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                new byte[] { (byte)'Z' },
                GetBytes("a"),
                GetBytes("bc\n"),
                GetBytes("def\r\n"),
                GetBytes("ghij\n"),
                GetBytes("zklmno\r\n\n"));

            var sr = new Utf8StreamReader(ms);
            var result = await sr.ReadToEndAsync();

            var expected = "Zabc\ndef\r\nghij\nzklmno\r\n\n";
            var actual = ToString(result);

            actual.Should().Be(expected);
        }
    }

    [Fact]
    public async Task ReadToEndChunks()
    {
        var bom = Encoding.UTF8.GetPreamble();

        var ms = new FakeMemoryStream();

        ms.AddMemory(
            new byte[] { bom[0] },
            new byte[] { bom[1] },
            new byte[] { bom[2], (byte)'Z' },
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var sr = new Utf8StreamReader(ms);

        var list = new List<byte[]>();
        await foreach (var item in sr.ReadToEndChunksAsync())
        {
            list.Add(item.ToArray());
        }

        ToString(list[0]).Should().Be("Z");
        ToString(list[1]).Should().Be("a");
        ToString(list[2]).Should().Be("bc\n");
        ToString(list[3]).Should().Be("def\r\n");
        ToString(list[4]).Should().Be("ghij\n");
        ToString(list[5]).Should().Be("zklmno\r\n\n");
    }

    [Fact]
    public async Task TestPeek()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var sr = new Utf8StreamReader(ms);

        sr.TryPeek(out var data).Should().BeFalse();
        (await sr.PeekAsync()).Should().Be((byte)'a');
        sr.TryPeek(out data).Should().BeTrue();
        data.Should().Be((byte)'a');

        ToString(await sr.ReadLineAsync()).Should().Be("abc");

        (await sr.PeekAsync()).Should().Be((byte)'d');

        ToString(await sr.ReadLineAsync()).Should().Be("def");
    }

    // LoadIntoBufferAtLeastAsync
    // TryRead
    // ReadAsync

    [Fact]
    public async Task TestRead()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        var sr = new Utf8StreamReader(ms);

        await sr.LoadIntoBufferAtLeastAsync(2);

        sr.TryRead(out var a).Should().BeTrue();
        a.Should().Be((byte)'a');

        sr.TryRead(out var b).Should().BeTrue();
        b.Should().Be((byte)'b');

        sr.TryRead(out var c).Should().BeTrue();
        c.Should().Be((byte)'c');

        sr.TryRead(out var n).Should().BeTrue();
        n.Should().Be((byte)'\n');

        sr.TryRead(out _).Should().BeFalse();

        (await sr.ReadAsync()).Should().Be((byte)'d');
    }

    static byte[] GetBytes(string x)
    {
        return Encoding.UTF8.GetBytes(x);
    }

    static string ToString(ReadOnlyMemory<byte>? buffer)
    {
        if (buffer == null) return null!;
        return Encoding.UTF8.GetString(buffer.Value.Span);
    }
}
