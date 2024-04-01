#if DEBUG

using Benchmark;

global::System.Console.WriteLine("DEBUG");

var benchmark = new BytesReadToEnd();
benchmark.GlobalSetup();

var s1 = await benchmark.FileReadAllBytesAsync();
var s2 = await benchmark.Utf8StreamReaderReadToEndAsync();

Console.WriteLine(s1.SequenceEqual(s2));

benchmark.GlobalCleanup();

#else
using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);

#endif
