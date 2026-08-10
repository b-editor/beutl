using System.Reflection;

using BenchmarkDotNet.Running;
using Beutl.Benchmarks.Rendering;

// Select a benchmark via `-- --filter <pattern>`; no args shows an interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
