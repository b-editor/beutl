using System.Reflection;

using BenchmarkDotNet.Running;

using Beutl.GpuPassTargetBenchmarkHarness;

BenchmarkHarnessProvenance.WriteFromEnvironment(typeof(TargetRenderPipelineBenchmarks).Assembly);

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
