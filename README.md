# Utf8StreamReader

[![GitHub Actions](https://github.com/Cysharp/Utf8StreamReader/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/Utf8StreamReader/actions) [![Releases](https://img.shields.io/github/release/Cysharp/Utf8StreamReader.svg)](https://github.com/Cysharp/Utf8StreamReader/releases)
[![NuGet package](https://img.shields.io/nuget/v/Utf8StreamReader.svg)](https://nuget.org/packages/Utf8StreamReader)

Utf8 based StreamReader for high performance text processing.

Avoiding unnecessary string allocation is a fundamental aspect of recent .NET performance improvements. Given that most file and network data is in UTF8, features like [JsonSerializer](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer?view=net-8.0) and [IUtf8SpanParsable](https://learn.microsoft.com/en-us/dotnet/api/system.iutf8spanparsable-1?view=net-8.0), which operate on UTF8-based data, have been added. More recently, methods like [Split](https://learn.microsoft.com/en-us/dotnet/api/system.memoryextensions.split?view=net-8.0), which avoids allocations, have also been introduced.

However, for the most common use case of parsing strings delimited by newlines, only the traditional [StreamReader](https://learn.microsoft.com/en-us/dotnet/api/system.io.streamreader) is provided, which generates a new String for each line, resulting in a large amount of allocations.

![image](https://github.com/Cysharp/Utf8StringInterpolation/assets/46207/ac8d2c7f-65fb-4dc1-b9f5-73219f036e58)
> Read simple 1000000 lines text

Incredibly, there is a **240,000 times** difference!

While it is possible to process data in UTF8 format using standard classes like [PipeReader](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipereader?view=dotnet-plat-ext-8.0) and [SequenceReader](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.sequencereader-1?view=net-8.0), they are generic librardies, so properly handling newline processing requires considerable effort(Handling BOM and Multiple Types of Newline Characters).

`Utf8StreamReader` provides a familiar API similar to StreamReader, making it easy to use, while its ReadLine-specific implementation maximizes performance.

## Getting Started

This library is distributed via NuGet, supporting `.NET Standard 2.1`, `.NET 6(.NET 7)` and `.NET 8` or above.

PM> Install-Package [Utf8StreamReader](https://www.nuget.org/packages/Utf8StreamReader)

The basic API involves `using var streamReader = new Utf8StreamReader(stream);` and then `ReadOnlyMemory<byte> line = await streamReader.ReadLineAsync();`. When enumerating all lines, you can choose from three styles:

```csharp
using Cysharp.IO; // namespace of Utf8StreamReader

public async Task Sample1(Stream stream)
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

The `ReadOnlyMemory<byte>` returned from `ReadLineAsync` o`TryReadLine` or r `TryReadLine` is only valid until the next call to `LoadIntoBufferAsync` or `TryReadLine` or `ReadLineAsync`. Since the data is shared with the internal buffer, it may be overwritten, moved, or returned on the next call, so the safety of the data cannot be guaranteed. The received data must be promptly parsed and converted into a separate object. If you want to keep the data as is, use `ToArray()` to convert it to a `byte[]`.

This design is similar to [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines).

## Optimizing FileStream

Similar to `StreamReader`, `Utf8StreamReader` has the ability to open a `FileStream` by accepting a `string path`.

```csharp
public Utf8StreamReader(string path)
public Utf8StreamReader(string path, int bufferSize)
public Utf8StreamReader(string path, FileStreamOptions options)
public Utf8StreamReader(string path, FileStreamOptions options, int bufferSize)
```

Unfortunately, the `FileStream` used by `StreamReader` is not optimized for modern .NET. For example, when using `FileStream` with asynchronous methods, it should be opened with `useAsync: true` for optimal performance. However, since `StreamReader` has both synchronous and asynchronous methods in its API, false is specified. Additionally, although `StreamReader` itself has a buffer and `FileStream` does not require a buffer, the buffer of `FileStream` is still being utilized.

Strictly speaking, [FileStream underwent a major overhaul in .NET 6](https://github.com/dotnet/runtime/issues/40359). The behavior is controlled by an internal `FileStreamStrategy`. For instance, on Windows, `SyncWindowsFileStreamStrategy` is used when useAsync is false, and `AsyncWindowsFileStreamStrategy` is used when useAsync is true. Moreover, if bufferSize is set to 1, the `FileStreamStrategy` is used directly, and it writes directly to the buffer passed via `ReadAsync(Memory<byte>)`. If any other value is specified, it is wrapped in a `BufferedFileStreamStrategy`.

Based on these observations of the internal behavior, `Utf8StreamReader` generates a `FileStream` with the following options:

```csharp
new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: true)
```

Furthermore, by devising how to call Stream as a whole, we have succeeded in making it function as a thin wrapper for [RandomAccess.ReadAsync](https://learn.microsoft.com/en-us/dotnet/api/system.io.randomaccess.readasync), which is the fastest way to call it.

For overloads that accept `FileStreamOptions`, the above settings are not reflected, so please adjust them manually.

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

## Reset

`Utf8StreamReader` is a class that supports reuse. By calling `Reset()`, the Stream and internal state are released. Using `Reset(Stream)`, it can be reused with a new `Stream`.

## Options

The constructor accepts `int bufferSize` and `bool leaveOpen` as parameters.

`int bufferSize` defaults to 4096, but if the data per line is large, changing the buffer size may improve performance. When the buffer size and the size per line are close, frequent buffer copy operations occur, leading to performance degradation.

`bool leaveOpen` determines whether the internal Stream is also disposed when the object is disposed. The default is `false`, which means the Stream is disposed.

Additionally, there are init properties that allow changing the option values for `ConfigureAwait` and `SkipBom`.

`bool ConfigureAwait { init; }` allows you to specify the value for `ConfigureAwait(bool continueOnCapturedContext)` when awaiting asynchronous methods internally. The default is `false`.

`bool SkipBom { init; }` determines whether to identify and skip the BOM (Byte Order Mark) included at the beginning of the data during the first read. The default is `true`, which means the BOM is skipped.

Currently, this is not an option, but `Utf8StreamReader` only determines `CRLF(\r\n)` or `LF(\n)` as newline characters. Since environments that use `CR(\r)` are now extremely rare, the CR check is omitted for performance reasons. If you need this functionality, please let us know by creating an Issue. We will consider adding it as an option

Unity
---
Unity, which supports .NET Standard 2.1, can run this library. Since the library is only provided through NuGet, it is recommended to use [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) for installation.

For detailed instructions on using NuGet libraries in Unity, please refer to the documentation of [Cysharp/R3](https://github.com/Cysharp/R3/) and other similar resources.

License
---
This library is under the MIT License.