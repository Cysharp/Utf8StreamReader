
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using BenchmarkDotNet.Attributes;
using Cysharp.IO;

namespace Benchmark;

[SimpleJob, MemoryDiagnoser]
public class FromMemory
{
    const int C = 1000000;
    // const int C = 100;

    byte[] utf8Data = default!;
    MemoryStream ms = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        var jsonLines = Enumerable.Range(0, C)
            .Select(x => new MyClass { MyProperty = x, MyProperty2 = "あいうえおかきくけこ" })
            .Select(x => JsonSerializer.Serialize(x, options))
            .ToArray();

        utf8Data = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, jsonLines));
    }

    [IterationSetup]
    public void Setup()
    {
        ms = new MemoryStream(utf8Data);
    }

    [Benchmark]
    public async Task StreamReader()
    {
        using var sr = new System.IO.StreamReader(ms);
        string? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // Console.WriteLine(line);
        }
    }

    [Benchmark]
    public async Task Utf8StreamReader()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(ms);
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
        using var sr = new Cysharp.IO.Utf8StreamReader(ms).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                // Console.WriteLine(Encoding.UTF8.GetString( line.Span));
            }
        }
    }

    [Benchmark]
    public async Task Utf8TextReaderToString()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(ms).AsTextReader();
        while (await sr.LoadIntoBufferAsync())
        {
            while (sr.TryReadLine(out var line))
            {
                _ = line.ToString();
                // Console.WriteLine(Encoding.UTF8.GetString( line.Span));
            }
        }
    }

    //[Benchmark]
    //public async Task Utf8StreamReaderReadLine()
    //{
    //    using var sr = new Cysharp.IO.Utf8StreamReader(ms);
    //    ReadOnlyMemory<byte>? line;
    //    while ((line = await sr.ReadLineAsync()) != null)
    //    {
    //        // Console.WriteLine(Encoding.UTF8.GetString(line.Value.Span));
    //    }
    //}

    //[Benchmark]
    //public async Task Utf8StreamReaderReadAllLines()
    //{
    //    using var sr = new Cysharp.IO.Utf8StreamReader(ms);
    //    await foreach (var line in sr.ReadAllLinesAsync())
    //    {
    //        //Console.WriteLine(Encoding.UTF8.GetString(line.Span));
    //    }
    //}

    [Benchmark]
    public async Task PipeReaderSequenceReader()
    {
        using (ms)
        {
            var reader = PipeReader.Create(ms);

        READ_AGAIN:
            var readResult = await reader.ReadAsync();

            if (!(readResult.IsCompleted | readResult.IsCanceled))
            {
                var buffer = readResult.Buffer;

                while (TryReadData(ref buffer, out var line))
                {
                    //Console.WriteLine(Encoding.UTF8.GetString(line));
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                goto READ_AGAIN;
            }

        }

        static bool TryReadData(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            var reader = new SequenceReader<byte>(buffer);
            if (reader.TryReadTo(out line, (byte)'\n', advancePastDelimiter: true))
            {
                buffer = buffer.Slice(reader.Consumed);
                return true;
            }
            return false;
        }
    }

    //[Benchmark]
    //public async Task PipelineStreamReader2()
    //{
    //    using (ms)
    //    {
    //        var reader = PipeReader.Create(ms);

    //    READ_AGAIN:
    //        var readResult = await reader.ReadAsync();

    //        if (!(readResult.IsCompleted | readResult.IsCanceled))
    //        {
    //            var buffer = readResult.Buffer;
    //            ConsumeAllData(ref buffer);
    //            reader.AdvanceTo(buffer.Start, buffer.End);
    //            goto READ_AGAIN;
    //        }
    //    }

    //    static void ConsumeAllData(ref ReadOnlySequence<byte> buffer)
    //    {
    //        var reader = new SequenceReader<byte>(buffer);
    //        while (reader.TryReadTo(out ReadOnlySequence<byte> line, (byte)'\n', advancePastDelimiter: true))
    //        {
    //            //Console.WriteLine(Encoding.UTF8.GetString(line));
    //        }
    //        buffer = buffer.Slice(reader.Consumed);
    //    }
    //}
}


public class MyClass
{
    public int MyProperty { get; set; }
    public string? MyProperty2 { get; set; }
}
