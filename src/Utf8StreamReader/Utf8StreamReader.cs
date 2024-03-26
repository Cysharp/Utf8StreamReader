using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cysharp.IO;

public sealed class Utf8StreamReader : IDisposable
{
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int DefaultBufferSize = 1024;
    const int DefaultFileStreamBufferSize = 4096;
    const int MinBufferSize = 128;

    readonly Stream stream;
    readonly bool leaveOpen;
    bool endOfStream;
    bool checkPreamble = true;
    bool isDisposed;

    byte[] inputBuffer;
    int positionBegin;
    int positionEnd;


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
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultFileStreamBufferSize, useAsync: true);
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

#if !NETSTANDARD
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<ReadOnlyMemory<byte>?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (positionEnd != 0 && TryReadLine(positionBegin, out var line)) // scan Begin to End.
        {
            return line;
        }

        if (endOfStream)
        {
            return null;
        }

        var examined = positionEnd; // avoid to duplicate scan

    LOAD_INTO_BUFFER:
        // not reaches full, repeatedly read
        if (positionEnd != inputBuffer.Length)
        {
            var read = await stream.ReadAsync(inputBuffer.AsMemory(positionEnd), cancellationToken);
            if (read == 0)
            {
                endOfStream = true;
                if (TryReadLine(positionEnd, out line)) //  try to return last line
                {
                    return line;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                positionEnd += read;

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

                if (TryReadLine(examined, out line)) // scan examined(already scanned) to End.
                {
                    return line;
                }
                examined += read;
                goto LOAD_INTO_BUFFER;
            }
        }

        // slide current buffer
        if (positionBegin != 0)
        {
            inputBuffer.AsSpan(positionBegin..positionEnd).CopyTo(inputBuffer);
            positionBegin = 0;
            positionEnd -= positionBegin;
            examined = positionEnd;
            goto LOAD_INTO_BUFFER;
        }

        // needs resize(positionBegin, positionEnd, examined are same)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(GetNewSize(inputBuffer.Length));
            inputBuffer.AsSpan().CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(inputBuffer);
            inputBuffer = newBuffer;
            goto LOAD_INTO_BUFFER;
        }
    }

    static int GetNewSize(int capacity)
    {
        int newCapacity = unchecked(capacity * 2);
        if ((uint)newCapacity > ArrayMaxLength) newCapacity = ArrayMaxLength;
        return newCapacity;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<byte>? line;
        while ((line = await ReadLineAsync(cancellationToken)) != null)
        {
            yield return line.Value;
        }
    }

    bool TryReadLine(int examined, out ReadOnlyMemory<byte> line)
    {
        var index = IndexOfNewline(inputBuffer.AsSpan(examined..positionEnd), out var newLineIndex);
        if (index == -1)
        {
            if (endOfStream && positionBegin != positionEnd)
            {
                // return last line
                line = inputBuffer.AsMemory(positionBegin..positionEnd);
                positionBegin = positionEnd;
                return true;
            }

            line = default;
            return false;
        }

        // index and newLineIndex is based on examined so needs add
        line = inputBuffer.AsMemory(positionBegin..(examined + index));
        positionBegin = (examined + newLineIndex + 1);
        return true;
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

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        ArrayPool<byte>.Shared.Return(inputBuffer);
        inputBuffer = null!;

        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    void ThrowIfDisposed()
    {
        if (isDisposed) throw new ObjectDisposedException("");
    }
}