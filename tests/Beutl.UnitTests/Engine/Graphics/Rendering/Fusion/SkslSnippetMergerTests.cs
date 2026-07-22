using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
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

        SkslMergedProgram program = SkslSnippetMerger.Merge(
            [new(first), new(second)]);

        Assert.Multiple(() =>
        {
            Assert.That(program.Source, Does.Contain("uniform float __beutl_s0_gain;")
                .And.Contain("uniform float __beutl_s1_gain;"));
            Assert.That(program.Source, Does.Contain("__beutl_s0_weights[2]")
                .And.Contain("__beutl_s1_weights[2]"));
            Assert.That(program.Source, Does.Contain("__beutl_s0_adjust")
                .And.Contain("__beutl_s1_adjust"));
            Assert.That(program.Source, Does.Contain("color.rrr")
                .And.Not.Contain("color.__beutl"));
            Assert.That(program.Source, Does.Contain("/* gain weights adjust */"),
                "comments are copied verbatim rather than interpreted as identifiers");
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

        SkslMergedProgram program = SkslSnippetMerger.Merge([new(red), new(blue)]);

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
            Assert.That(program.Stages.Select(static stage => stage.StageIndex), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void Merge_ProducesDeterministicBindingLayout()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> lookup = registry.RegisterBorrowed(new object(), "lookup", 3);
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
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White)));
            });

        SkslMergedProgram first = SkslSnippetMerger.Merge([new(description)]);
        SkslMergedProgram second = SkslSnippetMerger.Merge([new(description)]);

        Assert.Multiple(() =>
        {
            Assert.That(
                first.Bindings.Select(static binding =>
                    (binding.StageIndex, binding.Kind, binding.OriginalName, binding.MergedName,
                        binding.Type, binding.ArrayExtent, binding.CoordinateSpace)),
                Is.EqualTo(new[]
                {
                    (0, SkslBindingKind.Uniform, "gain", "__beutl_s0_gain", "float", (int?)null,
                        (ShaderResourceCoordinateSpace?)null),
                    (0, SkslBindingKind.Uniform, "offset", "__beutl_s0_offset", "float2", (int?)null,
                        (ShaderResourceCoordinateSpace?)null),
                    (0, SkslBindingKind.Resource, "lookup", "__beutl_s0_lookup", "shader", (int?)null,
                        (ShaderResourceCoordinateSpace?)ShaderResourceCoordinateSpace.Value),
                }));
            Assert.That(second.Bindings, Is.EqualTo(first.Bindings));
            Assert.That(second.Identity, Is.EqualTo(first.Identity));
        });
    }

    [Test]
    public void MergeAndSplit_SplitsBeforeStageLimitDeterministically()
    {
        var stages = Enumerable.Range(0, 5)
            .Select(_ => new SkslSnippetStage(ShaderDescription.CurrentPixel(Identity)))
            .ToArray();
        SkslBackendBudget budget = Budget(maxStages: 2);

        IReadOnlyList<SkslMergedProgram> first = SkslSnippetMerger.MergeAndSplit(stages, budget);
        IReadOnlyList<SkslMergedProgram> second = SkslSnippetMerger.MergeAndSplit(stages, budget);

        Assert.Multiple(() =>
        {
            Assert.That(first.Select(static program => program.StageCount), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(
                first.Select(static program => program.Stages.Select(static stage => stage.StageIndex).ToArray()),
                Is.EqualTo(new[] { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4 } }));
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
            [new(first), new(second)],
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
        RenderResource<object> firstResource = registry.RegisterBorrowed(new object(), "first", 1);
        RenderResource<object> secondResource = registry.RegisterBorrowed(new object(), "second", 1);
        ShaderDescription first = ResourceShader("lookup", firstResource);
        ShaderDescription second = ResourceShader("lookup", secondResource);
        SkslBackendBudget budget = samplerLimit
            ? Budget(maxSamplers: 2)
            : Budget(maxChildren: 2);

        IReadOnlyList<SkslMergedProgram> programs = SkslSnippetMerger.MergeAndSplit(
            [new(first), new(second)],
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
    public void MergeAndSplit_AccountsForGeneratedSourceLimit()
    {
        SkslSnippetStage first = new(ShaderDescription.CurrentPixel(Identity));
        SkslSnippetStage second = new(ShaderDescription.CurrentPixel(
            "half4 helper(half4 value) { return value; } "
            + "half4 apply(half4 color) { return helper(color); }"));
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
            Assert.That(programs.SelectMany(static program => program.Stages)
                .Select(static stage => stage.StageIndex), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void MergeAndSplit_AccountsForBackendProgramTokenLimit()
    {
        SkslSnippetStage first = new(ShaderDescription.CurrentPixel(Identity));
        SkslSnippetStage second = new(ShaderDescription.CurrentPixel(
            "half4 helper(half4 value) { return value; } "
            + "half4 apply(half4 color) { return helper(color); }"));
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
            Assert.That(programs.SelectMany(static program => program.Stages)
                .Select(static stage => stage.StageIndex), Is.EqualTo(new[] { 0, 1 }));
        });
    }

    [Test]
    public void MergeAndSplit_ReportsSingleStageBackendOverflowForStandaloneFallback()
    {
        SkslSnippetStage stage = new(ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.5f)));

        SkslMergedProgram program = SkslSnippetMerger.MergeAndSplit(
            [stage],
            Budget(maxUniformVectors: 0))[0];

        Assert.Multiple(() =>
        {
            Assert.That(program.RequiresStandaloneExecution, Is.True);
            Assert.That(program.OverflowReasons, Does.Contain(SkslBackendLimit.UniformVectors));
            Assert.That(program.Stages, Has.Count.EqualTo(1),
                "an individually unsupported stage remains visible to the ordinary unfused fallback");
        });
    }

    [Test]
    public void ProgramIdentity_UsesHashOnlyAsBucketAndComparesFullSourceAndLayout()
    {
        SkslMergedProgram first = SkslSnippetMerger.Merge(
            [new(ShaderDescription.CurrentPixel(Identity))]);
        SkslMergedProgram second = SkslSnippetMerger.Merge(
            [new(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }"))]);
        SkslMergedProgramIdentity firstCollision = new(
            first.Source,
            first.Bindings,
            first.Budget,
            bucketHashOverride: 17);
        SkslMergedProgramIdentity secondCollision = new(
            second.Source,
            second.Bindings,
            second.Budget,
            bucketHashOverride: 17);

        Assert.Multiple(() =>
        {
            Assert.That(firstCollision.GetHashCode(), Is.EqualTo(secondCollision.GetHashCode()));
            Assert.That(firstCollision, Is.Not.EqualTo(secondCollision));
            Assert.That(new HashSet<SkslMergedProgramIdentity> { firstCollision, secondCollision }, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void CoverageMetadata_RequiresEveryStageToHaveAnEngineProof()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(Identity);
        SkslMergedProgram homogeneous = SkslSnippetMerger.Merge(
            [
                new(description, SkslCoverageBehavior.PremultipliedCoverageHomogeneous),
                new(description, SkslCoverageBehavior.PremultipliedCoverageHomogeneous),
            ]);
        SkslMergedProgram mixed = SkslSnippetMerger.Merge(
            [
                new(description, SkslCoverageBehavior.PremultipliedCoverageHomogeneous),
                new(description),
            ]);

        Assert.Multiple(() =>
        {
            Assert.That(homogeneous.IsPremultipliedCoverageHomogeneous, Is.True);
            Assert.That(homogeneous.RequiresResolvedCoverage, Is.False);
            Assert.That(mixed.IsPremultipliedCoverageHomogeneous, Is.False);
            Assert.That(mixed.RequiresResolvedCoverage, Is.True);
            Assert.That(mixed.Stages[1].CoverageBehavior,
                Is.EqualTo(SkslCoverageBehavior.RequiresResolvedCoverage));
        });
    }

    [Test]
    public void Stage_RejectsWholeSourceBecauseItIsAnExplicitBarrier()
    {
        ShaderDescription wholeSource = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            RenderBoundsContract.Identity);

        Assert.That(
            () => new SkslSnippetStage(wholeSource),
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
        SkslMergedProgram program = SkslSnippetMerger.Merge([new(first), new(second)]);

        using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(program.Source, out string? error);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Null);
            Assert.That(effect, Is.Not.Null);
        });
    }

    private static ShaderDescription ResourceShader(string name, RenderResource<object> resource)
    {
        return ShaderDescription.CurrentPixel(
            $"uniform shader {name}; half4 apply(half4 color) {{ return {name}.eval(color.rg); }}",
            bindings => bindings.Resource(
                name,
                resource,
                ShaderResourceCoordinateSpace.Value,
                static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White))));
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
