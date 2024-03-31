using Cysharp.IO;
using System.Text;

namespace ConsoleApp1;

public abstract class RespParser
{
    enum RespType : byte
    {
        SimpleStrings = (byte)'+',
        Errors = (byte)'-',
        Integers = (byte)':',
        BulkStrings = (byte)'$',
        Arrays = (byte)'*'
    }

    Utf8StreamReader reader;

    public RespParser(Stream stream)
    {
        this.reader = new Utf8StreamReader(stream);
    }

    public async Task ReadStartAsync(CancellationToken cancellationToken = default)
    {
        while (await reader.LoadIntoBufferAsync(cancellationToken))
        {
            while (reader.TryReadLine(out var line))
            {
                await ParseAsync(line, cancellationToken);
            }
        }
    }

    async ValueTask ParseAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        var type = (RespType)line.Span[0];
        switch (type)
        {
            case RespType.SimpleStrings: // +OK\r\n
                OnSimpleString(line.Slice(1));
                break;
            case RespType.Errors: // -Error message\r\n
                OnError(line.Slice(1));
                break;
            case RespType.Integers: // :1000\r\n
                OnInteger(long.Parse(line.Span.Slice(1))); // Parse(ReadOnlySpan<byte> utf8Text
                break;
            case RespType.BulkStrings: // "$5\r\nhello\r\n"
                var count = int.Parse(line.Span.Slice(1)); // Parse(ReadOnlySpan<byte> utf8Text
                if (count == -1)
                {
                    OnBulkString(null);
                }
                else
                {
                    var dataWithNewLine = await reader.ReadBlockAsync(count + 2, cancellationToken);
                    OnBulkString(dataWithNewLine[..^2]); // without newline
                }
                break;
            case RespType.Arrays: // "*2\r\n$5\r\nhello\r\n$5\r\nworld\r\n"
                var length = int.Parse(line.Span.Slice(1));
                OnArrayBegin(length);
                for (var i = 0; i < length; i++)
                {
                    if (!reader.TryReadLine(out line))
                    {
                        line = (await reader.ReadLineAsync(cancellationToken)).Value;
                    }
                    await ParseAsync(line, cancellationToken);
                }
                OnArrayEnd();
                break;
            default:
                break;
        }
    }

    public abstract void OnSimpleString(ReadOnlyMemory<byte> strings);
    public abstract void OnError(ReadOnlyMemory<byte> errorMessage);
    public abstract void OnInteger(long integer);
    public abstract void OnBulkString(ReadOnlyMemory<byte>? data);
    public abstract void OnArrayBegin(int count);
    public abstract void OnArrayEnd();
}

public class ConsoleWriteLineRespParser(Stream stream)
    : RespParser(stream)
{
    public override void OnArrayBegin(int count)
    {
        Console.WriteLine($"ArrayBegin({count})");
    }

    public override void OnArrayEnd()
    {
        Console.WriteLine($"ArrayEnd");
    }

    public override void OnBulkString(ReadOnlyMemory<byte>? data)
    {
        Console.WriteLine($"BulkString:{(data == null ? "" : Encoding.UTF8.GetString(data.Value.Span))}");
    }

    public override void OnError(ReadOnlyMemory<byte> errorMessage)
    {
        Console.WriteLine($"Error:{Encoding.UTF8.GetString(errorMessage.Span)}");
    }

    public override void OnInteger(long integer)
    {
        Console.WriteLine($"Integer:{integer}");
    }

    public override void OnSimpleString(ReadOnlyMemory<byte> strings)
    {
        Console.WriteLine($"SimpleString:{Encoding.UTF8.GetString(strings.Span)}");
    }
}
