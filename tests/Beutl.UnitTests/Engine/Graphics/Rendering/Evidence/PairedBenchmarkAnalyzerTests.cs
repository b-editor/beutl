using System.Text.Json;

using Beutl.Evidence;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

/// <summary>
/// Pins the SC-008 analysis method. Every constant these tests assert is quoted from the SC-008 paragraph in
/// <c>docs/specs/004-gpu-pass-fusion/spec.md</c>, so a change to the method has to change both.
/// </summary>
[TestFixture]
public sealed class PairedBenchmarkAnalyzerTests
{
    private const int FastIterations = 2_000;

    [Test]
    public void Constants_MatchTheSpecifiedMethod()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PairedBenchmarkAnalyzer.BaseSeed, Is.EqualTo(20040719));
            Assert.That(PairedBenchmarkAnalyzer.DefaultBootstrapIterations, Is.EqualTo(100_000));
            Assert.That(PairedBenchmarkAnalyzer.ConfidenceLevel, Is.EqualTo(0.95));
            Assert.That(PairedBenchmarkAnalyzer.RequiredSamplesPerRun, Is.EqualTo(15));
            Assert.That(
                PairedBenchmarkAnalyzer.MaximumBaselineRepeatSymmetricToleranceFactor,
                Is.EqualTo(1.20));
        });
    }

    [Test]
    public void Median_UsesTheMeanOfTheTwoMiddleValuesForAnEvenCount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PairedBenchmarkAnalyzer.Median([3, 1, 2]), Is.EqualTo(2));
            Assert.That(PairedBenchmarkAnalyzer.Median([4, 1, 3, 2]), Is.EqualTo(2.5));
            Assert.That(PairedBenchmarkAnalyzer.Median([7]), Is.EqualTo(7));
        });
    }

    [Test]
    public void InterpolatedPercentile_InterpolatesLinearlyBetweenClosestRanks()
    {
        double[] ascending = [10, 20, 30, 40, 50];
        Assert.Multiple(() =>
        {
            Assert.That(PairedBenchmarkAnalyzer.InterpolatedPercentile(ascending, 0), Is.EqualTo(10));
            Assert.That(PairedBenchmarkAnalyzer.InterpolatedPercentile(ascending, 1), Is.EqualTo(50));
            Assert.That(PairedBenchmarkAnalyzer.InterpolatedPercentile(ascending, 0.5), Is.EqualTo(30));
            // rank = 0.125 * 4 = 0.5, so halfway between 10 and 20.
            Assert.That(PairedBenchmarkAnalyzer.InterpolatedPercentile(ascending, 0.125), Is.EqualTo(15));
        });
    }

    [Test]
    public void SymmetricToleranceFactor_TakesTheLargerOfTheUpperBoundAndTheReciprocalLowerBound()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PairedBenchmarkAnalyzer.SymmetricToleranceFactor(
                    new ConfidenceInterval { Lower = 0.5, Upper = 1.1 }),
                Is.EqualTo(2.0).Within(1e-12),
                "1 / 0.5 dominates the upper bound and is not clipped");
            Assert.That(
                PairedBenchmarkAnalyzer.SymmetricToleranceFactor(
                    new ConfidenceInterval { Lower = 0.95, Upper = 1.3 }),
                Is.EqualTo(1.3).Within(1e-12));
        });
    }

    [Test]
    public void DeriveSeed_CombinesTheBaseSeedWithTheCaseNameHash()
    {
        uint first = DeterministicBootstrapRandom.DeriveSeed(PairedBenchmarkAnalyzer.BaseSeed, "SingleShader");
        uint second = DeterministicBootstrapRandom.DeriveSeed(PairedBenchmarkAnalyzer.BaseSeed, "ShaderOpacityShader");
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(second), "each case must resample independently");
            Assert.That(
                first,
                Is.EqualTo(DeterministicBootstrapRandom.DeriveSeed(PairedBenchmarkAnalyzer.BaseSeed, "SingleShader")),
                "seed derivation must be a pure function of the base seed and the case name");
            Assert.That(
                DeterministicBootstrapRandom.Fnv1a32(string.Empty),
                Is.EqualTo(2166136261u),
                "the FNV-1a 32-bit offset basis");
            Assert.That(DeterministicBootstrapRandom.Fnv1a32("a"), Is.EqualTo(0xE40C292Cu));
        });
    }

    [Test]
    public void Bootstrap_IsReproducibleFromItsSeedAndSensitiveToIt()
    {
        // Tightly clustered runs: a resample cannot wander far from the median, so the interval genuinely
        // reflects the 2x difference rather than the within-run spread.
        double[] numerator = Samples(500_000, 15);
        double[] denominator = Samples(1_000_000, 15);

        ConfidenceInterval first = PairedBenchmarkAnalyzer.BootstrapMedianRatioInterval(
            numerator, denominator, 12345u, FastIterations);
        ConfidenceInterval repeat = PairedBenchmarkAnalyzer.BootstrapMedianRatioInterval(
            numerator, denominator, 12345u, FastIterations);

        Assert.Multiple(() =>
        {
            Assert.That(repeat.Lower, Is.EqualTo(first.Lower));
            Assert.That(repeat.Upper, Is.EqualTo(first.Upper));
            Assert.That(first.Lower, Is.LessThanOrEqualTo(first.Upper));
            Assert.That(first.Upper, Is.LessThan(1.0),
                "a numerator that is uniformly half the denominator must land entirely below 1.0");
        });
    }

    [Test]
    public void BootstrapRandom_DrawsADifferentSequenceForADifferentSeed()
    {
        int[] first = Draw(seed: 1);
        int[] repeat = Draw(seed: 1);
        int[] other = Draw(seed: 2);
        Assert.Multiple(() =>
        {
            Assert.That(repeat, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
            Assert.That(first, Has.All.InRange(0, 14));
        });

        static int[] Draw(uint seed)
        {
            var random = new DeterministicBootstrapRandom(seed);
            return [.. Enumerable.Range(0, 32).Select(_ => random.NextIndex(15))];
        }
    }

    [Test]
    public void Bootstrap_OnIdenticalSamplesCollapsesToOne()
    {
        double[] samples = [.. Enumerable.Repeat(1_000_000.0, 15)];
        ConfidenceInterval interval = PairedBenchmarkAnalyzer.BootstrapMedianRatioInterval(
            samples, samples, 1u, FastIterations);
        Assert.Multiple(() =>
        {
            Assert.That(interval.Lower, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(interval.Upper, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(PairedBenchmarkAnalyzer.IntervalContainsOne(interval), Is.True);
        });
    }

    [Test]
    public void Analyze_AcceptsAFasterFeatureWithAStableBaseline()
    {
        PairedBenchmarkManifest manifest = Analyze(
            baselineA: Samples(1_000_000, 15),
            feature: Samples(500_000, 15),
            baselineB: Samples(1_000_000, 15));

        PairedBenchmarkCaseResult primary = manifest.Cases["ShaderOpacityShader"];
        Assert.Multiple(() =>
        {
            Assert.That(manifest.BaselineRepeatStable, Is.True);
            Assert.That(manifest.PrimaryAcceptancePassed, Is.True);
            Assert.That(manifest.ControlBarrierAcceptancePassed, Is.True);
            Assert.That(manifest.FingerprintsComparable, Is.True);
            Assert.That(manifest.OverallAcceptancePassed, Is.True);
            Assert.That(primary.MedianRatio, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(primary.ConfidenceIntervalEntirelyBelowOne, Is.True);
            Assert.That(primary.BaselineSampleCount, Is.EqualTo(30), "baseline A and B are pooled");
            Assert.That(primary.BaselineFirstRunSampleCount, Is.EqualTo(15));
            Assert.That(primary.BaselineRepeatSampleCount, Is.EqualTo(15));
            Assert.That(primary.FeatureSampleCount, Is.EqualTo(15));
        });
    }

    [Test]
    public void Analyze_RefusesToAcceptWhenTheBaselineItselfDrifted()
    {
        // Baseline B is 2x baseline A: the machine moved between the two baseline runs, so nothing measured
        // between them can be attributed to the feature.
        PairedBenchmarkManifest manifest = Analyze(
            baselineA: Samples(1_000_000, 15),
            feature: Samples(400_000, 15),
            baselineB: Samples(2_000_000, 15));

        Assert.Multiple(() =>
        {
            Assert.That(manifest.BaselineRepeatStable, Is.False);
            Assert.That(manifest.PrimaryAcceptancePassed, Is.False);
            Assert.That(manifest.OverallAcceptancePassed, Is.False);
            Assert.That(
                manifest.Cases["ShaderOpacityShader"].BaselineRepeatMedianRatio,
                Is.EqualTo(2.0).Within(1e-9));
            Assert.That(manifest.Cases["ShaderOpacityShader"].BaselineRepeatStable, Is.False);
        });
    }

    [Test]
    public void Analyze_FailsAControlCaseThatRegressedBeyondItsOwnRepeatTolerance()
    {
        var manifest = PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
        {
            BaselineA = Run("baseline-a", new()
            {
                ["ShaderOpacityShader"] = Samples(1_000_000, 15),
                ["NoEffectControl"] = Samples(1_000_000, 15),
            }),
            Feature = Run("feature", new()
            {
                ["ShaderOpacityShader"] = Samples(500_000, 15),
                ["NoEffectControl"] = Samples(3_000_000, 15),
            }),
            BaselineB = Run("baseline-b", new()
            {
                ["ShaderOpacityShader"] = Samples(1_000_000, 15),
                ["NoEffectControl"] = Samples(1_000_000, 15),
            }),
            PrimaryCase = "ShaderOpacityShader",
            ControlBarrierCases = ["NoEffectControl"],
            ComparisonMode = "unit-test",
            BootstrapIterations = FastIterations,
        });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.BaselineRepeatStable, Is.True);
            Assert.That(manifest.PrimaryAcceptancePassed, Is.True);
            Assert.That(manifest.ControlBarrierAcceptancePassed, Is.False);
            Assert.That(manifest.OverallAcceptancePassed, Is.False);
            Assert.That(manifest.Cases["NoEffectControl"].IsControlOrBarrierGateCase, Is.True);
            Assert.That(manifest.Cases["NoEffectControl"].NoRegressionWithinBaselineRepeatTolerance, Is.False);
        });
    }

    [Test]
    public void Analyze_FailsWhenADeclaredControlCaseWasNeverMeasured()
    {
        var manifest = PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
        {
            BaselineA = Run("baseline-a", Single(Samples(1_000_000, 15))),
            Feature = Run("feature", Single(Samples(500_000, 15))),
            BaselineB = Run("baseline-b", Single(Samples(1_000_000, 15))),
            PrimaryCase = "ShaderOpacityShader",
            ControlBarrierCases = ["NoEffectControl"],
            ComparisonMode = "unit-test",
            BootstrapIterations = FastIterations,
        });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.MissingControlBarrierCases, Is.EqualTo(new[] { "NoEffectControl" }));
            Assert.That(
                manifest.ControlBarrierAcceptancePassed,
                Is.False,
                "a control workload the run never measured proves nothing");
            Assert.That(manifest.OverallAcceptancePassed, Is.False);
        });
    }

    [Test]
    public void Analyze_RejectsARunThatDidNotSupplyFifteenSamples()
    {
        Assert.That(
            () => Analyze(
                baselineA: Samples(1_000_000, 14),
                feature: Samples(500_000, 15),
                baselineB: Samples(1_000_000, 15)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("requires exactly 15"));
    }

    [Test]
    public void Analyze_RejectsANonFiniteSample()
    {
        double[] broken = [.. Samples(1_000_000, 15)];
        broken[3] = double.NaN;
        Assert.That(
            () => Analyze(baselineA: broken, feature: Samples(500_000, 15), baselineB: Samples(1_000_000, 15)),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("non-finite"));
    }

    [Test]
    public void Analyze_RefusesToCallTwoRunsComparableWithoutFingerprints()
    {
        var manifest = PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
        {
            BaselineA = RunWithoutFingerprint("baseline-a", Single(Samples(1_000_000, 15))),
            Feature = RunWithoutFingerprint("feature", Single(Samples(500_000, 15))),
            BaselineB = RunWithoutFingerprint("baseline-b", Single(Samples(1_000_000, 15))),
            PrimaryCase = "ShaderOpacityShader",
            ComparisonMode = "unit-test",
            BootstrapIterations = FastIterations,
        });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.PrimaryAcceptancePassed, Is.True, "the measurement itself still stands");
            Assert.That(manifest.FingerprintsComparable, Is.False);
            Assert.That(manifest.FingerprintMismatchReason, Does.Contain("fingerprint"));
            Assert.That(
                manifest.OverallAcceptancePassed,
                Is.False,
                "a result that cannot be shown to come from one machine is not an accepted result");
        });
    }

    [Test]
    public void Analyze_RefusesTwoRunsMeasuredOnDifferentDevices()
    {
        RenderEvidenceFingerprint here = TestFingerprint();
        RenderEvidenceFingerprint elsewhere = TestFingerprint() with { VulkanDeviceName = "Another GPU" };
        var manifest = PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
        {
            BaselineA = Run("baseline-a", Single(Samples(1_000_000, 15)), here),
            Feature = Run("feature", Single(Samples(500_000, 15)), elsewhere),
            BaselineB = Run("baseline-b", Single(Samples(1_000_000, 15)), here),
            PrimaryCase = "ShaderOpacityShader",
            ComparisonMode = "unit-test",
            BootstrapIterations = FastIterations,
        });

        Assert.Multiple(() =>
        {
            Assert.That(manifest.FingerprintsComparable, Is.False);
            Assert.That(manifest.FingerprintMismatchReason, Does.Contain("different conditions"));
            Assert.That(manifest.OverallAcceptancePassed, Is.False);
        });
    }

    [Test]
    public void Manifest_SerializesEveryFieldTheCriterionRequires()
    {
        PairedBenchmarkManifest manifest = Analyze(
            baselineA: Samples(1_000_000, 15),
            feature: Samples(500_000, 15),
            baselineB: Samples(1_000_000, 15));

        using JsonDocument document = JsonDocument.Parse(manifest.ToJson());
        JsonElement root = document.RootElement;
        JsonElement primary = root.GetProperty("cases").GetProperty("ShaderOpacityShader");
        Assert.Multiple(() =>
        {
            foreach (string name in new[]
                     {
                         "baselineRepeatStabilityRule",
                         "maximumBaselineRepeatSymmetricToleranceFactor",
                         "baselineRepeatStable",
                         "bootstrapSeed",
                         "bootstrapIterations",
                         "confidenceLevel",
                         "comparisonMode",
                         "environmentFingerprint",
                     })
            {
                Assert.That(root.TryGetProperty(name, out _), Is.True, $"manifest must record '{name}'");
            }

            foreach (string name in new[]
                     {
                         "baselineSampleCount",
                         "baselineFirstRunSampleCount",
                         "baselineRepeatSampleCount",
                         "baselineMedianNanoseconds",
                         "baselineFirstRunMedianNanoseconds",
                         "baselineRepeatMedianNanoseconds",
                         "baselineRepeatMedianRatio",
                         "baselineRepeatConfidenceInterval95",
                         "baselineRepeatConfidenceContainsOne",
                         "baselineRepeatSymmetricToleranceFactor",
                         "baselineRepeatSymmetricToleranceInterval",
                         "baselineRepeatStable",
                     })
            {
                Assert.That(primary.TryGetProperty(name, out _), Is.True, $"case must record '{name}'");
            }

            Assert.That(root.GetProperty("bootstrapSeed").GetInt32(), Is.EqualTo(20040719));
            Assert.That(root.GetProperty("primaryAcceptanceRule").GetString(),
                Is.EqualTo(PairedBenchmarkAnalyzer.PrimaryAcceptanceRule));
        });
    }

    [Test]
    public void ReadSamples_TakesOriginalValuesKeyedByTheCaseNameParameter()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"paired-{Guid.NewGuid():N}-report-full.json");
        File.WriteAllText(path, """
            {
              "Benchmarks": [
                {
                  "Method": "RenderCompleteTargetRequest",
                  "Parameters": "CaseName=SingleShader",
                  "Statistics": { "OriginalValues": [ 3, 1, 2 ], "Median": 999 }
                }
              ]
            }
            """);
        try
        {
            IReadOnlyDictionary<string, double[]> samples = PairedBenchmarkRun.ReadSamples(path);
            Assert.Multiple(() =>
            {
                Assert.That(samples.Keys, Is.EquivalentTo(new[] { "SingleShader" }));
                Assert.That(samples["SingleShader"], Is.EqualTo(new double[] { 3, 1, 2 }),
                    "the raw values are consumed rather than BenchmarkDotNet's outlier-classified summary");
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Samples spread symmetrically around <paramref name="value"/>, so their median is exactly it.</summary>
    /// <summary>
    /// Regression: BenchmarkDotNet abbreviates a long parameter into the display form
    /// <c>Multi(...)ncies [35]</c>, so keying the analysis by `Parameters` silently invents case names no scene
    /// has and the required-case lookup then fails on a real corpus.
    /// </summary>
    [Test]
    public void ReadSamples_PrefersTheUnabbreviatedFullNameOverTheAbbreviatedParameters()
    {
        string path = WriteReport("""
            {
              "Benchmarks": [
                {
                  "Method": "RenderCompleteTargetRequest",
                  "Parameters": "CaseName=Multi(...)ncies [35]",
                  "FullName": "Beutl.Benchmarks.Rendering.RenderPipelineBenchmarks.RenderCompleteTargetRequest(CaseName: \"MultipleDrawablesTargetDependencies\")",
                  "Statistics": { "OriginalValues": [ 1, 2, 3 ] }
                }
              ]
            }
            """);
        try
        {
            Assert.That(
                PairedBenchmarkRun.ReadSamples(path).Keys,
                Is.EquivalentTo(new[] { "MultipleDrawablesTargetDependencies" }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadSamples_RefusesAnAbbreviatedCaseNameItCannotResolve()
    {
        string path = WriteReport("""
            {
              "Benchmarks": [
                {
                  "Method": "RenderCompleteTargetRequest",
                  "Parameters": "CaseName=Multi(...)ncies [35]",
                  "Statistics": { "OriginalValues": [ 1, 2, 3 ] }
                }
              ]
            }
            """);
        try
        {
            Assert.That(
                () => PairedBenchmarkRun.ReadSamples(path),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("abbreviated"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteReport(string json)
    {
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            $"paired-{Guid.NewGuid():N}-report-full.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static double[] Samples(double value, int count)
        => [.. Enumerable.Range(0, count).Select(index => value + index - ((count - 1) / 2.0))];

    private static Dictionary<string, double[]> Single(double[] samples)
        => new(StringComparer.Ordinal) { ["ShaderOpacityShader"] = samples };

    private static PairedBenchmarkManifest Analyze(double[] baselineA, double[] feature, double[] baselineB)
        => PairedBenchmarkAnalyzer.Analyze(new PairedBenchmarkAnalysisRequest
        {
            BaselineA = Run("baseline-a", Single(baselineA)),
            Feature = Run("feature", Single(feature)),
            BaselineB = Run("baseline-b", Single(baselineB)),
            PrimaryCase = "ShaderOpacityShader",
            ComparisonMode = "unit-test",
            BootstrapIterations = FastIterations,
        });

    private static PairedBenchmarkRun Run(
        string label,
        Dictionary<string, double[]> samples,
        RenderEvidenceFingerprint? fingerprint = null)
        => new()
        {
            Label = label,
            Samples = samples,
            Fingerprint = fingerprint ?? TestFingerprint(),
        };

    private static PairedBenchmarkRun RunWithoutFingerprint(string label, Dictionary<string, double[]> samples)
        => new() { Label = label, Samples = samples, Fingerprint = null };

    internal static RenderEvidenceFingerprint TestFingerprint() => new()
    {
        OsDescription = "Test OS 1.0",
        OsVersion = "Unix 1.0",
        OsArchitecture = "Arm64",
        ProcessArchitecture = "Arm64",
        RuntimeIdentifier = "osx-arm64",
        FrameworkDescription = ".NET 10.0.0",
        EnvironmentVersion = "10.0.0",
        BuildConfiguration = "Release",
        RendererBackend = "Vulkan",
        SkiaBackend = "Vulkan",
        DeviceSelection = "automatic-no-preferred-device",
        MaxAttachmentDimension = 16384,
        VulkanApiVersion = "1.2.0",
        VulkanVendorId = "0x00001002",
        VulkanDeviceId = "0x00000001",
        VulkanDeviceType = "DiscreteGpu",
        VulkanDeviceName = "Test GPU",
        VulkanDeviceUuid = "00000000000000000000000000000001",
        VulkanDriverUuid = "00000000000000000000000000000002",
        VulkanDriverId = "DriverIDTest",
        VulkanDriverName = "TestDriver",
        VulkanDriverInfo = "1.0.0",
        VulkanDriverVersionRaw = "1",
        VulkanDriverVersionDecoded = "0.0.1",
        VulkanEnabledExtensions = ["VK_KHR_surface"],
        SkiaSharpManagedVersion = "3.0.0",
        SkiaSharpNativeVersion = "119.0",
        SilkNetVulkanVersion = "2.23.0",
        BeutlEngineAssemblyVersion = "2.99.99+0123456789abcdef0123456789abcdef01234567",
        BeutlEngineSourceRevision = "0123456789abcdef0123456789abcdef01234567",
    };
}
