using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cysharp.IO;

// similar as .NET9 SegmentedArrayBuilder<T> but for async operation and direct write
internal sealed class SegmentedArrayBufferWriter<T> : IDisposable
{
    // NetStandard2.1 does not have Array.MaxLength so use constant.
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int InitialSize = 1024; // TODO: change initial size

    InlineArray19<T> segments;    // TODO: InlineArray count
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

        var size = countInFinishedSegments + currentWritten;
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
struct InlineArray19<T>
{
    T[] array00;
    T[] array01;
    T[] array02;
    T[] array03;
    T[] array04;
    T[] array05;
    T[] array06;
    T[] array07;
    T[] array08;
    T[] array09;
    T[] array10;
    T[] array11;
    T[] array12;
    T[] array13;
    T[] array14;
    T[] array15;
    T[] array16;
    T[] array17;
    T[] array18;

    public T[] this[int i]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (i < 0 || i >= 19) Throw();
            return Unsafe.Add(ref Unsafe.As<InlineArray19<T>, T[]>(ref Unsafe.AsRef(in this)), i);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (i < 0 || i >= 19) Throw();
            Unsafe.Add(ref Unsafe.As<InlineArray19<T>, T[]>(ref Unsafe.AsRef(in this)), i) = value;
        }
    }

    void Throw()
    {
        throw new ArgumentOutOfRangeException();
    }
}
