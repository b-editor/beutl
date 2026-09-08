namespace Beutl.Evidence;

/// <summary>The <c>analyze-paired</c> entry point the SC-008 runner script calls.</summary>
public static class PairedBenchmarkCommand
{
    public const string Verb = "analyze-paired";

    public const string Usage = """
        Usage: analyze-paired --baseline-a <dir> --feature <dir> --baseline-b <dir> --output <manifest.json>
                              [--primary-case <name>] [--control-case <name>]... [--case <name>]...
                              [--comparison-mode <text>] [--bootstrap-iterations <n>]

        Each run directory must hold exactly one BenchmarkDotNet '*-report-full.json' and, to prove the runs are
        comparable, a 'counters/' directory written by RenderPipelineBenchmarks.
        Exit code 0 means the manifest was written AND its acceptance passed; 2 means it was written and the
        acceptance failed; 1 means the analysis could not run.
        """;

    public static int Run(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? baselineA = null;
        string? feature = null;
        string? baselineB = null;
        string? output = null;
        string primaryCase = "ShaderOpacityShader";
        string comparisonMode = "unspecified";
        int iterations = PairedBenchmarkAnalyzer.DefaultBootstrapIterations;
        var controlCases = new List<string>();
        var cases = new List<string>();

        for (int index = 0; index < arguments.Length; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--baseline-a": baselineA = Next(arguments, ref index); break;
                case "--feature": feature = Next(arguments, ref index); break;
                case "--baseline-b": baselineB = Next(arguments, ref index); break;
                case "--output": output = Next(arguments, ref index); break;
                case "--primary-case": primaryCase = Next(arguments, ref index); break;
                case "--comparison-mode": comparisonMode = Next(arguments, ref index); break;
                case "--control-case": controlCases.Add(Next(arguments, ref index)); break;
                case "--case": cases.Add(Next(arguments, ref index)); break;
                case "--bootstrap-iterations": iterations = int.Parse(Next(arguments, ref index)); break;
                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument '{argument}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        if (baselineA is null || feature is null || baselineB is null || output is null)
        {
            Console.Error.WriteLine("--baseline-a, --feature, --baseline-b and --output are all required.");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (controlCases.Count == 0)
        {
            controlCases.AddRange(
            [
                "MixedSpatialColor",
                "MultipleDrawablesTargetDependencies",
                "NoEffectControl",
                "ShaderOpacityShaderBarrier",
            ]);
        }

        PairedBenchmarkManifest manifest;
        try
        {
            manifest = PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
            {
                BaselineA = PairedBenchmarkRun.FromDirectory("baseline-a", baselineA),
                Feature = PairedBenchmarkRun.FromDirectory("feature", feature),
                BaselineB = PairedBenchmarkRun.FromDirectory("baseline-b", baselineB),
                PrimaryCase = primaryCase,
                ComparisonMode = comparisonMode,
                ControlBarrierCases = controlCases,
                Cases = cases,
                BootstrapIterations = iterations,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            Console.Error.WriteLine($"analyze-paired failed: {ex.Message}");
            return 1;
        }

        string fullOutput = Path.GetFullPath(output);
        if (Path.GetDirectoryName(fullOutput) is { Length: > 0 } parent)
            Directory.CreateDirectory(parent);
        File.WriteAllText(fullOutput, manifest.ToJson());

        Console.WriteLine($"Wrote {fullOutput}");
        Console.WriteLine($"  comparisonMode          : {manifest.ComparisonMode}");
        Console.WriteLine($"  fingerprintsComparable  : {manifest.FingerprintsComparable}");
        if (manifest.FingerprintMismatchReason is { Length: > 0 } reason)
            Console.WriteLine($"    reason                : {reason}");
        Console.WriteLine($"  baselineRepeatStable    : {manifest.BaselineRepeatStable}");
        Console.WriteLine($"  primaryAcceptancePassed : {manifest.PrimaryAcceptancePassed}");
        Console.WriteLine($"  controlBarrierPassed    : {manifest.ControlBarrierAcceptancePassed}");
        Console.WriteLine($"  overallAcceptancePassed : {manifest.OverallAcceptancePassed}");
        foreach ((string name, PairedBenchmarkCaseResult result) in manifest.Cases)
        {
            Console.WriteLine(
                $"  {name,-40} ratio={result.MedianRatio:F4} "
                + $"ci=[{result.ConfidenceInterval95.Lower:F4}, {result.ConfidenceInterval95.Upper:F4}]");
        }

        return manifest.OverallAcceptancePassed ? 0 : 2;
    }

    private static string Next(string[] arguments, ref int index)
    {
        index++;
        return index < arguments.Length
            ? arguments[index]
            : throw new ArgumentException($"Argument '{arguments[index - 1]}' needs a value.");
    }
}
