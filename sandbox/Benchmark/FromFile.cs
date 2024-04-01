using BenchmarkDotNet.Attributes;
using Cysharp.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

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
    public async Task StreamReaderFileStream()
    {
        using var sr = new System.IO.StreamReader(filePath);
        string? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // ...
        }
    }

    [Benchmark]
    public async Task FileReadLinesAsync()
    {
        await foreach (var line in File.ReadLinesAsync(filePath, Encoding.UTF8))
        {
        }
    }

    [Benchmark]
    public async Task Utf8StreamReaderFileStreamScalability()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Scalability);
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // ...
            }
        }
    }

    [Benchmark]
    public async Task Utf8StreamReaderFileStreamThroughput()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Throughput);
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // ...
            }
        }
    }

    [Benchmark]
    public async ValueTask Utf8StreamReaderFileStreamThroughputSyncRead()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Throughput) { SyncRead = true };
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderFileStreamScalability()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Scalability).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // ...
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderFileStreamThroughput()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Throughput).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // ...
            }
        }
    }

    [Benchmark]
    public async ValueTask Utf8TextReaderFileStreamThroughputSyncRead()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Throughput) { SyncRead = true }.AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // ...
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderToStringFileStreamScalability()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Scalability).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                _ = line.ToString();
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderToStringFileStreamThroughput()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(filePath, fileOpenMode: FileOpenMode.Throughput).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                _ = line.ToString();
            }
        }
    }
}
