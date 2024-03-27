using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Utf8StreamReaderTests;

public class FileReadTest(ITestOutputHelper Console)
{
    [Fact]
    public async Task ReadPath()
    {
        var path1 = Path.Combine(Path.GetDirectoryName(typeof(FileReadTest).Assembly.FullName!)!, "file1.txt");
        var actual = await Utf8StreamReaderResultAsync(path1);

        actual.Should().Equal([
            "abcde",
            "fgh",
            "ijklmnopqrs"
        ]);
    }

    static async Task<string[]> Utf8StreamReaderResultAsync(string path)
    {
        using var reader = new Utf8StreamReader(path);
        var l = new List<string>();
        await foreach (var item in reader.ReadAllLinesAsync())
        {
            l.Add(Encoding.UTF8.GetString(item.Span));
        }
        return l.ToArray();
    }
}
