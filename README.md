# Utf8StreamReader

[![GitHub Actions](https://github.com/Cysharp/Utf8StreamReader/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/Utf8StreamReader/actions) [![Releases](https://img.shields.io/github/release/Cysharp/Utf8StreamReader.svg)](https://github.com/Cysharp/Utf8StreamReader/releases)
[![NuGet package](https://img.shields.io/nuget/v/Utf8StreamReader.svg)](https://nuget.org/packages/Utf8StreamReader)

Utf8 based StreamReader for high performance text processing.

Avoiding unnecessary string allocation is a fundamental aspect of recent .NET performance improvements. Given that most file and network data is in UTF8, features like [JsonSerializer](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer?view=net-8.0) and [IUtf8SpanParsable](https://learn.microsoft.com/en-us/dotnet/api/system.iutf8spanparsable-1?view=net-8.0), which operate on UTF8-based data, have been added. More recently, methods like [.NET8 MemoryExtensions.Split](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.split?view=net-8.0), which avoids allocations, have also been introduced.

However, for the most common use case of parsing strings delimited by newlines, only the traditional [StreamReader](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader) is provided, which generates a new String for each line, resulting in a large amount of allocations.

![image](https://github.com/Cysharp/Utf8StringInterpolation/assets/46207/ac8d2c7f-65fb-4dc1-b9f5-73219f036e58)
> Read simple 1000000 lines text

Incredibly, there is a **240,000 times** difference!

While it is possible to process data in UTF8 format using standard classes like [PipeReader](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipereader?view=dotnet-plat-ext-8.0) and [SequenceReader](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.sequencereader-1?view=net-8.0), they are generic librardies, so properly handling newline processing requires considerable effort(Handling BOM and Multiple Types of Newline Characters).

`Utf8StreamReader` provides a familiar API similar to StreamReader, making it easy to use, while its ReadLine-specific implementation maximizes performance.

By using optimized internal processing, higher performance can be achieved when reading Strings from Files compared to using the standard `StreamReader.ReadToEnd` or `File.ReadAllText` methods.

![image](https://github.com/Cysharp/Utf8StreamReader/assets/46207/f2dc965a-768a-4069-a3e3-387f5279421a)

> Read from file(1000000 lines text)

```csharp
[Benchmark]
public async Task<string> StreamReaderReadToEndAsync()
{
    using var sr = new System.IO.StreamReader(filePath);
    return await sr.ReadToEndAsync();
}

[Benchmark]
public async Task<string> Utf8TextReaderReadToEndAsync()
{
    using var sr = new Cysharp.IO.Utf8StreamReader(filePath).AsTextReader();
    return await sr.ReadToEndAsync();
}

[Benchmark]
public async Task<string> FileReadAllTextAsync()
{
    return await File.ReadAllTextAsync(filePath);
}
```

For an explanation of the performance difference, please refer to the [ReadString Section](#readstring).

## Getting Started

This library is distributed via NuGet, supporting `.NET Standard 2.1`, `.NET 6(.NET 7)` and `.NET 8` or above. For information on usage with Unity, please refer to the [Unity Section](#unity).

PM> Install-Package [Utf8StreamReader](https://www.nuget.org/packages/Utf8StreamReader)

The basic API involves `using var streamReader = new Utf8StreamReader(stream);` and then `ReadOnlyMemory<byte> line = await streamReader.ReadLineAsync();`. When enumerating all lines, you can choose from three styles:

```csharp
using Cysharp.IO; // namespace of Utf8StreamReader

public async Task Sample1(Stream stream)


![image](https://github.com/Cysharp/Utf8StreamReader/assets/46207/df82bb8c-00bf-4159-b21d-83706691ccd3)

{
    using var reader = new Utf8StreamReader(stream);

    // Most performant style, similar as System.Threading.Channels
    while (await reader.LoadIntoBufferAsync())
    {
        while (reader.TryReadLine(out var line))
        {
            // line is ReadOnlyMemory<byte>, deserialize UTF8 directly.
            _ = JsonSerializer.Deserialize<Foo>(line.Span);
        }
    }
}

public async Task Sample2(Stream stream)
{
    using var reader = new Utf8StreamReader(stream);

    // Classical style, same as StreamReader
    ReadOnlyMemory<byte>? line = null;
    while ((line = await reader.ReadLineAsync()) != null)
    {
        _ = JsonSerializer.Deserialize<Foo>(line.Value.Span);
    }
}

public async Task Sample3(Stream stream)
{
    using var reader = new Utf8StreamReader(stream);

    // Most easiest style, use async streams
    await foreach (var line in reader.ReadAllLinesAsync())
    {
        _ = JsonSerializer.Deserialize<Foo>(line.Span);
    }
}
```

From a performance perspective, `Utf8StreamReader` only provides asynchronous APIs.

Theoretically, the highest performance can be achieved by combining `LoadIntoBufferAsync` and `TryReadLine` in a double while loop. This is similar to the combination of `WaitToReadAsync` and `TryRead` in [Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).

`ReadLineAsync`, like StreamReader.ReadLine, returns null to indicate that the end has been reached.

`ReadAllLinesAsync` returns an `IAsyncEnumerable<ReadOnlyMemory<byte>>`. Although there is a performance difference, it is minimal, so this API is ideal when you want to use it easily.

All asynchronous methods accept a `CancellationToken` and support cancellation.

For a real-world usage example, refer to [StreamMessageReader.cs](https://github.com/Cysharp/Claudia/blob/main/src/Claudia/StreamMessageReader.cs) in [Cysharp/Claudia](https://github.com/Cysharp/Claudia/), a C# SDK for Anthropic Claude, which parses [server-sent events](https://developer.mozilla.org/en-US/docs/Web/API/Server-sent_events/Using_server-sent_events).

## Buffer Lifetimes

The `ReadOnlyMemory<byte>` returned from `ReadLineAsync` or `TryReadLine` is only valid until the next call to `LoadIntoBufferAsync` or `TryReadLine` or `ReadLineAsync`. Since the data is shared with the internal buffer, it may be overwritten, moved, or returned on the next call, so the safety of the data cannot be guaranteed. The received data must be promptly parsed and converted into a separate object. If you want to keep the data as is, use `ToArray()` to convert it to a `byte[]`.

This design is similar to [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines).

## Read as `ReadOnlyMemory<char>`

You can convert it to a `Utf8TextReader` that extracts `ReadOnlyMemory<char>` or `string`. Although there is a conversion cost, it is still fast and low allocation, so it can be used as an alternative to `StreamReader`.

![image](https://github.com/Cysharp/Utf8StreamReader/assets/46207/d77af0fd-76af-46ce-8261-0863e4ab7109)

After converting with `AsTextReader()`, all the same methods (`TryReadLine`, `ReadLineAsync`, `LoadIntoBufferAsync`, `ReadAllLinexAsync`) can be used.

```csharp
using var sr = new Cysharp.IO.Utf8StreamReader(ms).AsTextReader();
while (await sr.LoadIntoBufferAsync())
{
    while (sr.TryReadLine(out var line))
    {
        // line is ReadOnlyMemory<char>, you can add to StringBuilder or other parsing method.

        // If you neeed string, ReadOnlyMemory<char>.ToString() build string instance
        // string str = line.ToString();
    }
}
```

You can perform text processing without allocation, such as splitting `ReadOnlySpan<char>` using [MemoryExtensions.Split](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.split?view=net-8.0#system-memoryextensions-split(system-readonlyspan((system-char))-system-span((system-range))-system-char-system-stringsplitoptions)), and concatenate the results using StringBuilder's [`Append/AppendLine(ReadOnlySpan<char>)`](https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.append). This way, string-based processing can be done with much lower allocation compared to `StreamReader`.

When a string is needed, you can convert `ReadOnlyMemory<char>` to a string using `ToString()`. Even with the added string conversion, the performance is higher than `StreamReader`, so it can be used as a better alternative.

## Optimizing FileStream

Similar to `StreamReader`, `Utf8StreamReader` has the ability to open a `FileStream` by accepting a `string path`.

```csharp
public Utf8StreamReader(string path, FileOpenMode fileOpenMode = FileOpenMode.Throughput)
public Utf8StreamReader(string path, int bufferSize, FileOpenMode fileOpenMode = FileOpenMode.Throughput)
public Utf8StreamReader(string path, FileStreamOptions options)
public Utf8StreamReader(string path, FileStreamOptions options, int bufferSize)
```

Unfortunately, the `FileStream` used by `StreamReader` is not optimized for modern .NET. For example, when using `FileStream` with asynchronous methods, it should be opened with `useAsync: true` for optimal performance. However, since `StreamReader` has both synchronous and asynchronous methods in its API, false is specified. Additionally, although `StreamReader` itself has a buffer and `FileStream` does not require a buffer, the buffer of `FileStream` is still being utilized.

It is difficult to handle `FileStream` correctly with high performance. By specifying a `string path`, the stream is opened with options optimized for `Utf8StreamReader`, so it is recommended to use this overload rather than opening `FileStream` yourself. The following is a benchmark of `FileStream`.

![image](https://github.com/Cysharp/Utf8StreamReader/assets/46207/83936827-2380-414a-9778-f53252689eb7)

`Utf8StreamReader` opens `FileStream` with the following settings:

```csharp
var useAsync = (fileOpenMode == FileOpenMode.Scalability);
new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: useAsync)
```

Due to historical reasons, the options for `FileStream` are odd, but by setting `bufferSize` to 1, you can avoid the use of internal buffers. `FileStream` has been significantly revamped in .NET 6, and by controlling the setting of this option and the way `Utf8StreamReader` is called as a whole, it can function as a thin wrapper around the fast [RandomAccess.ReadAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.readasync), allowing you to avoid most of the overhead of FileStream.

`FileOpenMode` is a proprietary option of `Utf8StreamReader`.


```csharp
public enum FileOpenMode
{
    Scalability,
    Throughput
}
```

In a Windows environment, the table in the [IO section of the Performance Improvements in .NET 6 blog](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-6/#io) shows that throughput decreases when `useAsync: true` is used.

| Method | Runtime | IsAsync | BufferSize | Mean |
| -      | -       | -       | -          | -    |
| ReadAsync	| .NET 6.0 | True  | 1 | 119.573 ms |
| ReadAsync	| .NET 6.0 | False | 1 | 36.018 ms  |

By setting `Utf8StreamReader` to `FileOpenMode.Scalability`, true async I/O is enabled and scalability is prioritized. If set to `FileOpenMode.Throughput`, it internally becomes sync-over-async and consumes the ThreadPool, but reduces the overhead of asynchronous I/O and improves throughput.

If frequently executed within a server application, setting it to `Scalability`, and for batch applications, setting it to `Throughput` will likely yield the best performance characteristics. The default is `Throughput`. (In the current .NET implementation, both seem to be the same (similar to Throughput on Windows) in Linux environments.)

In `Utf8StreamReader`, by carefully adjusting the buffer size on the `Utf8StreamReader` side, the performance difference is minimized. Please refer to the above benchmark results image for specific values.

For overloads that accept `FileStreamOptions`, the above settings are not reflected, so please adjust them manually.

## ReadString

By combining the above FileStream optimization with `.AsTextReader().ReadToEndAsync()`, you can achieve higher performance when reading out a `string` compared to `StreamReader.ReadToEnd` or `File.ReadAllText`.

![image](https://github.com/Cysharp/Utf8StreamReader/assets/46207/f2dc965a-768a-4069-a3e3-387f5279421a)

The implementation of `File.ReadAllText` in dotnet/runtime uses `StreamReader.ReadToEnd`, so they are almost the same. However, in the case of `File.ReadAllText`, it uses `useAsync: true` when opening the `FileStream`. That accounts for the performance difference in the benchmark.

Another significant difference in the implementation is that `Utf8StreamReader` generates a `string` without using `StringBuilder`. `StreamReader.ReadToEnd` generates a string using the following flow: `byte[] buffer` -> `char[] decodeBuffer` -> `StringBuilder.Append(char[])` -> `StringBuilder.ToString()`, but there are removable inefficiencies. Both `char[]` and `StringBuilder` are `char[]` buffers, and copying occurs. By generating a `string` directly from `char[]`, the copy to the internal buffer of `StringBuilder` can be eliminated.

In `Utf8StreamReader`'s `.AsTextReader().ReadToEndAsync()`, it receives streaming data in read buffer units from `Utf8StreamReader` (`ReadToEndChunksAsync`), converts it to `char[]` chunks using `Decoder`, and generates the string all at once using `string.Create`.

```csharp
// Utf8TextReader is a helper class for ReadOnlyMemory<char> and string generation that internally holds Utf8StreamReader
public async ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken = default)
{
    // Using a method similar to .NET 9 LINQ to Objects's ToArray improvement, returns a structure optimized for gap-free sequential expansion
    // StreamReader.ReadToEnd copies the buffer to a StringBuilder, but this implementation holds char[] chunks(char[][]) without copying.
    using var writer = new SegmentedArrayBufferWriter<char>();
    var decoder = Encoding.UTF8.GetDecoder();

    // Utf8StreamReader.ReadToEndChunksAsync returns the internal buffer ReadOnlyMemory<byte> as an asynchronous sequence upon each read completion
    await foreach (var chunk in reader.ReadToEndChunksAsync(cancellationToken).ConfigureAwait(reader.ConfigureAwait))
    {
        var input = chunk;
        while (input.Length != 0)
        {
            // The Decoder directly writes from the read buffer to the char[] buffer
            decoder.Convert(input.Span, writer.GetMemory().Span, flush: false, out var bytesUsed, out var charsUsed, out var completed);
            input = input.Slice(bytesUsed);
            writer.Advance(charsUsed);
        }
    }
    
    decoder.Convert([], writer.GetMemory().Span, flush: true, out _, out var finalCharsUsed, out _);
    writer.Advance(finalCharsUsed);

    // Directly generate a string from the char[][] buffer using String.Create
    return string.Create(writer.WrittenCount, writer, static (stringSpan, writer) =>
    {
        foreach (var item in writer.GetSegmentsAndDispose())
        {
            item.Span.CopyTo(stringSpan);
            stringSpan = stringSpan.Slice(item.Length);
        }
    });
}
```

SegmentedArrayBufferWriter borrows the idea (which I proposed) from [the performance improvement of ToArray in LINQ in .NET 9](https://github.com/dotnet/runtime/pull/96570), and internally holds an InlineArray that expands by equal multipliers.

```csharp
[StructLayout(LayoutKind.Sequential)]
struct InlineArray19<T>
{
    public const int InitialSize = 8192;
    T[] array00;  // 8192
    T[] array01;  // 16384
    T[] array02;  // 32768
    T[] array03;  // 65536
    T[] array04;  // 131072
    T[] array05;  // 262144
    T[] array06;  // 524288
    T[] array07;  // 1048576
    T[] array08;  // 2097152
    T[] array09;  // 4194304
    T[] array10;  // 8388608
    T[] array11;  // 16777216
    T[] array12;  // 33554432
    T[] array13;  // 67108864
    T[] array14;  // 134217728
    T[] array15;  // 268435456
    T[] array16;  // 536870912
    T[] array17;  // 1073741824
    T[] array18;  // Array.MaxLength - total

    public T[] this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (i < 0 || i > 18) Throw();
            return Unsafe.Add(ref array00, i);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (i < 0 || i > 18) Throw();
            Unsafe.Add(ref array00, i) = value;
        }
    }
    void Throw() { throw new ArgumentOutOfRangeException(); }
}
```

With these optimizations for both reading and writing, we achieved several times the speedup compared to the .NET standard library.

## Binary Read

`TryPeek`, `PeekAsync`, `TryRead`, `ReadAsync`, `TryReadBlock`, and `ReadBlockAsync` enable reading as binary, irrespective of newline codes. For example, Redis's protocol, RESP, is a text protocol and typically newline-delimited, but after `$N`, it requires reading N bytes (BulkString). For instance, `$5\r\nhello\r\n` means reading 5 bytes.

Here's an example of how it can be parsed:

```csharp
// $5\r\nhello\r\n
var line = await reader.ReadLineAsync(); // $5(+ consumed \r\n)
if (line.Value.Span[0] == (byte)'$')
{
    Utf8Parser.TryParse(line.Value.Span.Slice(1), out int size, out _); // 5
    var block = await reader.ReadBlockAsync(size); // hello
    await reader.ReadLineAsync(); // consume \r\n
    Console.WriteLine(Encoding.UTF8.GetString(block.Span));
}
```

A sample that parses all RESP code is available in [RespReader.cs](https://github.com/Cysharp/Utf8StreamReader/blob/fb8a02e1/sandbox/ConsoleApp1/RespReader.cs).

Additionally, when using `LoadIntoBufferAsync` and `LoadIntoBufferAtLeastAsync` to include data in the buffer, using `Try***` allows for more efficient execution.

```csharp
while (await reader.LoadIntoBufferAsync())
{
    while (reader.TryReadLine(out var line))
    {
        switch (line.Span[0])
        {
            case (byte)'$':
                Utf8Parser.TryParse(line.Span.Slice(1), out int size, out _);
                if (!reader.TryReadBlock(size + 2, out var block)) // +2 is \r\n
                {
                    // ReadBlockAsync is TryReadBlock + LoadIntoBufferAtLeastAsync
                    block = await reader.ReadBlockAsync(size + 2);
                }
                yield return block.Slice(0, size);
                break;
            // and others('+', '-', ':', '*')
            default:
                break;
        }
    }
}
```

When using `ReadToEndAsync`, you can obtain a `byte[]` using Utf8StreamReader's efficient binary reading/concatenation (`SegmentedArrayBufferWriter<byte>, InlineArray19<byte>`). However, it's important to note that by default, it checks for and trims the BOM (Byte Order Mark). If you expect a complete binary read, set `SkipBom = false` to disable the BOM check.

```csharp
using var reader = new Utf8StreamReader(stream) { SkipBom = false };
byte[] bytes = await reader.ReadToEndAsync();
```

## Reset

`Utf8StreamReader` is a class that supports reuse. By calling `Reset()`, the Stream and internal state are released. Using `Reset(Stream)`, it can be reused with a new `Stream`.

## Options

The constructor accepts `int bufferSize` and `bool leaveOpen` as parameters.

`int bufferSize` defaults to 65536 and the buffer is rented from `ArrayPool<byte>`. If the data per line is large, changing the buffer size may improve performance. When the buffer size and the size per line are close, frequent buffer copy operations occur, leading to performance degradation.

`bool leaveOpen` determines whether the internal Stream is also disposed when the object is disposed. The default is `false`, which means the Stream is disposed.

Additionally, there are init properties that allow changing the option values for `ConfigureAwait`, `SyncRead` and `SkipBom`.

`bool ConfigureAwait { init; }` allows you to specify the value for `ConfigureAwait(bool continueOnCapturedContext)` when awaiting asynchronous methods internally. The default is `false`.

`bool SyncRead { init; }` configures the Stream to use synchronous reading, meaning it will use Read instead. This causes all Async operations to complete synchronously. There is potential for slight performance improvements when a `FileStream` is opened with `useAsync:false`. Normally, leaving it as false is fine. The default is `false`.

`bool SkipBom { init; }` determines whether to identify and skip the BOM (Byte Order Mark) included at the beginning of the data during the first read. The default is `true`, which means the BOM is skipped.

Currently, this is not an option, but `Utf8StreamReader` only determines `CRLF(\r\n)` or `LF(\n)` as newline characters. Since environments that use `CR(\r)` are now extremely rare, the CR check is omitted for performance reasons. If you need this functionality, please let us know by creating an Issue. We will consider adding it as an option

Unity
---
Unity, which supports .NET Standard 2.1, can run this library. Since the library is only provided through NuGet, it is recommended to use [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) for installation.

For detailed instructions on using NuGet libraries in Unity, please refer to the documentation of [Cysharp/R3](https://github.com/Cysharp/R3/) and other similar resources.

License
---
This library is under the MIT License.