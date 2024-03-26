using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

BenchmarkSwitcher.FromAssembly(Assembly.GetEntryAssembly()!).Run(args);

file class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddJob(Job.ShortRun.WithWarmupCount(1).WithIterationCount(1));
    }
}

[Config(typeof(BenchmarkConfig))]
public class ReadLine
{
    byte[] utf8Data = default!;
    MemoryStream ms = default!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new JsonSerializerOptions();
        options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);

        var jsonLines = Enumerable.Range(0, 100000)
            .Select(x => new MyClass { MyProperty = x, MyProperty2 = "あいうえおかきくけこ" })
            .Select(x => JsonSerializer.Serialize(x, options))
            .ToArray();

        utf8Data = Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, jsonLines));
    }

    [IterationSetup]
    public void Setup()
    {
        ms = new MemoryStream(utf8Data);
    }

    [Benchmark]
    public async Task StreamReader()
    {
        using var sr = new System.IO.StreamReader(ms);
        string? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // JsonSerializer.Deserialize<MyClass>(line);
        }
    }

    [Benchmark]
    public async Task Utf8StreamReader()
    {
        using var sr = new Cysharp.IO.Utf8StreamReader(ms);
        ReadOnlyMemory<byte>? line;
        while ((line = await sr.ReadLineAsync()) != null)
        {
            // JsonSerializer.Deserialize<MyClass>(line.Value.Span);
        }
    }
}


public class MyClass
{
    public int MyProperty { get; set; }
    public string? MyProperty2 { get; set; }
}
