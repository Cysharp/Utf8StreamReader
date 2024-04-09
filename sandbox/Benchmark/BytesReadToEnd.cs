using BenchmarkDotNet.Attributes;
using Cysharp.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Benchmark;

[SimpleJob, MemoryDiagnoser]
public class BytesReadToEnd
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
    public async Task<byte[]> FileReadAllBytesAsync()
    {
        // ReadAllBytes knows file-length so fastest.
        return await File.ReadAllBytesAsync(filePath);
    }

    [Benchmark]
    public async Task<byte[]> Utf8StreamReaderReadToEndAsync()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath);
        return await sr.ReadToEndAsync();
    }
}

[SimpleJob, MemoryDiagnoser]
public class BytesReadToEnd2
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
    public async Task<byte[]> MemoryStreamCopyToToArray()
    {
        using var fs = new FileStream(filePath, FileMode.Open);
        var ms = new MemoryStream();
        await fs.CopyToAsync(ms);

        return ms.ToArray();
    }

    [Benchmark]
    public async Task<byte[]> Utf8StreamReaderReadToEndAsync()
    {
        using var fs = new FileStream(filePath, FileMode.Open);
        using var sr = new Cysharp.IO.Utf8StreamReader(fs);
        return await sr.ReadToEndAsync(disableBomCheck: false); // hack for ignore optimize(for benchmark fairness)
    }
}
