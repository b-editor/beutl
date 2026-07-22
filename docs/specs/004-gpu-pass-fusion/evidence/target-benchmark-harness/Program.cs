using System.Reflection;

using BenchmarkDotNet.Running;

using Beutl.GpuPassTargetBenchmarkHarness;

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
