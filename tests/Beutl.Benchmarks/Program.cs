using System.Reflection;

using BenchmarkDotNet.Running;

// Select a benchmark via `-- --filter <pattern>`; no args shows an interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
