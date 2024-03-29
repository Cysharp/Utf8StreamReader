using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BenchmarkDotNet.Attributes;
using Cysharp.IO;

namespace Benchmark;

[SimpleJob, MemoryDiagnoser]
public class FromFile
{
    const int C = 1000000;

    string filePath = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        var path = Path.GetTempFileName();
        var newline = OperatingSystem.IsWindows() ? "\r\n"u8 : "\n"u8;
        using var file = File.OpenWrite(path);
        for (var i = 0; i < C; i++)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(
                new MyClass { MyProperty = i, MyProperty2 = "あいうえおかきくけこ" }, options);
            file.Write(json);
            file.Write(newline);
        }

        filePath = path;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        File.Delete(filePath);
    }

    [Benchmark]
    public async Task StreamReader()
    {
        using var fs = File.OpenRead(filePath);
        using var sr = new System.IO.StreamReader(fs);
        string? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // Console.WriteLine(line);
        }
    }

    [Benchmark]
    public async Task Utf8StreamReader()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath);
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // Console.WriteLine(Encoding.UTF8.GetString( line.Span));
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReader()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // Console.WriteLine(line);
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderToString()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // Console.WriteLine(line);
            }
        }
    }
}
