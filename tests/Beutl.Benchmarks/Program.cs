using System.Reflection;

using BenchmarkDotNet.Running;

using Beutl.Evidence;

// The SC-008 paired analysis reads finished BenchmarkDotNet reports, so it is a verb of this executable rather
// than a benchmark: it must not start the switcher, and its exit code is the acceptance result.
if (args.Length > 0 && args[0] == PairedBenchmarkCommand.Verb)
{
    return PairedBenchmarkCommand.Run(args[1..]);
}

// Select a benchmark via `-- --filter <pattern>`; no args shows an interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
