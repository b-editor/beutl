using System.Reflection;

using BenchmarkDotNet.Running;
using Beutl.Benchmarks.Rendering;

// Select a benchmark via `-- --filter <pattern>`; no args shows an interactive picker.
if (args.Length > 0 && string.Equals(args[0], "paired-analyze", StringComparison.Ordinal))
{
    return PairedBenchmarkAnalyzer.Run(args[1..], Console.Out, Console.Error);
}

if (args.Length > 0 && string.Equals(args[0], "paired-self-test", StringComparison.Ordinal))
{
    return PairedBenchmarkAnalyzer.RunSelfTest(Console.Out, Console.Error);
}

if (args.Length > 0 && string.Equals(args[0], "feature-visual-export", StringComparison.Ordinal))
{
    return FeatureVisualEvidenceExporter.Run(args[1..], Console.Out, Console.Error);
}

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
