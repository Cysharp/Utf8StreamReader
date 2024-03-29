#if DEBUG

using Benchmark;

global::System.Console.WriteLine("DEBUG");

var benchmark = new FromMemory();
benchmark.GlobalSetup();
benchmark.Setup();

// await benchmark.PipelineStreamReader2();

#else
using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);

#endif
