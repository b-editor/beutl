using System.Reflection;

using BenchmarkDotNet.Running;

// Select a benchmark with `-- --filter <pattern>`, e.g. `--filter *DispatcherBenchmark*`.
// With no arguments, BenchmarkDotNet shows an interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
