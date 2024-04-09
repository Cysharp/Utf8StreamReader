using Cysharp.IO;
using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;




var aa = Encoding.UTF8.GetBytes("$5\r\nhello\r\n");
var stream = new MemoryStream(aa);

using var reader = new Utf8StreamReader(stream) { SkipBom = false };
byte[] bytes = await reader.ReadToEndAsync();


//while (await reader.LoadIntoBufferAsync())
//{
//    while (reader.TryReadLine(out var line))
//    {
//        switch (line.Span[0])
//        {
//            case (byte)'$':
//                Utf8Parser.TryParse(line.Span.Slice(1), out int size, out _);
//                if (!reader.TryReadBlock(size + 2, out var block)) // +2 is \r\n
//                {
//                    // ReadBlockAsync is TryReadBlock + LoadIntoBufferAtLeastAsync
//                    block = await reader.ReadBlockAsync(size + 2);
//                }
//                yield return block.Slice(0, size);
//                break;
//            // and others('+', '-', ':', '*')
//            default:
//                break;
//        }
//    }
//}


//var path = "file1.txt";


//var fs = new FileStream(path, FileMode.Open,FileAccess.Read, FileShare.Read, 0, false);
//var buf = new byte[1024];
//await fs.ReadAsync(buf);

//using var reader = new Utf8StreamReader(path).AsTextReader();



//var str = await reader.ReadToEndAsync();
//Console.WriteLine(str.ToString());

// new StreamReader().ReadBlock(


//var options = new JsonSerializerOptions();
//options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

//var jsonLines = Enumerable.Range(0, 100000)
//    .Select(x => new MyClass { MyProperty = x, MyProperty2 = "あいうえおかきくけこ" })
//    .Select(x => JsonSerializer.Serialize(x, options))
//    .ToArray();

//var utf8Data = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, jsonLines));

//var ms = new MemoryStream(utf8Data);


////using var sr = new System.IO.StreamReader(ms);
////string? line;
////while ((line = await sr.ReadLineAsync()) != null)
////{
////    // JsonSerializer.Deserialize<MyClass>(line);
////}

//using var sr = new Cysharp.IO.Utf8StreamReader(ms);
//ReadOnlyMemory<byte>? line;
//while ((line = await sr.ReadLineAsync()) != null)
//{
//}



//public class MyClass
//{
//    public int MyProperty { get; set; }
//    public string? MyProperty2 { get; set; }
//}

