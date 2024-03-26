using System.Linq;
using System.Text;

namespace Utf8StreamReaderTests;

public class UnitTest1(ITestOutputHelper Console)
{
    static MemoryStream CreateStringStream(string input) => new(Encoding.UTF8.GetBytes(input));


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
    public void Foo()
    {

        // TODO: ensure buffer patterns.
        // TODO:memory stream read...
        // TODO: empty string
        // TODO: crlf, lf



    }

}