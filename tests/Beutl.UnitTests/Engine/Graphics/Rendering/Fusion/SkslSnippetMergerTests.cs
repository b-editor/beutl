using System.Text;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class SkslSnippetMergerTests
{
    private const string Identity = "half4 apply(half4 color) { return color; }";

    [Test]
    public void Merge_IsolatesTopLevelSymbolsWithoutRenamingMembersOrComments()
    {
        const string source =
            "uniform float gain;\n"
            + "const float weights[2] = float[2](0.25, 0.75);\n"
            + "half3 adjust(half3 value) { return value * gain * weights[0]; }\n"
            + "half4 apply(half4 color) { /* gain weights adjust */ "
            + "return half4(adjust(color.rgb) + color.rrr * weights[1], color.a); }";

        ShaderDescription first = ShaderDescription.CurrentPixel(
            source,
            static bindings => bindings.Uniform("gain", 0.5f));
        ShaderDescription second = ShaderDescription.CurrentPixel(
            source,
            static bindings => bindings.Uniform("gain", 0.75f));

        SkslMergedProgram program = SkslSnippetMerger.Merge([first, second]);
        const string FirstPrefix = "__beutl_s0_";
        const string SecondPrefix = "__beutl_s1_";

        Assert.Multiple(() =>
        {
            Assert.That(program.Source, Does.Contain($"uniform float {FirstPrefix}gain;")
                .And.Contain($"uniform float {SecondPrefix}gain;"));
            Assert.That(program.Source, Does.Contain($"{FirstPrefix}weights[2]")
                .And.Contain($"{SecondPrefix}weights[2]"));
            Assert.That(program.Source, Does.Contain($"{FirstPrefix}adjust")
                .And.Contain($"{SecondPrefix}adjust"));
            Assert.That(program.Source, Does.Contain("color.rrr")
                .And.Not.Contain($"color.{FirstPrefix}")
                .And.Not.Contain($"color.{SecondPrefix}"));
            Assert.That(program.Source, Does.Contain("/* gain weights adjust */"),
                "comments are copied verbatim rather than interpreted as identifiers");
        });
    }

    [Test]
    public void Merge_UsesValidatedPrecisionQualifiedTopLevelSymbols()
    {
        const string source =
            "uniform highp float gain;\n"
            + "const mediump float bias = 0.25;\n"
            + "highp float4 helper(highp float4 value) { return value * gain + bias; }\n"
            + "half4 apply(half4 color) { return half4(helper(float4(color))); }";
        ShaderDescription description = ShaderDescription.CurrentPixel(
            source,
            static bindings => bindings.Uniform("gain", 0.5f));

        SkslMergedProgram program = SkslSnippetMerger.Merge([description, description]);

        Assert.Multiple(() =>
        {
            Assert.That(
                description.Source.TopLevelSymbols,
                Is.EquivalentTo(new[] { "gain", "bias", "helper", "apply" }));
            foreach (string prefix in new[] { "__beutl_s0_", "__beutl_s1_" })
            {
                Assert.That(program.Source, Does.Contain($"uniform highp float {prefix}gain;"));
                Assert.That(program.Source, Does.Contain($"const mediump float {prefix}bias"));
                Assert.That(program.Source, Does.Contain($"highp float4 {prefix}helper("));
                Assert.That(program.Source, Does.Contain($"half4 {prefix}apply("));
            }
        });

        using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? error);
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(effect, Is.Not.Null);
        });
    }

    [Test]
    public void Merge_PreservesAuthoredStageOrder()
    {
        ShaderDescription red = ShaderDescription.CurrentPixel(
            "half4 red(half4 value) { return half4(value.r, 0, 0, value.a); } "
            + "half4 apply(half4 color) { return red(color); }");
        ShaderDescription blue = ShaderDescription.CurrentPixel(
            "half4 blue(half4 value) { return half4(0, 0, value.b, value.a); } "
            + "half4 apply(half4 color) { return blue(color); }");

        SkslMergedProgram program = SkslSnippetMerger.Merge([red, blue]);

        int firstCall = program.Source.IndexOf(
            "__beutl_pixel = __beutl_s0_apply(__beutl_pixel);",
            StringComparison.Ordinal);
        int secondCall = program.Source.IndexOf(
            "__beutl_pixel = __beutl_s1_apply(__beutl_pixel);",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(firstCall, Is.GreaterThanOrEqualTo(0));
            Assert.That(secondCall, Is.GreaterThan(firstCall));
            Assert.That(program.FirstStageIndex, Is.Zero);
            Assert.That(program.StageCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void Merge_ProducesDeterministicBindingLayout()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> lookup = registry.RegisterBorrowed(new object());
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; uniform float2 offset; uniform shader lookup; "
            + "half4 apply(half4 color) { return lookup.eval(color.rg + offset) * gain; }",
            bindings =>
            {
                bindings.Uniform("gain", 0.5f);
                bindings.Uniform("offset", new System.Numerics.Vector2(1, 2));
                bindings.Resource(
                    "lookup",
                    lookup,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)));
            });

        SkslMergedProgram first = SkslSnippetMerger.Merge([description]);
        SkslMergedProgram second = SkslSnippetMerger.Merge([description]);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Bindings.Select(static binding =>
                    (binding.StageIndex, binding.BindingIndex, binding.Kind, binding.MergedName)),
                Is.EqualTo(new[]
                {
                    (0, 0, SkslBindingKind.Uniform, "__beutl_s0_gain"),
                    (0, 1, SkslBindingKind.Uniform, "__beutl_s0_offset"),
                    (0, 0, SkslBindingKind.Resource, "__beutl_s0_lookup"),
                }));
            Assert.That(second.Bindings, Is.EqualTo(first.Bindings));
            Assert.That(second.Identity, Is.EqualTo(first.Identity));
        });
    }

    [Test]
    public void MergeAndSplit_SplitsBeforeStageLimitDeterministically()
    {
        var stages = Enumerable.Range(0, 5)
            .Select(static _ => ShaderDescription.CurrentPixel(Identity))
            .ToArray();
        SkslBackendBudget budget = Budget(maxStages: 2);

        IReadOnlyList<SkslMergedProgram> first = SkslSnippetMerger.MergeAndSplit(stages, budget);
        IReadOnlyList<SkslMergedProgram> second = SkslSnippetMerger.MergeAndSplit(stages, budget);

        Assert.Multiple(() =>
        {
            Assert.That(first.Select(static program => program.StageCount), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(
                first.Select(static program => program.FirstStageIndex),
                Is.EqualTo(new[] { 0, 2, 4 }));
            Assert.That(second.Select(static program => program.Source),
                Is.EqualTo(first.Select(static program => program.Source)));
            Assert.That(first, Has.All.Matches<SkslMergedProgram>(static program => !program.RequiresStandaloneExecution));
        });
    }

    [Test]
    public void MergeAndSplit_AccountsForUniformVectorLimitsIncludingArraysAndMatrices()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(
            "uniform float4 values[2]; half4 apply(half4 color) { return color * values[0]; }",
            static bindings => bindings.Uniform("values", (ReadOnlySpan<float>)[1, 1, 1, 1, 1, 1, 1, 1]));
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "uniform float2x2 matrix; half4 apply(half4 color) { return color * matrix[0][0]; }",
            static bindings => bindings.Uniform("matrix", (ReadOnlySpan<float>)[1, 0, 0, 1]));

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            [first, second],
            Budget(maxUniformVectors: 2));

        Assert.Multiple(() =>
        {
            Assert.That(programs, Has.Count.EqualTo(2));
            Assert.That(programs.Select(static program => program.UniformVectorCount), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(static program => !program.RequiresStandaloneExecution));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MergeAndSplit_AccountsForSamplerAndChildLimits(bool samplerLimit)
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> firstResource = registry.RegisterBorrowed(new object());
        RenderResource<object> secondResource = registry.RegisterBorrowed(new object());
        ShaderDescription first = ResourceShader("lookup", firstResource);
        ShaderDescription second = ResourceShader("lookup", secondResource);
        SkslBackendBudget budget = samplerLimit
            ? Budget(maxSamplers: 2)
            : Budget(maxChildren: 2);

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            [first, second],
            budget);

        Assert.Multiple(() =>
        {
            Assert.That(programs, Has.Count.EqualTo(2));
            Assert.That(programs.Select(static program => program.SamplerCount), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(programs.Select(static program => program.ChildCount), Is.EqualTo(new[] { 2, 2 }));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(static program => !program.RequiresStandaloneExecution));
        });
    }

    [Test]
    public void PortableBudget_ReservesOneSamplerAndChildForTheImplicitSource()
    {
        using var registry = new RenderRequestResourceRegistry();
        SkslBackendBudget budget = SkslBackendBudgetResolver.Portable;
        ShaderDescription[] stages = Enumerable.Range(0, budget.MaxSamplers)
            .Select(index =>
            {
                RenderResource<object> resource = registry.RegisterBorrowed(new object());
                return ResourceShader($"lookup{index}", resource);
            })
            .ToArray();

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            stages,
            budget);

        Assert.Multiple(() =>
        {
            Assert.That(programs.Select(static program => program.StageCount),
                Is.EqualTo(new[] { budget.MaxSamplers - 1, 1 }));
            Assert.That(programs.Select(static program => program.SamplerCount),
                Is.EqualTo(new[] { budget.MaxSamplers, 2 }));
            Assert.That(programs.Select(static program => program.ChildCount),
                Is.EqualTo(new[] { budget.MaxChildren, 2 }));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(
                static program => !program.RequiresStandaloneExecution));
        });
    }

    [TestCase(GRBackend.Vulkan)]
    [TestCase(GRBackend.Metal)]
    public void BackendProfile_SingleStageBeyondSamplerLimitRequiresStandaloneFallback(GRBackend backend)
    {
        using var registry = new RenderRequestResourceRegistry();
        SkslBackendBudget budget = SkslBackendBudgetResolver.Resolve(backend);
        ShaderDescription description = ResourceShader(budget.MaxSamplers, registry);

        SkslMergedProgram program = SkslSnippetMerger.MergeAndSplit(
            [description],
            budget).Single();

        Assert.Multiple(() =>
        {
            Assert.That(program.SamplerCount, Is.EqualTo(budget.MaxSamplers + 1));
            Assert.That(program.RequiresStandaloneExecution, Is.True);
            Assert.That(program.OverflowReasons, Does.Contain(SkslBackendLimit.Samplers));
            Assert.That(program.OverflowReasons.Contains(SkslBackendLimit.Children),
                Is.EqualTo(program.ChildCount > budget.MaxChildren));
            Assert.That(program.StageCount, Is.EqualTo(1),
                "an individually unsupported stage remains visible to the ordinary unfused fallback");
        });
    }

    [Test]
    public void MergeAndSplit_AccountsForGeneratedSourceLimit()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(Identity);
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "half4 helper(half4 value) { return value; } "
            + "half4 apply(half4 color) { return helper(color); }");
        SkslMergedProgram firstOnly = SkslSnippetMerger.Merge([first]);
        SkslMergedProgram secondOnly = SkslSnippetMerger.Merge([second]);
        int limit = Math.Max(firstOnly.SourceByteCount, secondOnly.SourceByteCount);

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            [first, second],
            Budget(maxSourceBytes: limit));

        Assert.Multiple(() =>
        {
            Assert.That(programs, Has.Count.EqualTo(2));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(program => program.SourceByteCount <= limit));
            Assert.That(programs.SelectMany(StageIndices), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void MergeAndSplit_AccountsForBackendProgramTokenLimit()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(Identity);
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "half4 helper(half4 value) { return value; } "
            + "half4 apply(half4 color) { return helper(color); }");
        SkslMergedProgram firstOnly = SkslSnippetMerger.Merge([first]);
        SkslMergedProgram secondOnly = SkslSnippetMerger.Merge([second]);
        int limit = Math.Max(firstOnly.ProgramTokenCount, secondOnly.ProgramTokenCount);

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            [first, second],
            Budget(maxProgramTokens: limit));

        Assert.Multiple(() =>
        {
            Assert.That(programs, Has.Count.EqualTo(2));
            Assert.That(programs, Has.All.Matches<SkslMergedProgram>(program => program.ProgramTokenCount <= limit));
            Assert.That(programs.SelectMany(StageIndices), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void MergeAndSplit_UsesExactUtf8AndTokenMetricsAcrossStageIndexDigitBoundary()
    {
        const string source =
            "// UTF-8 境界コメント\r\n"
            + "const highp float gain = 1.0;\r\n"
            + "half4 apply(half4 color) { return color * gain; }\r\n";
        ShaderDescription description = ShaderDescription.CurrentPixel(source);
        ShaderDescription[] stages = Enumerable.Range(0, 11)
            .Select(_ => description)
            .ToArray();
        SkslMergedProgram merged = SkslSnippetMerger.Merge(stages);

        IReadOnlyList<SkslMergedProgram> atBoundary = SkslSnippetMerger.MergeAndSplit(
            stages,
            Budget(
                maxSourceBytes: merged.SourceByteCount,
                maxProgramTokens: merged.ProgramTokenCount));
        IReadOnlyList<SkslMergedProgram> belowByteBoundary = SkslSnippetMerger.MergeAndSplit(
            stages,
            Budget(
                maxSourceBytes: merged.SourceByteCount - 1,
                maxProgramTokens: merged.ProgramTokenCount));
        IReadOnlyList<SkslMergedProgram> belowTokenBoundary = SkslSnippetMerger.MergeAndSplit(
            stages,
            Budget(
                maxSourceBytes: merged.SourceByteCount,
                maxProgramTokens: merged.ProgramTokenCount - 1));

        Assert.Multiple(() =>
        {
            Assert.That(merged.Source, Does.Contain("__beutl_s9_apply")
                .And.Contain("__beutl_s10_apply"));
            Assert.That(merged.Source, Does.Not.Contain('\r'));
            Assert.That(merged.SourceByteCount, Is.EqualTo(Encoding.UTF8.GetByteCount(merged.Source)));
            Assert.That(merged.ProgramTokenCount, Is.EqualTo(SkslLexer.Tokenize(merged.Source).Count));
            Assert.That(atBoundary, Has.Count.EqualTo(1));
            Assert.That(belowByteBoundary, Has.Count.EqualTo(2));
            Assert.That(belowTokenBoundary, Has.Count.EqualTo(2));
        });

        foreach (SkslMergedProgram program in atBoundary
                     .Concat(belowByteBoundary)
                     .Concat(belowTokenBoundary))
        {
            Assert.Multiple(() =>
            {
                Assert.That(program.SourceByteCount, Is.EqualTo(Encoding.UTF8.GetByteCount(program.Source)));
                Assert.That(program.ProgramTokenCount, Is.EqualTo(SkslLexer.Tokenize(program.Source).Count));
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(
                belowByteBoundary.SelectMany(StageIndices),
                Is.EqualTo(Enumerable.Range(0, 11)));
            Assert.That(
                belowTokenBoundary.SelectMany(StageIndices),
                Is.EqualTo(Enumerable.Range(0, 11)));
        });
    }

    [Test]
    public void MergeAndSplit_ReportsSingleStageBackendOverflowForStandaloneFallback()
    {
        ShaderDescription stage = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.5f));

        SkslMergedProgram program = SkslSnippetMerger.MergeAndSplit(
            [stage],
            Budget(maxUniformVectors: 0))[0];

        Assert.Multiple(() =>
        {
            Assert.That(program.RequiresStandaloneExecution, Is.True);
            Assert.That(program.OverflowReasons, Does.Contain(SkslBackendLimit.UniformVectors));
            Assert.That(program.StageCount, Is.EqualTo(1),
                "an individually unsupported stage remains visible to the ordinary unfused fallback");
        });
    }

    [Test]
    public void Merge_WholeSourceHeadFeedsFollowingCurrentPixelStage()
    {
        ShaderDescription wholeSource = ShaderDescription.WholeSource(
            "uniform shader src; uniform float gain; "
            + "half4 sampleSource(float2 coord) { return src.eval(coord) * gain; } "
            + "half4 main(float2 coord) { return sampleSource(coord); }",
            RenderBoundsContract.Identity,
            static bindings => bindings.Uniform("gain", 0.75f));
        ShaderDescription currentPixel = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.5f));

        SkslMergedProgram program = SkslSnippetMerger.Merge(
            [wholeSource, currentPixel]);

        Assert.Multiple(() =>
        {
            Assert.That(program.Source, Does.Contain("half4 __beutl_head_main(float2 coord)")
                .And.Contain("half4 sampleSource(float2 coord)")
                .And.Contain("half4 __beutl_s1_apply(half4 color)")
                .And.Contain("half4 __beutl_pixel = __beutl_head_main(coord);")
                .And.Contain("__beutl_pixel = __beutl_s1_apply(__beutl_pixel);"));
            Assert.That(program.Source.Split("uniform shader src;", StringSplitOptions.None), Has.Length.EqualTo(2),
                "the WholeSource declaration is the only implicit-source declaration");
            Assert.That(program.Bindings.Select(static binding => binding.MergedName),
                Is.EqualTo(new[] { "gain", "__beutl_s1_gain" }));
            Assert.That(program.SourceByteCount, Is.EqualTo(Encoding.UTF8.GetByteCount(program.Source)));
            Assert.That(program.ProgramTokenCount, Is.EqualTo(SkslLexer.Tokenize(program.Source).Count));
        });

        using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? error);
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(effect, Is.Not.Null);
        });
    }

    [Test]
    public void Merge_RejectsWholeSourceAfterTheHeadPosition()
    {
        ShaderDescription wholeSource = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            RenderBoundsContract.Identity);
        ShaderDescription currentPixel = ShaderDescription.CurrentPixel(Identity);

        Assert.That(
            () => SkslSnippetMerger.Merge(
                [currentPixel, wholeSource]),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Merge_EmitsSkiaCompilableCurrentPixelProgram()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.5f));
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }");
        SkslMergedProgram program = SkslSnippetMerger.Merge([first, second]);

        using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? error);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(effect, Is.Not.Null);
        });
    }

    private static IEnumerable<int> StageIndices(SkslMergedProgram program)
        => Enumerable.Range(program.FirstStageIndex, program.StageCount);

    private static ShaderDescription ResourceShader(string name, RenderResource<object> resource)
    {
        return ShaderDescription.CurrentPixel(
            $"uniform shader {name}; half4 apply(half4 color) {{ return {name}.eval(color.rg); }}",
            bindings => bindings.Resource(
                name,
                resource,
                ShaderResourceCoordinateSpace.Value,
                static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White))));
    }

    private static ShaderDescription ResourceShader(
        int resourceCount,
        RenderRequestResourceRegistry registry)
    {
        string[] names = Enumerable.Range(0, resourceCount)
            .Select(static index => $"lookup{index}")
            .ToArray();
        RenderResource<object>[] resources = names
            .Select(_ => registry.RegisterBorrowed(new object()))
            .ToArray();
        string declarations = string.Join(' ', names.Select(static name => $"uniform shader {name};"));

        return ShaderDescription.CurrentPixel(
            $"{declarations} half4 apply(half4 color) {{ return color; }}",
            bindings =>
            {
                for (int index = 0; index < names.Length; index++)
                {
                    bindings.Resource(
                        names[index],
                        resources[index],
                        ShaderResourceCoordinateSpace.Value,
                        static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)));
                }
            });
    }

    private static SkslBackendBudget Budget(
        int maxStages = int.MaxValue,
        int maxUniformVectors = int.MaxValue,
        int maxSamplers = int.MaxValue,
        int maxChildren = int.MaxValue,
        int maxSourceBytes = int.MaxValue,
        int maxProgramTokens = int.MaxValue)
    {
        return new SkslBackendBudget(
            "unit-test-backend",
            maxStages,
            maxUniformVectors,
            maxSamplers,
            maxChildren,
            maxSourceBytes,
            maxProgramTokens);
    }
}
