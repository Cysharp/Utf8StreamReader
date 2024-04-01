using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security;

namespace Cysharp.IO;

// similar as .NET9 SegmentedArrayBuilder<T> but for async operation and direct write
internal sealed class SegmentedArrayBufferWriter<T> : IDisposable
{
    // NetStandard2.1 does not have Array.MaxLength so use constant.
    const int ArrayMaxLength = 0X7FFFFFC7;

    const int SegmentSize = 27; // TODO: change size
    const int InitialSize = 1024;

    T[][] segments;
    int currentSegmentIndex = -1;
    int countInFinishedSegments;
    bool isDisposed = false;

    public SegmentedArrayBufferWriter()
    {
        segments = new T[SegmentSize][];
    }

    public int GetTotalCount(int currentMemoryCount) => countInFinishedSegments + currentMemoryCount;

    public Memory<T> GetNextMemory()
    {
        int size;
        if (currentSegmentIndex == -1)
        {
            size = InitialSize;
        }
        else
        {
            var finished = segments[currentSegmentIndex];
            size = (int)Math.Min(finished.Length * 2L, ArrayMaxLength);
            countInFinishedSegments += finished.Length;
        }

        currentSegmentIndex++;
        var array = ArrayPool<T>.Shared.Rent(size);
        segments[currentSegmentIndex] = array;
        return array;
    }

    public void Write(ref Memory<T> currentMemory, ref int currentMemoryWritten, ReadOnlySpan<T> source)
    {
        while (source.Length != 0)
        {
            var copySize = Math.Min(source.Length, currentMemory.Length);

            source.Slice(0, copySize).CopyTo(currentMemory.Span);

            currentMemory = currentMemory.Slice(copySize);
            currentMemoryWritten += copySize;
            if (currentMemory.Length == 0)
            {
                currentMemory = GetNextMemory();
                currentMemoryWritten = 0;
            }
            source = source.Slice(copySize);
        }
    }

    public T[] ToArrayAndDispose(int lastMemoryCount)
    {
        if (isDisposed) throw new ObjectDisposedException("");
        isDisposed = true;

        var size = countInFinishedSegments + lastMemoryCount;
        if (size == 0)
        {
            if (currentSegmentIndex != -1)
            {
                ArrayPool<T>.Shared.Return(segments[0], clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            }
            return [];
        }

#if !NETSTANDARD
        var result = GC.AllocateUninitializedArray<T>(size);
#else
        var result = new T[size];
#endif
        var destination = result.AsSpan();
        for (int i = 0; i < currentSegmentIndex; i++)
        {
            var segment = segments[i];
            segment.AsSpan().CopyTo(destination);
            destination = destination.Slice(segment.Length);
            ArrayPool<T>.Shared.Return(segment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        // last array
        var lastSegment = segments[currentSegmentIndex];
        lastSegment.AsSpan(0, lastMemoryCount).CopyTo(destination);
        ArrayPool<T>.Shared.Return(lastSegment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());

        return result;
    }

    // NOTE: create struct enumerator?
    public IEnumerable<ReadOnlyMemory<T>> GetSegmentsAndDispose(int lastMemoryCount)
    {
        if (isDisposed) throw new ObjectDisposedException("");
        isDisposed = true;

        if (currentSegmentIndex == -1)
        {
            yield return Array.Empty<T>();
            yield break;
        }

        for (int i = 0; i < currentSegmentIndex; i++)
        {
            var segment = segments[i];
            yield return segment;
            ArrayPool<T>.Shared.Return(segment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        // last array
        var lastSegment = segments[currentSegmentIndex];
        yield return lastSegment.AsMemory(0, lastMemoryCount);
        ArrayPool<T>.Shared.Return(lastSegment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        for (int i = 0; i <= currentSegmentIndex; i++)
        {
            ArrayPool<T>.Shared.Return(segments[i], clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
