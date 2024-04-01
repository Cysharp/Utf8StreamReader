using BenchmarkDotNet.Attributes;
using Cysharp.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Benchmark;

[SimpleJob, MemoryDiagnoser]
public class StringBenchmark
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
    public async Task<string> StreamReaderReadToEndAsync()
    {
        using var sr = new System.IO.StreamReader(filePath);
        return await sr.ReadToEndAsync();
    }

    [Benchmark]
    public async Task<string> Utf8StreamReaderReadToEndAsync()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath).AsTextReader();
        return await sr.ReadToEndAsync();
    }

    [Benchmark]
    public async Task<string> FileReadAllTextAsync()
    {
        return await File.ReadAllTextAsync(filePath);
    }

}
