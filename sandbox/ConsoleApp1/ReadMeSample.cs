using Cysharp.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleApp1;

internal class ReadMeSample
{


    public async void Sample1(Stream stream)
    {
        using var reader = new Utf8StreamReader(stream);

        // Most performant style, similar as System.Threading.Channels
        while (await reader.LoadIntoBufferAsync())
        {
            while (reader.TryReadLine(out var line))
            {
                // line is ReadOnlyMemory<byte>, deserialize UTF8 directly.
                _ = JsonSerializer.Deserialize<Foo>(line.Span);
            }
        }
    }

    public async void Sample2(Stream stream)
    {
        using var reader = new Utf8StreamReader(stream);

        // Classical style, same as StreamReader
        ReadOnlyMemory<byte>? line = null;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            _ = JsonSerializer.Deserialize<Foo>(line.Value.Span);
        }
    }

    public async void Sample3(Stream stream)
    {
        using var reader = new Utf8StreamReader(stream);

        // Most easiest style, use async streams
        await foreach (var line in reader.ReadAllLinesAsync())
        {
            _ = JsonSerializer.Deserialize<Foo>(line.Span);
        }
    }
}


public class Foo
{

}
