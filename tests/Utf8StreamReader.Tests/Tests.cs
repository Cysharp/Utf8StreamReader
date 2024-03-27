using System.Buffers;
using System.Text;

namespace Utf8StreamReaderTests;

public class Tests(ITestOutputHelper Console)
{
    [Fact]
    public async Task Standard()
    {
        var originalStrings = """
foo
bare

baz boz too
""";

        var stream = CreateStringStream(originalStrings);


        using var reader = new Utf8StreamReader(stream);

        var sb = new StringBuilder();

        bool isFirst = true;
        ReadOnlyMemory<byte>? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (isFirst) isFirst = false;
            else sb.AppendLine();

            Console.WriteLine(Encoding.UTF8.GetString(line.Value.Span));
            sb.Append(Encoding.UTF8.GetString(line.Value.Span));
        }

        sb.ToString().Should().Be(originalStrings.ToString());
    }

    [Fact]
    public async Task BOM()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat("""
foo
bare

baz boz too
"""u8.ToArray()).ToArray();

        var bomStrings = Encoding.UTF8.GetString(bytes);

        var stream = CreateStringStream(bomStrings);

        var originalStrings = """
foo
bare

baz boz too
""";

        using var reader = new Utf8StreamReader(stream);

        var sb = new StringBuilder();

        bool isFirst = true;
        ReadOnlyMemory<byte>? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (isFirst) isFirst = false;
            else sb.AppendLine();

            Console.WriteLine(Encoding.UTF8.GetString(line.Value.Span));
            sb.Append(Encoding.UTF8.GetString(line.Value.Span));
        }

        sb.ToString().Should().Be(originalStrings.ToString());
    }

    [Fact]
    public async Task NewLineCheck()
    {
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes("a"),
                GetBytes("bc\n"),
                GetBytes("def\r\n"),
                GetBytes("ghij\n"),
                GetBytes("jklmno"));

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes("a"),
                GetBytes("bc\n"),
                GetBytes("def\r\n"),
                GetBytes("ghij\n"),
                GetBytes("jklmno\r\n")); // + last new line

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes("a"),
                GetBytes("bc\n"),
                GetBytes("def\r\n"),
                GetBytes("ghij\n"),
                GetBytes("jklmno\r\n\n")); // + last new line x2

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
    }

    [Fact]
    public async Task BOM2()
    {
        {
            var ms = new FakeMemoryStream();

            // small bom
            ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                GetBytes("a"));

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        {
            var ms = new FakeMemoryStream();

            // long bom
            ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                GetBytes("abcdefghijklmnopqrastu"));

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }

        // yes bom
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                Encoding.UTF8.GetPreamble(),
                GetBytes("あいうえお")); // japanese hiragana.

            var reader = new Utf8StreamReader(ms) { SkipBom = false };
            var line = await reader.ReadLineAsync();
            line.Value.Slice(0, 3).Span.SequenceEqual(Encoding.UTF8.Preamble).Should().BeTrue();
            line.Value.Slice(3).Span.SequenceEqual(GetBytes("あいうえお")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task EmptyString()
    {
        {
            var ms = new MemoryStream();

            var expected = await StreamReaderResultAsync(ms);

            ms = new MemoryStream();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        // bom only
        {
            var ms = new FakeMemoryStream();

            // small bom
            ms.AddMemory(Encoding.UTF8.GetPreamble());

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        // newline only
        {
            var ms = new FakeMemoryStream();

            // small bom
            ms.AddMemory(GetBytes("\r\n"));

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
        // newline only 2
        {
            var ms = new FakeMemoryStream();

            // small bom
            ms.AddMemory(GetBytes("\n\r\n"));

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms);

            actual.Should().Equal(expected);
        }
    }

    [Fact]
    public async Task SmallString()
    {
        var ms = new FakeMemoryStream();

        ms.AddMemory(GetBytes("z"));

        var expected = await StreamReaderResultAsync(ms);

        ms.Restart();

        var actual = await Utf8StreamReaderResultAsync(ms);

        actual.Should().Equal(expected);
    }

    [Fact]
    public async Task SliceAndResize()
    {
        var bufferSize = 256;

        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes("!!!\r\n"), // first line consume
                GetBytes(new string('a', 245)),
                GetBytes("bcdefghijklmnopqrstuvwxyz\r\n"),
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
            );

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms, bufferSize);

            actual[1].Should().Be(expected[1]);
            actual.Should().Equal(expected);
        }
        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes("!!!\r\n"), // first line consume
                GetBytes(new string('a', 252)),
                GetBytes("bcdefghijklmnopqrstuvwxyz\r\n"),
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
            );

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms, bufferSize);

            actual[1].Should().Be(expected[1]);
            actual.Should().Equal(expected);
        }
    }

    [Fact]
    public async Task OnlySlice()
    {
        var bufferSize = 256;

        {
            var ms = new FakeMemoryStream();

            ms.AddMemory(
                GetBytes(new string('a', 245)),
                GetBytes("\r\n"),
                GetBytes("あいうえおかきくけこ\n"),
                GetBytes("ABCDEFGHIJKLMN")
            );

            var expected = await StreamReaderResultAsync(ms);

            ms.Restart();

            var actual = await Utf8StreamReaderResultAsync(ms, bufferSize);

            actual[1].Should().Be(expected[1]);
            actual.Should().Equal(expected);
        }
    }

    static async Task<string[]> Utf8StreamReaderResultAsync(Stream ms, int? size = null)
    {
        var reader = (size == null) ? new Utf8StreamReader(ms) : new Utf8StreamReader(ms, size.Value);
        var l = new List<string>();
        await foreach (var item in reader.ReadAllLinesAsync())
        {
            l.Add(GetString(item));
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

    static MemoryStream CreateStringStream(string input) => new(Encoding.UTF8.GetBytes(input));
}
