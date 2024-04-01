using Cysharp.IO;
using System.Buffers.Text;
using System.Text;

namespace ConsoleApp1;

public enum RespType : byte
{
    SimpleStrings = (byte)'+',
    Errors = (byte)'-',
    Integers = (byte)':',
    BulkStrings = (byte)'$',
    Arrays = (byte)'*'
}

public abstract class RespReader : IDisposable
{
    Utf8StreamReader reader;

    public RespReader(Stream stream)
    {
        this.reader = new Utf8StreamReader(stream);
    }

    // NOTE: for more fast processing, you need to use TryRead method.

    public async ValueTask<RespType> ReadRespTypeAsync(CancellationToken cancellationToken = default)
    {
        return (RespType)await reader.ReadAsync(cancellationToken);
    }

    // all read message api expect befor call ReadRespTypeAsync(already trimed type prefix)

    public async ValueTask<string> ReadSimpleStringAsync(CancellationToken cancellationToken = default) // +OK\r\n
    {
        return Encoding.UTF8.GetString((await reader.ReadLineAsync(cancellationToken)).Value.Span);
    }

    public async ValueTask<string> ReadErrorMessageAsync(CancellationToken cancellationToken = default) // -Error message\r\n
    {
        return Encoding.UTF8.GetString((await reader.ReadLineAsync(cancellationToken)).Value.Span);
    }

    public async ValueTask<long> ReadIntegerAsync(CancellationToken cancellationToken = default) // :1000\r\n
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        Utf8Parser.TryParse(line.Value.Span, out long value, out _);
        return value;
    }

    public async ValueTask<ReadOnlyMemory<byte>?> ReadBulkStringAsync(CancellationToken cancellationToken = default) // "$5\r\nhello\r\n"
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        Utf8Parser.TryParse(line.Value.Span, out int count, out _);
        if (count == -1)
        {
            return null;
        }
        else
        {
            var dataWithNewLine = await reader.ReadBlockAsync(count + 2, cancellationToken);
            return dataWithNewLine[..^2]; // without newline
        }
    }

    // for perf improvement, ReadIntegerArray, ReadStringArray, ReadArray<T> for bulkstrings is better approach
    public async ValueTask<object[]> ReadArrayAsync(CancellationToken cancellationToken = default) // "*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n"
    {
        var line = await reader.ReadLineAsync();
        Utf8Parser.TryParse(line.Value.Span, out int count, out _);

        var result = new object[count];
        for (int i = 0; i < count; i++)
        {
            var type = await ReadRespTypeAsync(cancellationToken);
            switch (type)
            {
                case RespType.SimpleStrings:
                    result[i] = await ReadSimpleStringAsync(cancellationToken);
                    break;
                case RespType.Errors:
                    result[i] = await ReadErrorMessageAsync(cancellationToken);
                    break;
                case RespType.Integers:
                    result[i] = await ReadIntegerAsync(cancellationToken);
                    break;
                case RespType.BulkStrings:
                    result[i] = (await ReadBulkStringAsync(cancellationToken)).Value.ToArray(); // materialize immediately
                    break;
                case RespType.Arrays:
                    result[i] = await ReadArrayAsync(cancellationToken);
                    break;
                default:
                    break;
            }
        }

        return result;
    }

    public void Dispose()
    {
        reader.Dispose();
    }
}
