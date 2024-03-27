using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cysharp.IO;

public sealed class Utf8StreamReader : IAsyncDisposable, IDisposable
{
    // NetStandard2.1 does not have Array.MaxLength so use constant.
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int DefaultBufferSize = 4096;
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
        init => skipBom = checkPreamble = value;
    }

    public bool ConfigureAwait { get; init; } = false;

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

    public Utf8StreamReader(string path)
      : this(path, DefaultBufferSize)
    {
    }

    public Utf8StreamReader(string path, int bufferSize)
        : this(OpenPath(path), bufferSize, leaveOpen: false)
    {
    }

    static FileStream OpenPath(string path)
    {
        // useAsync:1 + bufferSize 1 chooses internal FileStreamStrategy to AsyncWindowsFileStreamStrategy(in windows)
        // but bufferSize larger than 1, wrapped strategy with BufferedFileStreamStrategy, it is unnecessary in ReadLine.
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, useAsync: true);
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

            lastNewLinePosition = lastExaminedPosition = -2; // not exists new line in this bufer
            line = default;
            return false;
        }

        line = inputBuffer.AsMemory(positionBegin, index); // positionBegin..(positionBegin+index)
        positionBegin = (positionBegin + newLineIndex + 1);
        lastNewLinePosition = lastExaminedPosition = -1;
        return true;
    }

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
                    // convert to absoulte
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
        var examined = positionEnd; // avoid to duplicate scan

    LOAD_INTO_BUFFER:
        // not reaches full, repeatedly read
        if (positionEnd != inputBuffer.Length)
        {
            var read = await stream.ReadAsync(inputBuffer.AsMemory(positionEnd), cancellationToken).ConfigureAwait(ConfigureAwait);
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
                var index = IndexOfNewline(inputBuffer.AsSpan(examined, positionEnd - examined), out var examinedIndex);
                if (index != -1)
                {
                    lastNewLinePosition = examined + index;
                    lastExaminedPosition = examined + examinedIndex;
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

    public ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (TryReadLine(out var line))
        {
            return new ValueTask<ReadOnlyMemory<byte>?>(line);
        }

        return Core(this, cancellationToken);

        static async ValueTask<ReadOnlyMemory<byte>?> Core(Utf8StreamReader self, CancellationToken cancellationToken)
        {
            if (await self.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(self.ConfigureAwait))
            {
                if (self.TryReadLine(out var line))
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
