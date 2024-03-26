using System.IO.Pipelines;
using System.Text;
using System.Text.Json;



var jsonLines = Enumerable.Range(0, 100000)
    .Select(x => new MyClass { MyProperty = x, MyProperty2 = "あいうえおかきくけこ" })
    .Select(x => JsonSerializer.Serialize(x))
    .ToArray();

var utf8Data = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, jsonLines));

var ms = new MemoryStream(utf8Data);


//using var sr = new System.IO.StreamReader(ms);
//string? line;
//while ((line = await sr.ReadLineAsync()) != null)
//{
//    // JsonSerializer.Deserialize<MyClass>(line);
//}

using var sr = new Cysharp.IO.Utf8StreamReader(ms);
ReadOnlyMemory<byte>? line;
while ((line = await sr.ReadLineAsync()) != null)
{
}



public class MyClass
{
    public int MyProperty { get; set; }
    public string? MyProperty2 { get; set; }
}
