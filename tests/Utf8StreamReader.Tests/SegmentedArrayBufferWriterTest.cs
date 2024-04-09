namespace Utf8StreamReaderTests;

public class SegmentedArrayBufferWriterTest
{
    [Fact(Skip = "Reduce memory usage in CI")]
    public void AllocateFull()
    {
        var writer = new SegmentedArrayBufferWriter<byte>();

        var memCount = 8192;
        long total = 0;
        for (int i = 0; i < 18; i++)
        {
            var mem = writer.GetMemory();
            mem.Length.Should().Be(memCount);
            total += mem.Length;
            memCount *= 2;
            writer.Advance(mem.Length);
        }

        Memory<byte> lastMemory = writer.GetMemory();
        (total).Should().BeLessThan(Array.MaxLength);
        (total + lastMemory.Length).Should().BeGreaterThan(Array.MaxLength);
    }
}
