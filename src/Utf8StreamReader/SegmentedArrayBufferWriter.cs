using System.Buffers;
using System.Runtime.CompilerServices;

namespace Cysharp.IO;

// similar as .NET9 SegmentedArrayBuilder<T> but for async operation and direct write
internal sealed class SegmentedArrayBufferWriter<T> : IDisposable
{
    const int SegmentSize = 27; // TODO: change size
    const int InitialSize = 1024;

    T[][] segments;
    int segmentsCount = 0;
    int countInFinishedSegments;
    bool isDisposed = false;

    public SegmentedArrayBufferWriter()
    {
        segments = new T[SegmentSize][];
    }

    public Memory<T> GetNextMemory()
    {
        int size;
        if (segmentsCount == 0)
        {
            size = InitialSize;
        }
        else
        {
            var finished = segments[segmentsCount];
            size = finished.Length * 2; // TODO: calc for max
            countInFinishedSegments += finished.Length;
        }
        segmentsCount++;
        var array = ArrayPool<T>.Shared.Rent(size);
        return array;
    }

    public T[] ToArrayAndDispose(int lastMemoryCount)
    {
        if (isDisposed) throw new ObjectDisposedException("");
        isDisposed = true;

        var size = countInFinishedSegments + lastMemoryCount;
        if (size == 0) return [];

        var result = new T[size];
        var destination = result.AsSpan();
        var finishedSegmentCount = segmentsCount - 1;
        for (int i = 0; i < finishedSegmentCount; i++)
        {
            var segment = segments[i];
            segment.AsSpan().CopyTo(destination);
            destination = destination.Slice(segment.Length);
            ArrayPool<T>.Shared.Return(segment, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }

        // last array
        segments[segmentsCount].CopyTo(destination);

        return result;
    }

    public void Dispose()
    {
        if (isDisposed) return;

        isDisposed = true;
        for (int i = 0; i < segmentsCount; i++)
        {
            ArrayPool<T>.Shared.Return(segments[i], clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }
}
