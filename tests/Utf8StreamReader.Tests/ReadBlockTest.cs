using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utf8StreamReaderTests;

public class ReadBlockTest
{
    [Fact]
    public async Task Foo()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(
            GetBytes("a"),
            GetBytes("bc\n"),
            GetBytes("def\r\n"),
            GetBytes("ghij\n"),
            GetBytes("zklmno\r\n\n"));

        //var sr = new StreamReader(ms);
        //var a = await sr.ReadLineAsync();
        //var buf = new char[1024];
        //await sr.ReadBlockAsync(buf.AsMemory(0, 10));


        var reader = new Utf8StreamReader(ms);
        ToString((await reader.ReadLineAsync()).Value).Should().Be("abc");

        ToString((await reader.ReadBlockAsync(2))).Should().Be("de");

        ToString((await reader.ReadLineAsync()).Value).Should().Be("f");

        ToString((await reader.ReadBlockAsync(8))).Should().Be("\r\nghij\nz");

        ToString((await reader.ReadLineAsync()).Value).Should().Be("klmno");


    }

    static byte[] GetBytes(string x)
    {
        return Encoding.UTF8.GetBytes(x);
    }

    static string ToString(ReadOnlyMemory<byte> buffer)
    {
        return Encoding.UTF8.GetString(buffer.Span);
    }
}
