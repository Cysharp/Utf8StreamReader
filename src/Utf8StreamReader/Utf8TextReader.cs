using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Cysharp.IO;

public sealed class Utf8TextReader : IDisposable, IAsyncDisposable
{
    const int DefaultCharBufferSize = 1024; // buffer per line.
    const int MinBufferSize = 128;

    readonly Utf8StreamReader reader;
    readonly int bufferSize;
    char[] outputBuffer;
    bool isDisposed;

    public Utf8TextReader(Utf8StreamReader reader)
        : this(reader, DefaultCharBufferSize)
    {
    }

    public Utf8TextReader(Utf8StreamReader reader, int bufferSize)
    {
        this.reader = reader;
        this.outputBuffer = ArrayPool<char>.Shared.Rent(Math.Max(bufferSize, MinBufferSize));
        this.bufferSize = bufferSize;
    }

    public Stream BaseStream => reader.BaseStream;
    public Utf8StreamReader BaseReader => reader;

    public ValueTask<bool> LoadIntoBufferAsync(CancellationToken cancellationToken = default)
    {
        return reader.LoadIntoBufferAsync(cancellationToken);
    }

    public bool TryReadLine(out ReadOnlyMemory<char> line)
    {
        if (!reader.TryReadLine(out var utf8Line))
        {
            line = default;
            return false;
        }

        var maxCharCount = Encoding.UTF8.GetMaxCharCount(utf8Line.Length);
        if (outputBuffer.Length < maxCharCount)
        {
            // need new buffer
            ArrayPool<char>.Shared.Return(outputBuffer);
            outputBuffer = ArrayPool<char>.Shared.Rent(maxCharCount);
        }

        var size = Encoding.UTF8.GetChars(utf8Line.Span, outputBuffer);
        line = outputBuffer.AsMemory(0, size);
        return true;
    }

    public ValueTask<ReadOnlyMemory<char>?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (TryReadLine(out var line))
        {
            return new ValueTask<ReadOnlyMemory<char>?>(line);
        }

        return Core(cancellationToken);

#if !NETSTANDARD
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<ReadOnlyMemory<char>?> Core(CancellationToken cancellationToken)
        {
            if (await LoadIntoBufferAsync(cancellationToken).ConfigureAwait(reader.ConfigureAwait))
            {
                if (TryReadLine(out var line))
                {
                    return line;
                }
            }
            return null;
        }
    }

    public async IAsyncEnumerable<ReadOnlyMemory<char>> ReadAllLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await LoadIntoBufferAsync(cancellationToken).ConfigureAwait(reader.ConfigureAwait))
        {
            while (TryReadLine(out var line))
            {
                yield return line;
            }
        }
    }

    public async ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        using var writer = new SegmentedArrayBufferWriter<char>();
        var memory = writer.GetNextMemory();
        var writtenCurrentMemoryCount = 0;
        var decoder = Encoding.UTF8.GetDecoder();

        await foreach (var chunk in reader.ReadToEndChunksAsync(cancellationToken))
        {
            var input = chunk;
        CONVERT:
            {
                decoder.Convert(input.Span, memory.Span, flush: false, out var bytesUsed, out var charsUsed, out var completed);
                input = input.Slice(bytesUsed);
                memory = memory.Slice(charsUsed);
                writtenCurrentMemoryCount += charsUsed;

                if (memory.Length == 0)
                {
                    writtenCurrentMemoryCount = 0;
                    memory = writer.GetNextMemory();
                }

                if (input.Length != 0)
                {
                    goto CONVERT;
                }
            }
        }

        var stringLength = writer.GetTotalCount(writtenCurrentMemoryCount);
        return string.Create(stringLength, (writer, writtenCurrentMemoryCount), static (stringSpan, state) =>
        {
            var (writer, writtenCurrentMemoryCount) = state;
            foreach (var item in writer.GetSegmentsAndDispose(writtenCurrentMemoryCount))
            {
                item.Span.CopyTo(stringSpan);
                stringSpan = stringSpan.Slice(item.Length);
            }
        });
    }

    public void Reset()
    {
        ThrowIfDisposed();
        ClearState();
        reader.Reset();
    }

    public void Reset(Stream stream)
    {
        ThrowIfDisposed();
        ClearState();

        outputBuffer = ArrayPool<char>.Shared.Rent(Math.Max(bufferSize, MinBufferSize));
        reader.Reset(stream);
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        ClearState();
        reader.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (isDisposed) return default;

        isDisposed = true;
        ClearState();
        return reader.DisposeAsync();
    }

    void ClearState()
    {
        if (outputBuffer != null)
        {
            ArrayPool<char>.Shared.Return(outputBuffer);
            outputBuffer = null!;
        }
    }

    void ThrowIfDisposed()
    {
        if (isDisposed) throw new ObjectDisposedException("");
    }
}

public static class Utf8StreamReaderExtensions
{
    public static Utf8TextReader AsTextReader(this Utf8StreamReader reader) => new Utf8TextReader(reader);
    public static Utf8TextReader AsTextReader(this Utf8StreamReader reader, int bufferSize) => new Utf8TextReader(reader, bufferSize);
}
