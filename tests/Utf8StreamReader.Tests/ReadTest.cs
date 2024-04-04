using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utf8StreamReaderTests;

public class ReadTest
{
    // LoadIntoBufferAtLeast

    // ReadToEndChunks

    // ReadToEndAsync


    // ReadToEndAsync + hint

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



    // TryRead
    // ReadAsync




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
