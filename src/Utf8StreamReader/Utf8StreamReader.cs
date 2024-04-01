using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cysharp.IO;

public enum FileOpenMode
{
    Scalability,
    Throughput
}

public sealed class Utf8StreamReader : IAsyncDisposable, IDisposable
{
    // NetStandard2.1 does not have Array.MaxLength so use constant.
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int DefaultBufferSize = 65536;
    const int MinBufferSize = 1024;

    Stream stream;
    readonly bool leaveOpen;
    readonly int bufferSize;
    bool endOfStream;
    bool checkPreamble = true;
    bool skipBom = true;
    bool isDisposed;

    byte[] inputBuffer;
    int positionBegin;
    int positionEnd;
    int lastNewLinePosition = -2; // -2 is not exists new line in buffer, -1 is not yet searched. absolute path from inputBuffer begin
    int lastExaminedPosition;

    public bool SkipBom
    {
        get => skipBom;
        init => skipBom = checkPreamble = value;
    }

    public bool ConfigureAwait { get; init; } = false;

    public bool SyncRead { get; init; } = false;

    public Utf8StreamReader(Stream stream)
        : this(stream, DefaultBufferSize, false)
    {
    }

    public Utf8StreamReader(Stream stream, int bufferSize)
        : this(stream, bufferSize, false)
    {
    }

    public Utf8StreamReader(Stream stream, bool leaveOpen)
        : this(stream, DefaultBufferSize, leaveOpen)
    {
    }

    public Utf8StreamReader(Stream stream, int bufferSize, bool leaveOpen)
    {
        this.inputBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, MinBufferSize));
        this.stream = stream;
        this.bufferSize = bufferSize;
        this.leaveOpen = leaveOpen;
    }

    public Utf8StreamReader(string path, FileOpenMode fileOpenMode = FileOpenMode.Throughput)
      : this(path, DefaultBufferSize, fileOpenMode)
    {
    }

    public Utf8StreamReader(string path, int bufferSize, FileOpenMode fileOpenMode = FileOpenMode.Throughput)
        : this(OpenPath(path, fileOpenMode), bufferSize, leaveOpen: false)
    {
    }

    static FileStream OpenPath(string path, FileOpenMode fileOpenMode = FileOpenMode.Throughput)
    {
        var fileOptions = (fileOpenMode == FileOpenMode.Scalability)
            ? (FileOptions.SequentialScan | FileOptions.Asynchronous)
            : FileOptions.SequentialScan;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, options: fileOptions);
    }

#if !NETSTANDARD

    public Utf8StreamReader(string path, FileStreamOptions options)
        : this(path, options, DefaultBufferSize)
    {
    }

    public Utf8StreamReader(string path, FileStreamOptions options, int bufferSize)
        : this(OpenPath(path, options), bufferSize)
    {
    }

    static FileStream OpenPath(string path, FileStreamOptions options)
    {
        return new FileStream(path, options);
    }

#endif

    // Peek() and EndOfStream is `Sync` method so does not provided.

    public Stream BaseStream => stream;

    public bool TryReadLine(out ReadOnlyMemory<byte> line)
    {
        ThrowIfDisposed();

        if (lastNewLinePosition >= 0)
        {
            line = inputBuffer.AsMemory(positionBegin, lastNewLinePosition - positionBegin);
            positionBegin = lastExaminedPosition + 1;
            lastNewLinePosition = lastExaminedPosition = -1;
            return true;
        }

        // AsSpan(positionBegin..positionEnd) is more readable but don't use range notation, it is slower.
        var index = IndexOfNewline(inputBuffer.AsSpan(positionBegin, positionEnd - positionBegin), out var newLineIndex);
        if (index == -1)
        {
            if (endOfStream && positionBegin != positionEnd)
            {
                // return last line
                line = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
                positionBegin = positionEnd;
                return true;
            }

            lastNewLinePosition = lastExaminedPosition = -2; // not exists new line in this buffer
            line = default;
            return false;
        }

        line = inputBuffer.AsMemory(positionBegin, index); // positionBegin..(positionBegin+index)
        positionBegin = (positionBegin + newLineIndex + 1);
        lastNewLinePosition = lastExaminedPosition = -1;
        return true;
    }

#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<bool> LoadIntoBufferAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // pre-check

        if (endOfStream)
        {
            if (positionBegin != positionEnd) // not yet fully consumed
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (lastNewLinePosition >= 0) return true; // already filled line into buffer

            // lastNewLineIndex, lastExamined is relative from positionBegin
            if (lastNewLinePosition == -1)
            {
                var index = IndexOfNewline(inputBuffer.AsSpan(positionBegin, positionEnd - positionBegin), out var examinedIndex);
                if (index != -1)
                {
                    // convert to absolute
                    lastNewLinePosition = positionBegin + index;
                    lastExaminedPosition = positionBegin + examinedIndex;
                    return true;
                }
            }
            else
            {
                // change status to not searched
                lastNewLinePosition = -1;
            }
        }

        // requires load into buffer

        if (positionEnd != 0 && positionBegin == positionEnd)
        {
            // can reset buffer position
            positionBegin = positionEnd = 0;
        }

        var examined = positionEnd; // avoid to duplicate scan

    LOAD_INTO_BUFFER:
        // not reaches full, repeatedly read
        if (positionEnd != inputBuffer.Length)
        {
            var read = SyncRead
                ? stream.Read(inputBuffer.AsSpan(positionEnd))
                : await stream.ReadAsync(inputBuffer.AsMemory(positionEnd), cancellationToken).ConfigureAwait(ConfigureAwait);

            positionEnd += read;
            if (read == 0)
            {
                endOfStream = true;
                if (positionBegin != positionEnd) // has last line
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                // first Read, require to check UTF8 BOM
                if (checkPreamble)
                {
                    if (read < 2) goto LOAD_INTO_BUFFER;
                    if (inputBuffer.AsSpan(0, 3).SequenceEqual(Encoding.UTF8.Preamble))
                    {
                        positionBegin = 3;
                    }
                    checkPreamble = false;
                }

                // scan examined(already scanned) to End.
                // Back one index to check if CRLF fell on buffer boundary
                var scanFrom = examined > 0 ? examined - 1 : examined;
                var index = IndexOfNewline(inputBuffer.AsSpan(scanFrom, positionEnd - scanFrom), out var examinedIndex);
                if (index != -1)
                {
                    lastNewLinePosition = scanFrom + index;
                    lastExaminedPosition = scanFrom + examinedIndex;
                    return true;
                }

                examined = positionEnd;
                goto LOAD_INTO_BUFFER;
            }
        }

        // slide current buffer
        if (positionBegin != 0)
        {
            inputBuffer.AsSpan(positionBegin, positionEnd - positionBegin).CopyTo(inputBuffer);
            positionEnd -= positionBegin;
            positionBegin = 0;
            examined = positionEnd;
            goto LOAD_INTO_BUFFER;
        }

        // buffer is completely full, needs resize(positionBegin, positionEnd, examined are same)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(GetNewSize(inputBuffer.Length));
            inputBuffer.AsSpan().CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(inputBuffer);
            inputBuffer = newBuffer;
            goto LOAD_INTO_BUFFER;
        }
    }

#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask LoadIntoBufferAtLeastAsync(int minimumBytes, CancellationToken cancellationToken = default)
    {
        var loaded = positionEnd - positionBegin;
        if (minimumBytes < loaded)
        {
            return;
        }
        if (endOfStream)
        {
            throw new EndOfStreamException();
        }

        if (positionEnd != 0 && positionBegin == positionEnd)
        {
            // can reset buffer position
            loaded = positionBegin = positionEnd = 0;
            lastNewLinePosition = -1;
        }

        var remains = minimumBytes - loaded;

        if (inputBuffer.Length - positionEnd < remains)
        {
            // needs resize before load loop
            var newBuffer = ArrayPool<byte>.Shared.Rent(Math.Min(GetNewSize(inputBuffer.Length), positionEnd + remains));
            inputBuffer.AsSpan().CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(inputBuffer);
            inputBuffer = newBuffer;
        }

    LOAD_INTO_BUFFER:
        var read = SyncRead
            ? stream.Read(inputBuffer.AsSpan(positionEnd))
            : await stream.ReadAsync(inputBuffer.AsMemory(positionEnd), cancellationToken).ConfigureAwait(ConfigureAwait);
        positionEnd += read;
        if (read == 0)
        {
            throw new EndOfStreamException();
        }
        else
        {
            // first Read, require to check UTF8 BOM
            if (checkPreamble)
            {
                if (read < 2) goto LOAD_INTO_BUFFER;
                if (inputBuffer.AsSpan(0, 3).SequenceEqual(Encoding.UTF8.Preamble))
                {
                    positionBegin = 3;
                    remains += 3; // read 3 bytes should not contains
                }
                checkPreamble = false;
            }

            remains -= read;
            if (remains < 0)
            {
                return;
            }

            goto LOAD_INTO_BUFFER;
        }
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadToEndChunksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (endOfStream)
        {
            var result = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
            positionBegin = positionEnd;
            if (result.Length != 0)
            {
                yield return result;
            }
            yield break;
        }

        if (positionEnd != 0 && positionBegin != positionEnd)
        {
            yield return inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
        }

        positionBegin = positionEnd = 0;
        lastNewLinePosition = -2;

    LOAD_INTO_BUFFER:
        var read = SyncRead
            ? stream.Read(inputBuffer.AsSpan(positionEnd))
            : await stream.ReadAsync(inputBuffer.AsMemory(positionEnd), cancellationToken).ConfigureAwait(ConfigureAwait);

        positionEnd += read;
        if (read == 0)
        {
            endOfStream = true;
            var result = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
            positionBegin = positionEnd;
            if (result.Length != 0)
            {
                yield return result;
            }
            yield break;
        }
        else
        {
            // NOTE: ReadToEnd does not check, trim BOM.
            yield return inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
            positionBegin = positionEnd = 0;
            goto LOAD_INTO_BUFFER;
        }
    }

    public ValueTask<byte[]> ReadToEndAsync(CancellationToken cancellationToken = default)
    {
        if (BaseStream is FileStream fs && fs.CanSeek)
        {
            return ReadToEndAsync(fs.Length, cancellationToken);
        }

        return ReadToEndAsync(-1, cancellationToken);
    }

    public async ValueTask<byte[]> ReadToEndAsync(long resultSizeHint, CancellationToken cancellationToken = default)
    {
        if (endOfStream)
        {
            var slice = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
            positionBegin = positionEnd = 0;
            lastNewLinePosition = -2;
            return (slice.Length != 0)
                ? slice.ToArray()
                : [];
        }

        if (resultSizeHint != -1)
        {
            if (resultSizeHint == 0)
            {
                return [];
            }

            var result = new byte[resultSizeHint];
            var memory = result.AsMemory();

            if (positionEnd != 0 && positionBegin != positionEnd)
            {
                var slice = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
                slice.CopyTo(memory);
                memory = memory.Slice(slice.Length);
            }

            positionBegin = positionEnd = 0;
            lastNewLinePosition = -2;

            while (true)
            {
                var read = SyncRead
                   ? stream.Read(memory.Span)
                   : await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(ConfigureAwait);

                if (read == 0)
                {
                    break;
                }
                else
                {
                    memory = memory.Slice(read);
                }
            }

            return result;
        }
        else
        {
            using var writer = new SegmentedArrayBufferWriter<byte>();
            var memory = writer.GetNextMemory();
            var currentMemoryWritten = 0;

            if (positionEnd != 0 && positionBegin != positionEnd)
            {
                var slice = inputBuffer.AsMemory(positionBegin, positionEnd - positionBegin);
                writer.Write(ref memory, ref currentMemoryWritten, slice.Span);
            }

            positionBegin = positionEnd = 0;
            lastNewLinePosition = -2;

            while (true)
            {
                var read = SyncRead
                   ? stream.Read(memory.Span)
                   : await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(ConfigureAwait);

                if (read == 0)
                {
                    break;
                }
                else
                {
                    // NOTE: ReadToEnd does not check, trim BOM.
                    memory = memory.Slice(read);
                    currentMemoryWritten += read;
                    if (memory.Length == 0)
                    {
                        memory = writer.GetNextMemory();
                        currentMemoryWritten = 0;
                    }
                }
            }

            endOfStream = true;
            return (currentMemoryWritten == 0)
                ? []
                : writer.ToArrayAndDispose(currentMemoryWritten);
        }
    }

    public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (TryReadLine(out var line))
        {
            return new ValueTask<ReadOnlyMemory<byte>?>(line);
        }

        return Core(cancellationToken);

#if !NETSTANDARD
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<ReadOnlyMemory<byte>?> Core(CancellationToken cancellationToken)
        {
            if (await LoadIntoBufferAsync(cancellationToken).ConfigureAwait(ConfigureAwait))
            {
                if (TryReadLine(out var line))
                {
                    return line;
                }
            }
            return null;
        }
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAllLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await LoadIntoBufferAsync(cancellationToken).ConfigureAwait(ConfigureAwait))
        {
            while (TryReadLine(out var line))
            {
                yield return line;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeek(out byte data)
    {
        ThrowIfDisposed();

        if (positionEnd - positionBegin > 0)
        {
            data = inputBuffer[positionBegin];
            return true;
        }

        data = default;
        return false;
    }

    public ValueTask<byte> PeekAsync(CancellationToken cancellationToken = default)
    {
        if (TryPeek(out var data))
        {
            return new ValueTask<byte>(data);
        }

        return Core(cancellationToken);

#if !NETSTANDARD
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<byte> Core(CancellationToken cancellationToken)
        {
            await LoadIntoBufferAtLeastAsync(1, cancellationToken);
            return inputBuffer[positionBegin];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(out byte data)
    {
        ThrowIfDisposed();

        if (TryPeek(out data))
        {
            positionBegin += 1;
            lastNewLinePosition = lastExaminedPosition = -1;
            return true;
        }

        data = default;
        return false;
    }

    public ValueTask<byte> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (TryRead(out var data))
        {
            return new ValueTask<byte>(data);
        }

        return Core(cancellationToken);

#if !NETSTANDARD
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<byte> Core(CancellationToken cancellationToken)
        {
            await LoadIntoBufferAtLeastAsync(1, cancellationToken);
            TryRead(out var data);
            return data;
        }
    }

    public bool TryReadBlock(int count, out ReadOnlyMemory<byte> block)
    {
        ThrowIfDisposed();

        var loaded = positionEnd - positionBegin;
        if (count < loaded)
        {
            block = inputBuffer.AsMemory(positionBegin, count);
            positionBegin += count;
            lastNewLinePosition = lastExaminedPosition = -1;
            return false;
        }

        block = default;
        return false;
    }

    public ValueTask<ReadOnlyMemory<byte>> ReadBlockAsync(int count, CancellationToken cancellationToken = default)
    {
        if (TryReadBlock(count, out var block))
        {
            return new ValueTask<ReadOnlyMemory<byte>>(block);
        }

        return Core(count, cancellationToken);

#if !NETSTANDARD
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        async ValueTask<ReadOnlyMemory<byte>> Core(int count, CancellationToken cancellationToken)
        {
            await LoadIntoBufferAtLeastAsync(count, cancellationToken);
            TryReadBlock(count, out var block);
            return block;
        }
    }

    static int GetNewSize(int capacity)
    {
        int newCapacity = unchecked(capacity * 2);
        if ((uint)newCapacity > ArrayMaxLength) newCapacity = ArrayMaxLength;
        return newCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int IndexOfNewline(ReadOnlySpan<byte> span, out int examined)
    {
        // we only supports LF(\n) or CRLF(\r\n).
        var indexOfNewLine = span.IndexOf((byte)'\n');
        if (indexOfNewLine == -1)
        {
            examined = span.Length - 1;
            return -1;
        }
        examined = indexOfNewLine;

        if (indexOfNewLine >= 1 && span[indexOfNewLine - 1] == '\r')
        {
            indexOfNewLine--; // case of '\r\n'
        }

        return indexOfNewLine;
    }

    // Reset API like Utf8JsonWriter

    public void Reset()
    {
        ThrowIfDisposed();
        ClearState();
    }

    public void Reset(Stream stream)
    {
        ThrowIfDisposed();
        ClearState();

        this.inputBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, MinBufferSize));
        this.stream = stream;
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        ClearState();
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;

        isDisposed = true;
        if (!leaveOpen && stream != null)
        {
            await stream.DisposeAsync().ConfigureAwait(ConfigureAwait);
            stream = null!;
        }
        ClearState();
    }

    void ClearState()
    {
        if (inputBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(inputBuffer);
            inputBuffer = null!;
        }

        if (!leaveOpen && stream != null)
        {
            stream.Dispose();
            stream = null!;
        }

        positionBegin = positionEnd = 0;
        endOfStream = false;
        checkPreamble = skipBom;
        lastNewLinePosition = lastExaminedPosition = -2;
    }

    void ThrowIfDisposed()
    {
        if (isDisposed) throw new ObjectDisposedException("");
    }
}
