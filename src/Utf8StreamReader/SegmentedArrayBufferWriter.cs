using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cysharp.IO;

// similar as .NET9 SegmentedArrayBuilder<T> but for async operation and direct write
internal sealed class SegmentedArrayBufferWriter<T> : IDisposable
{
    // NetStandard2.1 does not have Array.MaxLength so use constant.
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int InitialSize = 65536;

    InlineArray14<T> segments;
    int currentSegmentIndex;
    int countInFinishedSegments;

    T[] currentSegment;
    int currentWritten;

    bool isDisposed = false;

    public int WrittenCount => countInFinishedSegments + currentWritten;

    public SegmentedArrayBufferWriter()
    {
        currentSegment = segments[0] = ArrayPool<T>.Shared.Rent(InitialSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<T> GetMemory() // no sizeHint
    {
        return currentSegment.AsMemory(currentWritten);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan()
    {
        return currentSegment.AsSpan(currentWritten);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        currentWritten += count;
        if (currentWritten == currentSegment.Length)
        {
            AllocateNextMemory();
        }
    }

    void AllocateNextMemory()
    {
        countInFinishedSegments += currentSegment.Length;
        var nextSize = (int)Math.Min(currentSegment.Length * 2L, ArrayMaxLength);

        currentSegmentIndex++;
        currentSegment = segments[currentSegmentIndex] = ArrayPool<T>.Shared.Rent(nextSize);
        currentWritten = 0;
    }

    public void Write(ReadOnlySpan<T> source)
    {
        while (source.Length != 0)
        {
            var destination = GetSpan();
            var copySize = Math.Min(source.Length, destination.Length);

            source.Slice(0, copySize).CopyTo(destination);

            Advance(copySize);
            source = source.Slice(copySize);
        }
    }

    public T[] ToArrayAndDispose()
    {
        if (isDisposed) throw new ObjectDisposedException("");
        isDisposed = true;

        var size = checked(countInFinishedSegments + currentWritten);
        if (size == 0)
        {
            ArrayPool<T>.Shared.Return(currentSegment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            return [];
        }

#if !NETSTANDARD
        var result = GC.AllocateUninitializedArray<T>(size);
#else
        var result = new T[size];
#endif
        var destination = result.AsSpan();

        // without current
        for (int i = 0; i < currentSegmentIndex; i++)
        {
            var segment = segments[i];
            segment.AsSpan().CopyTo(destination);
            destination = destination.Slice(segment.Length);
            ArrayPool<T>.Shared.Return(segment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        // write current
        currentSegment.AsSpan(0, currentWritten).CopyTo(destination);
        ArrayPool<T>.Shared.Return(currentSegment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        currentSegment = null!;
        segments = default!;
        return result;
    }

    // NOTE: create struct enumerator?
    public IEnumerable<ReadOnlyMemory<T>> GetSegmentsAndDispose()
    {
        if (isDisposed) throw new ObjectDisposedException("");
        isDisposed = true;

        // without current
        for (int i = 0; i < currentSegmentIndex; i++)
        {
            var segment = segments[i];
            yield return segment;
            ArrayPool<T>.Shared.Return(segment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        // current
        if (currentWritten != 0)
        {
            yield return currentSegment.AsMemory(0, currentWritten);
        }
        ArrayPool<T>.Shared.Return(currentSegment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        currentSegment = null!;
        segments = default!;
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        for (int i = 0; i <= currentSegmentIndex; i++)
        {
            ArrayPool<T>.Shared.Return(segments[i], clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        currentSegment = null!;
        segments = default!;
    }
}

[StructLayout(LayoutKind.Sequential)]
struct InlineArray14<T>
{
    T[] array00; // 65536
    T[] array01; // 131072
    T[] array02; // 262144
    T[] array03; // 524288
    T[] array04; // 1048576
    T[] array05; // 2097152
    T[] array06; // 4194304
    T[] array07; // 8388608
    T[] array08; // 16777216
    T[] array09; // 33554432
    T[] array10; // 67108864
    T[] array11; // 134217728
    T[] array12; // 268435456
    T[] array13; // 536870912
    T[] array14; // 1073741824 (Total = 2147418112), overflow

    public T[] this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (i < 0 || i > 14) Throw();
            return Unsafe.Add(ref array00, i);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (i < 0 || i > 14) Throw();
            Unsafe.Add(ref array00, i) = value;
        }
    }

    void Throw()
    {
        throw new ArgumentOutOfRangeException();
    }
}
