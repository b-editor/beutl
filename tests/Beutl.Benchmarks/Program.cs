using System.Reflection;

using BenchmarkDotNet.Running;

// `-- probe <Scene> <Resolution> <frames>` runs the per-frame diagnostics probe instead of BDN.
if (args is ["probe", var scene, var resolution, var frames])
{
    Beutl.Benchmarks.Rendering.EffectPipelineBenchmarks.Probe(scene, resolution, int.Parse(frames));
    return;
}

// Select a benchmark via `-- --filter <pattern>`; no args shows an interactive picker.
BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
