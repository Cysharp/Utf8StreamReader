#pragma warning disable CS1998

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Utf8StreamReaderTests;

internal class FakeMemoryStream : Stream
{
    #region NotImplemented

    public override bool CanRead => true;

    public override bool CanSeek => throw new NotImplementedException();

    public override bool CanWrite => throw new NotImplementedException();

    public override long Length => throw new NotImplementedException();

    public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override void Flush()
    {
        throw new NotImplementedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    #endregion

    public bool IsDisposed { get; set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
    }

    Memory<byte>[] lastAddedData = default!;
    Queue<StrongBox<Memory<byte>>> data = new();

    public void AddMemory(params Memory<byte>[] memories)
    {
        foreach (Memory<byte> mem in memories)
        {
            if (mem.Length == 0) throw new ArgumentException("Length 0 is not allowed.");
            data.Enqueue(new(mem));
        }
        this.lastAddedData = memories;
    }

    public void Restart()
    {
        data.Clear();
        AddMemory(lastAddedData);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (data.Count == 0)
        {
            return 0;
        }

        var memory = data.Peek().Value;

        var copySize = Math.Min(memory.Length, buffer.Length);
        memory.Slice(0, copySize).CopyTo(buffer);
        var newMemory = memory.Slice(copySize);
        if (newMemory.Length == 0)
        {
            data.Dequeue();
        }
        else
        {
            data.Peek().Value = newMemory;
        }

        return copySize;
    }
}
