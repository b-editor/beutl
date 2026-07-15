using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Covers the declarative effect-graph compiler, its per-frame resource resolution, and the fused shader
/// executor (feature 004, T029). Uses synthetic test-authored descriptors via the public builder surface;
/// the fused-execution cases run on the raster backend (no Vulkan) so they are GPU-less-CI-safe.
/// </summary>
[TestFixture]
[NonParallelizable]
public class EffectGraphCompilerTests
{
    // A coordinate-invariant snippet that scales premultiplied rgb by a uniform. Distinct uniform name per role
    // keeps the snippet merger's whole-word prefixing unambiguous.
    private const string ScaleSnippet =
        """
        uniform float scaleAmount;
        half4 apply(half4 c) {
            return half4(c.rgb * scaleAmount, c.a);
        }
        """;

    private static ShaderNodeDescriptor Scale(float amount)
        => ShaderNodeDescriptor.Snippet(ScaleSnippet, u => u.Float("scaleAmount", amount));

    // An invariant snippet whose every top-level declaration — a file-scope const, a helper function, and the
    // apply entry — is INDENTED (the closing """ sits at column 0, so the raw literal keeps the leading spaces).
    // Contract A2 requires only `half4 apply(half4 c)`, never column-0 formatting; the merger must still prefix
    // each of these to feN_ so the generated main's `fe0_apply` call resolves.
    private const string IndentedSnippet =
"""
    const half3 tintWeights = half3(0.3, 0.5, 0.2);
    half3 boost(half3 rgb) {
        return rgb * tintWeights;
    }
    half4 apply(half4 c) {
        return half4(boost(c.rgb), c.a);
    }
""";

    // The same math at column 0 — the reference the indented variant must render identically to.
    private const string ColumnZeroSnippet =
"""
const half3 tintWeights = half3(0.3, 0.5, 0.2);
half3 boost(half3 rgb) {
    return rgb * tintWeights;
}
half4 apply(half4 c) {
    return half4(boost(c.rgb), c.a);
}
""";

    // An indented apply whose body calls the `clamp` builtin. The relaxed top-level matchers must NOT mistake the
    // indented `return clamp(...)` body statement for a function definition (which would rename clamp to fe0_clamp
    // and break compilation).
    private const string IndentedBuiltinCallSnippet =
"""
    half4 apply(half4 c) {
        return clamp(c, half4(0.0), half4(1.0));
    }
""";

    private static EffectGraphBuilder NewBuilder(Rect bounds, float workingScale = 1f)
        => new(bounds, outputScale: 1f, workingScale: workingScale, RenderIntent.Delivery);

    private static CompiledPlan Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    // ---- Fusion grouping --------------------------------------------------------------------------------

    [Test]
    public void Compile_MaximalInvariantRun_CollapsesToOneFusedPass()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(Scale(0.9f))
            .Shader(Scale(0.8f))
            .ColorFilter(ColorFilterNodeDescriptor.Create(() => SKColorFilter.CreateLumaColor(), "Luma"))
            .Shader(Scale(1.1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0], Is.TypeOf<FusedShaderPass>());
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(4),
            "all four adjacent invariant nodes fuse into one pass (maximality)");
    }

    [Test]
    public void Compile_ExceedingStageBudget_SplitsIntoConsecutiveFusedPasses()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds);
        int nodeCount = EffectGraphCompiler.MaxFusionStages + 1;
        for (int i = 0; i < nodeCount; i++)
            builder.Shader(Scale(1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes, Has.Length.EqualTo(2), "17 invariant nodes split at the 16-stage budget");
        Assert.That(((FusedShaderPass)plan.Passes[0]).Stages, Has.Length.EqualTo(EffectGraphCompiler.MaxFusionStages));
        Assert.That(((FusedShaderPass)plan.Passes[1]).Stages, Has.Length.EqualTo(1));
    }

    [Test]
    public void Compile_ExceedingUniformBudget_SplitsIntoConsecutiveFusedPasses()
    {
        const int valuesPerStage = 120;
        string source =
            $"uniform lowp float values[{valuesPerStage}];\n"
            + "half4 apply(half4 c) { return c * values[0]; }";
        float[] values = Enumerable.Repeat(1f, valuesPerStage).ToArray();
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.Snippet(source, u => u.FloatArray("values", values)))
            .Shader(ShaderNodeDescriptor.Snippet(source, u => u.FloatArray("values", values)));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes, Has.Length.EqualTo(2));
        Assert.That(plan.Passes, Is.All.TypeOf<FusedShaderPass>());
        Assert.That(plan.Passes.Cast<FusedShaderPass>().Select(p => p.Stages.Length), Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void Compile_HugeUniformArray_SaturatesBudgetWithoutOverflowing()
    {
        const string huge =
            "uniform float4 values[2147483647];\n"
            + "half4 apply(half4 c) { return c * values[0]; }";
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.Snippet(huge))
            .Shader(Scale(1f));

        CompiledPlan? plan = null;
        Assert.DoesNotThrow(() => plan = Compile(builder));

        Assert.That(plan!.Passes, Has.Length.EqualTo(2),
            "an oversized node remains a singleton and cannot overflow fusion-budget bookkeeping");
    }

    [Test]
    public void Compile_TypeSideMatrixArrays_ConsumeFullFusionBudget()
    {
        const string source =
            "uniform mat4[29] transforms;\n"
            + "half4 apply(half4 c) { return c * transforms[0][0][0]; }";
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.Snippet(source))
            .Shader(ShaderNodeDescriptor.Snippet(source));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes.Cast<FusedShaderPass>().Select(pass => pass.Stages.Length),
            Is.EqualTo(new[] { 1, 1 }),
            "each mat4[29] consumes 116 vectors, so two stages exceed the 224-vector floor");
    }

    [Test]
    public void Compile_FusionNeverCrossesASkiaFilter()
    {
        var bounds = new Rect(0, 0, 100, 80);
        EffectGraphBuilder builder = NewBuilder(bounds)
            .Shader(Scale(1f))
            .Blur(new Size(4, 4))
            .Shader(Scale(1f));

        CompiledPlan plan = Compile(builder);

        Assert.That(plan.Passes.Select(p => p.GetType()),
            Is.EqualTo(new[] { typeof(FusedShaderPass), typeof(SkiaFilterPass), typeof(FusedShaderPass) }));
    }

    [Test]
    public void CompilerRepresentation_IsNotPublicAbi()
    {
        Type[] implementationTypes =
        [
            typeof(CompiledPlan), typeof(CompiledPass), typeof(FusedStage), typeof(FusedShaderPass),
            typeof(SkiaFilterPass), typeof(GeometryPass), typeof(ComputePass), typeof(SplitPass),
            typeof(CompositePass), typeof(ResourcePlan), typeof(IntermediateDecl), typeof(StructuralKey),
            typeof(PassBackend),
        ];

        Assert.That(implementationTypes, Is.All.Matches<Type>(type => type.IsNotPublic),
            "compiler schedule/cache records are internal implementation, not plugin ABI");
    }

    // N1: a multi-declarator snippet uniform ('uniform float a, b;') escapes the merger's per-name feN_ prefixing,
    // so it is rejected at snippet construction. Single-declarator and fixed-size array uniforms remain valid.
    [Test]
    public void SkslSnippet_MultiDeclaratorUniform_IsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderNodeDescriptor.Snippet("uniform float a, b;\nhalf4 apply(half4 c){ return half4(c.rgb*a*b, c.a); }"),
                Throws.ArgumentException,
                "a comma-separated uniform declarator list is rejected");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet("uniform float a;\nuniform float b;\nhalf4 apply(half4 c){ return c; }"),
                "single-declarator uniforms are valid");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet("uniform float lut[4];\nhalf4 apply(half4 c){ return c; }"),
                "a fixed-size array uniform is single-declarator and valid");
        });
    }

    // N1b: a multi-declarator top-level const ('const float A = 1.0, B = 2.0;') escapes the merger's per-name feN_
    // prefixing exactly like a multi-declarator uniform (the merger prefixes only the first declarator), so it is
    // rejected at snippet construction. Single-declarator consts, consts with comma-bearing initializers, and
    // function-local multi-declarator consts stay valid.
    [Test]
    public void SkslSnippet_MultiDeclaratorTopLevelConst_IsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderNodeDescriptor.Snippet(
                    "const float A = 1.0, B = 2.0;\nhalf4 apply(half4 c){ return half4(c.rgb*A*B, c.a); }"),
                Throws.ArgumentException,
                "a comma-separated top-level const declarator list is rejected");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "const float A = 1.0;\nconst float B = 2.0;\nhalf4 apply(half4 c){ return c; }"),
                "single-declarator top-level consts are valid");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "const float3 W = float3(0.2126, 0.7152, 0.0722);\nhalf4 apply(half4 c){ return c; }"),
                "commas inside a single const's initializer parens are not declarator separators");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "half4 apply(half4 c){ const float a = 1.0, b = 2.0; return half4(c.rgb*a*b, c.a); }"),
                "a function-local multi-declarator const is block-scoped and left alone");
        });
    }

    // N1c: a top-level struct in a snippet ('struct Foo { ... };') would collide in a fused program because the merger
    // does not rename struct TYPE names (it prefixes only uniforms/consts by feN_), so it is rejected at snippet
    // construction. A function-local struct is block-scoped and left alone; a whole-source shader is never merged, so a
    // top-level struct there stays legal.
    [Test]
    public void SkslSnippet_TopLevelStruct_IsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderNodeDescriptor.Snippet(
                    "struct Foo { float a; };\nhalf4 apply(half4 c){ Foo f; f.a = c.r; return half4(c.rgb*f.a, c.a); }"),
                Throws.ArgumentException,
                "a top-level struct declaration in a snippet is rejected");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "half4 apply(half4 c){ struct Foo { float a; }; Foo f; f.a = c.r; return half4(c.rgb*f.a, c.a); }"),
                "a function-local struct is block-scoped and left alone");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "const float structWeight = 0.5;\nhalf4 apply(half4 c){ return half4(c.rgb*structWeight, c.a); }"),
                "an identifier merely containing 'struct' is not a struct declaration");
            Assert.DoesNotThrow(
                () => SkslSource.WholeSource(
                    "struct Foo { float a; };\nuniform shader src;\nhalf4 main(float2 coord){ return src.eval(coord); }"),
                "a top-level struct in a whole-source shader stays legal (whole-source is never merged)");
        });
    }

    // N1d: the structural checks parse code, not comments — a comment merely MENTIONING a rejected form must not
    // reject the snippet — and a `const`-qualified function parameter is a qualifier, not a top-level const
    // declaration, so its parameter-list comma is not a declarator list.
    [Test]
    public void SkslSnippet_CommentMentionsAndConstParameters_AreAccepted()
    {
        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "// uniform float a, b; is not allowed\nuniform float a;\nhalf4 apply(half4 c){ return half4(c.rgb*a, c.a); }"),
                "a line comment mentioning a multi-declarator uniform is not a declaration");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "/* struct Foo { float a; }; const float A = 1.0, B = 2.0; */\nhalf4 apply(half4 c){ return c; }"),
                "a block comment mentioning rejected forms is not a declaration");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.Snippet(
                    "half4 blend(const half4 a, half4 b){ return a + b; }\nhalf4 apply(half4 c){ return blend(c, c); }"),
                "a const-qualified function parameter's comma is a parameter separator, not a declarator list");
        });
    }

    // N1g: the executor binds the upstream input under the implicit child 'src' before the descriptor's own
    // children, so an extra whole-source child reusing that name would silently replace the effect input the
    // shader's src.eval reads — rejected at describe time.
    [Test]
    public void WholeSource_ChildNamedSrc_IsRejected()
    {
        const string source = "uniform shader src;\nhalf4 main(float2 coord){ return src.eval(coord); }";
        using SKShader extra = SKShader.CreateColor(SKColors.Red);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderNodeDescriptor.WholeSource(
                    source, BoundsContract.FullFrame, children: [new ChildBinding("src", extra)]),
                Throws.ArgumentException,
                "'src' is the reserved implicit source child of a whole-source shader");
            Assert.That(
                () => ShaderNodeDescriptor.WholeSourceInvariant(
                    source, children: [new ChildBinding("src", extra)]),
                Throws.ArgumentException,
                "the invariant whole-source form reserves 'src' identically");
            Assert.DoesNotThrow(
                () => ShaderNodeDescriptor.WholeSource(
                    source, BoundsContract.FullFrame, children: [new ChildBinding("map", extra)]),
                "a differently-named extra child stays valid");
        });
    }

    // N1h: children bind by NAME into the shared runtime builder, so a duplicate name would silently replace the
    // earlier binding — rejected at describe time for both descriptor forms.
    [Test]
    public void DuplicateChildBindingNames_AreRejected()
    {
        const string wholeSource = "uniform shader src;\nhalf4 main(float2 coord){ return src.eval(coord); }";
        using SKShader a = SKShader.CreateColor(SKColors.Red);
        using SKShader b = SKShader.CreateColor(SKColors.Blue);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderNodeDescriptor.WholeSource(
                    wholeSource, BoundsContract.FullFrame,
                    children: [new ChildBinding("map", a), new ChildBinding("map", b)]),
                Throws.ArgumentException,
                "duplicate whole-source child names would silently bind the later shader");
            Assert.That(
                () => ShaderNodeDescriptor.Snippet(
                    "uniform shader lut;\nhalf4 apply(half4 c){ return lut.eval(c.rg * 255.0); }",
                    samplers: [new ChildBinding("lut", a), new ChildBinding("lut", b)]),
                Throws.ArgumentException,
                "duplicate snippet sampler names would silently bind the later shader");
            Assert.That(
                () => ShaderNodeDescriptor.WholeSource(
                    wholeSource, BoundsContract.FullFrame, children: [new ChildBinding("map", a), null!]),
                Throws.ArgumentException,
                "a null child element surfaces as argument validation, not a NullReferenceException");
        });
    }

    // N1f: Build transfers the node list and disposal set to the graph BY REFERENCE; a stashed builder mutating
    // them afterwards would silently change the compiled graph, so post-build mutation throws at the call site.
    [Test]
    public void Builder_UsedAfterBuild_Throws()
    {
        var builder = new EffectGraphBuilder(new Rect(0, 0, 100, 100), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Saturate(0.5f);
        using EffectGraph graph = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.Saturate(0.5f), Throws.InvalidOperationException,
                "appending after Build must throw instead of mutating the built graph's node list");
            Assert.That(() => builder.Track(SKColorFilter.CreateBlendMode(SKColors.Red, SKBlendMode.SrcOver)),
                Throws.InvalidOperationException,
                "tracking after Build must throw instead of mutating the built graph's disposal set");
        });
    }

    // N1e: default(BoundsContract) carries no maps and no structural identity — a growing filter authored with it
    // would render clipped with no diagnostic — so the contract-taking descriptor factories reject it at describe
    // time instead of silently substituting identity maps.
    [Test]
    public void DescriptorFactories_DefaultBoundsContract_IsRejected()
    {
        const string wholeSource = "uniform shader src;\nhalf4 main(float2 coord){ return src.eval(coord); }";

        Assert.Multiple(() =>
        {
            Assert.That(
                () => GeometryNodeDescriptor.Create(static _ => { }, default),
                Throws.ArgumentException,
                "a geometry node's mandatory contract must be an authored one, never the uninitialized default");
            Assert.That(
                () => SkiaFilterNodeDescriptor.Create(static inner => inner, default),
                Throws.ArgumentException,
                "a Skia-filter node's contract must be an authored one, never the uninitialized default");
            Assert.That(
                () => ShaderNodeDescriptor.WholeSource(wholeSource, default),
                Throws.ArgumentException,
                "a whole-source shader's contract must be an authored one, never the uninitialized default");
            Assert.That(
                () => ComputeNodeDescriptor.Create(
                    static _ => { }, 1, default, ComputeFallbackPolicy.Identity),
                Throws.ArgumentException,
                "a compute node must declare whether its kernel is local or full-frame");
            Assert.DoesNotThrow(
                () => GeometryNodeDescriptor.Create(static _ => { }, BoundsContract.FullFrame),
                "the full-frame contract stays the sanctioned non-local execution contract");
        });
    }

    // B1: a snippet whose top-level declarations are indented must still fuse. The merger prefixes the indented
    // const, helper, and apply to fe0_ so the generated main's fe0_apply call resolves and the program compiles.
    [Test]
    public void Merge_IndentedTopLevelDeclarations_ArePrefixedAndCompile()
    {
        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(IndentedSnippet)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Contain("fe0_apply(half4"), "the indented apply entry is prefixed to fe0_apply");
            Assert.That(merged, Does.Contain("fe0_boost"), "the indented helper function is prefixed");
            Assert.That(merged, Does.Contain("fe0_tintWeights"), "the indented file-scope const is prefixed");

            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // B1: the relaxed top-level matchers must not grab an indented body statement. `return clamp(...)` is a call
    // inside the function body, not a file-scope definition, so `clamp` stays a builtin (never renamed to fe0_clamp).
    [Test]
    public void Merge_IndentedBodyBuiltinCall_IsNotMistakenForATopLevelDefinition()
    {
        string merged = SkslSnippetMerger.Merge([SkslSource.Snippet(IndentedBuiltinCallSnippet)]);

        Assert.Multiple(() =>
        {
            Assert.That(merged, Does.Not.Contain("fe0_clamp"),
                "an indented `return clamp(...)` body statement is not a top-level definition; clamp stays a builtin");
            using SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(merged, out string? error);
            Assert.That(error, Is.Null, $"the merged program compiles ({error})");
            Assert.That(effect, Is.Not.Null);
        });
    }

    // B1 end-to-end: an indented snippet fused with a second snippet renders identically to the same math at
    // column 0. Pre-fix the indented apply/helper/const are never prefixed, so the merged program fails to compile
    // and Execute throws.
    [Test]
    public void FusedChain_IndentedSnippet_RendersLikeNonIndentedEquivalent()
    {
        var bounds = new Rect(0, 0, 96, 64);
        ShaderNodeDescriptor indented = ShaderNodeDescriptor.Snippet(IndentedSnippet);
        ShaderNodeDescriptor columnZero = ShaderNodeDescriptor.Snippet(ColumnZeroSnippet);

        using Bitmap indentedResult = RenderChain([indented, Scale(1.1f)], bounds, fuse: true, diagnostics: null, pool: null);
        using Bitmap referenceResult = RenderChain([columnZero, Scale(1.1f)], bounds, fuse: true, diagnostics: null, pool: null);

        double ssim = ImageMetrics.Ssim(referenceResult, indentedResult);
        double mae = ImageMetrics.MeanAbsoluteError(referenceResult, indentedResult);
        Assert.Multiple(() =>
        {
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    // ---- Structural key vs parameters -------------------------------------------------------------------

    [Test]
    public void StructuralKey_ParameterOnlyChange_IsEqual_ButSizesReResolve()
    {
        var bounds = new Rect(0, 0, 200, 150);

        CompiledPlan planSmall = Compile(NewBuilder(bounds).Shader(Scale(0.5f)).Blur(new Size(5, 5)));
        CompiledPlan planLarge = Compile(NewBuilder(bounds).Shader(Scale(0.9f)).Blur(new Size(20, 20)));

        Assert.That(planSmall.Key, Is.EqualTo(planLarge.Key),
            "a uniform value and an animated blur sigma are parameters — the structural key must match so a cache hits");

        FrameResources small = EffectGraphCompiler.ResolveResources(planSmall, Rect.Invalid, workingScale: 1f);
        FrameResources large = EffectGraphCompiler.ResolveResources(planLarge, Rect.Invalid, workingScale: 1f);

        // The blur pass (index 1) inflates by sigma×3, so its resolved buffer grows with sigma — recompiled? no,
        // re-resolved: the plans differ only in per-frame sizes, proving bounds are not part of the key.
        Assert.That(large.Passes[1].Width, Is.GreaterThan(small.Passes[1].Width));
        Assert.That(large.Passes[1].Height, Is.GreaterThan(small.Passes[1].Height));
    }

    [Test]
    public void StructuralKey_DifferentEffectKind_Differs()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan blur = Compile(NewBuilder(bounds).Blur(new Size(5, 5)));
        CompiledPlan dilate = Compile(NewBuilder(bounds).Dilate(5, 5));

        Assert.That(blur.Key, Is.Not.EqualTo(dilate.Key));
    }

    [Test]
    public void StructuralKey_DifferentSkslSources_KeepExactIdentity()
    {
        const string firstSource = "half4 apply(half4 c) { return c; }";
        const string secondSource = "half4 apply(half4 c) { return half4(c.rgb, c.a * 0.5); }";
        var bounds = new Rect(0, 0, 32, 32);
        using EffectGraph first = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.Snippet(firstSource))
            .Build();
        using EffectGraph second = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.Snippet(secondSource))
            .Build();

        Assert.That(StructuralKey.Compute(first), Is.Not.EqualTo(StructuralKey.Compute(second)),
            "the compact precomputed hash must remain only an index; exact SKSL text determines equality");
    }

    [Test]
    public void StructuralKey_TokenContainingSerializedNodeText_DoesNotAliasAnotherGraph()
    {
        var bounds = new Rect(0, 0, 100, 100);
        using EffectGraph oneNode = NewBuilder(bounds)
            .ColorFilter(ColorFilterNodeDescriptor.Create(
                static () => null, "x|0:split:z,2,0"))
            .Build();
        using EffectGraph twoNodes = NewBuilder(bounds)
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, "x"))
            .Split(SplitNodeDescriptor.Static(static _ => { }, 2, "z"))
            .Build();

        Assert.That(StructuralKey.Compute(oneNode), Is.Not.EqualTo(StructuralKey.Compute(twoNodes)),
            "node boundaries and typed token payloads must remain distinct even when a token contains key syntax");
    }

    [Test]
    public void StructuralKey_DoesNotDependOnMutableTokenText()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var token = new MutableStructuralToken("stable");
        using EffectGraph graph = NewBuilder(bounds)
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, token))
            .Build();
        StructuralKey beforeMutation = StructuralKey.Compute(graph);
        int beforeHash = beforeMutation.GetHashCode();

        token.Value = "changed";
        using EffectGraph stableGraph = NewBuilder(bounds)
            .ColorFilter(ColorFilterNodeDescriptor.Create(
                static () => null, new MutableStructuralToken("stable")))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(beforeMutation.GetHashCode(), Is.EqualTo(beforeHash));
            Assert.That(beforeMutation, Is.EqualTo(StructuralKey.Compute(graph)),
                "reference-identity equality stays stable when an unrelated ToString value changes");
            Assert.That(beforeMutation, Is.Not.EqualTo(StructuralKey.Compute(stableGraph)),
                "distinct reference tokens do not alias merely because their text once matched");
        });
    }

    [Test]
    public void StructuralKey_TypeTokensWithSameFullNameFromDifferentAssemblies_Differ()
    {
        static Type CreateType(string assemblyName)
        {
            var name = new System.Reflection.AssemblyName(assemblyName);
            System.Reflection.Emit.AssemblyBuilder assembly
                = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                    name, System.Reflection.Emit.AssemblyBuilderAccess.Run);
            System.Reflection.Emit.ModuleBuilder module = assembly.DefineDynamicModule(assemblyName);
            return module.DefineType("Plugin.CollisionToken").CreateType()!;
        }

        Type first = CreateType("StructuralTokenAssemblyA");
        Type second = CreateType("StructuralTokenAssemblyB");
        using EffectGraph firstGraph = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, first))
            .Build();
        using EffectGraph secondGraph = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, second))
            .Build();

        Assert.That(StructuralKey.Compute(firstGraph), Is.Not.EqualTo(StructuralKey.Compute(secondGraph)),
            "type structural tokens must retain assembly/load-context identity, not only their full name");
    }

    [Test]
    public void StructuralKey_WholeSourceTileMode_Differs()
    {
        const string source = "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }";
        ShaderNodeDescriptor clamp = ShaderNodeDescriptor.WholeSource(
            source, BoundsContract.Identity, srcTileMode: SKShaderTileMode.Clamp);
        ShaderNodeDescriptor repeat = ShaderNodeDescriptor.WholeSource(
            source, BoundsContract.Identity, srcTileMode: SKShaderTileMode.Repeat);
        using EffectGraph clampGraph = NewBuilder(new Rect(0, 0, 10, 10)).Shader(clamp).Build();
        using EffectGraph repeatGraph = NewBuilder(new Rect(0, 0, 10, 10)).Shader(repeat).Build();

        Assert.That(StructuralKey.Compute(clampGraph), Is.Not.EqualTo(StructuralKey.Compute(repeatGraph)),
            "the cached runtime stage embeds SrcTileMode, so changing it must miss the plan cache");
    }

    [Test]
    public void Compile_ChainedStaticSplitsBeyondCumulativeLimit_UsesRuntimeResourceAccounting()
    {
        SplitNodeDescriptor first = SplitNodeDescriptor.Static(static _ => { }, 100, "first-static-split");
        SplitNodeDescriptor second = SplitNodeDescriptor.Static(static _ => { }, 100, "second-static-split");

        CompiledPlan plan = Compile(NewBuilder(new Rect(0, 0, 100, 100)).Split(first).Split(second));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Resources.IsStaticallyBounded, Is.False,
                "the cumulative 10,000-way fan-out must not expand into a static resource declaration array");
            Assert.That(plan.Resources.Intermediates, Is.Empty);
        });
    }

    [Test]
    public void Compile_HugeColorScratchDeclaration_UsesRuntimeResourceAccountingWithoutOverflow()
    {
        ComputeNodeDescriptor compute = ComputeNodeDescriptor.Create(
            static _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
            colorScratchCount: int.MaxValue, structuralToken: "huge-scratch");

        CompiledPlan plan = Compile(NewBuilder(new Rect(0, 0, 100, 100)).Compute(compute));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Resources.IsStaticallyBounded, Is.False);
            Assert.That(plan.Resources.Intermediates, Is.Empty,
                "an unenumerable declaration must use runtime accounting, not overflow into an empty bounded plan");
        });
    }

    [Test]
    public void StructuralKey_UnequalTokensWithSameText_DoNotAlias()
    {
        var first = new SameTextToken(1);
        var second = new SameTextToken(2);
        using EffectGraph firstGraph = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, first))
            .Build();
        using EffectGraph secondGraph = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, second))
            .Build();

        Assert.That(StructuralKey.Compute(firstGraph), Is.Not.EqualTo(StructuralKey.Compute(secondGraph)));
    }

    [Test]
    public void StructuralKey_EqualRecordTokensShareShape_ButDifferentRuntimeTypesDoNot()
    {
        using EffectGraph first = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, new SameTextToken(7)))
            .Build();
        using EffectGraph equal = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, new SameTextToken(7)))
            .Build();
        using EffectGraph integer = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, 7))
            .Build();
        using EffectGraph longInteger = NewBuilder(new Rect(0, 0, 10, 10))
            .ColorFilter(ColorFilterNodeDescriptor.Create(static () => null, 7L))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(StructuralKey.Compute(first), Is.EqualTo(StructuralKey.Compute(equal)));
            Assert.That(StructuralKey.Compute(integer), Is.Not.EqualTo(StructuralKey.Compute(longInteger)));
        });
    }

    private sealed record SameTextToken(int Value)
    {
        public override string ToString() => "same-text";
    }

    private sealed record UnknownDescriptor : EffectNodeDescriptor
    {
        internal override EffectNodeKind Kind => (EffectNodeKind)(-1);

        public override BoundsContract Bounds => BoundsContract.Identity;

        public override bool IsCoordinateInvariant => true;
    }

    [Test]
    public void ParameterBlock_RebindRejectsMismatchedPassShapeInReleaseBuilds()
    {
        var bounds = new Rect(0, 0, 100, 100);
        using EffectGraph cachedGraph = NewBuilder(bounds).Saturate(1.2f).Build();
        using EffectGraph freshGraph = NewBuilder(bounds)
            .Saturate(1.2f)
            .Split(SplitNodeDescriptor.Static(static _ => { }, 2, "shape-mismatch"))
            .Build();
        CompiledPlan cached = EffectGraphCompiler.Compile(cachedGraph, diagnostics: null);

        Assert.That(
            () => ParameterBlock.Extract(freshGraph).RebindOnto(cached),
            Throws.InvalidOperationException.With.Message.Contains("shape"));
    }

    [Test]
    public void ParameterBlock_RebindRejectsDifferentWholeSourceBoundsIdentity()
    {
        const string source = "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }";
        var bounds = new Rect(0, 0, 100, 100);
        BoundsContract firstBounds = BoundsContract.Create(FirstForward, static rect => rect);
        BoundsContract secondBounds = BoundsContract.Create(SecondForward, static rect => rect);
        using EffectGraph cachedGraph = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.WholeSource(source, firstBounds))
            .Build();
        using EffectGraph freshGraph = NewBuilder(bounds)
            .Shader(ShaderNodeDescriptor.WholeSource(source, secondBounds))
            .Build();
        CompiledPlan cached = EffectGraphCompiler.Compile(cachedGraph, diagnostics: null);

        Assert.That(StructuralKey.Compute(cachedGraph), Is.Not.EqualTo(StructuralKey.Compute(freshGraph)),
            "bounds methods are compared by exact structural identity rather than a collision-prone hash");
        Assert.That(
            () => ParameterBlock.Extract(freshGraph).RebindOnto(cached),
            Throws.InvalidOperationException.With.Message.Contains("shape"));

        static Rect FirstForward(Rect rect) => rect.Inflate(1);
        static Rect SecondForward(Rect rect) => rect.Inflate(2);
    }

    [Test]
    public void StructuralKey_UnknownDescriptor_ThrowsInsteadOfProducingPartialIdentity()
    {
        var bounds = new Rect(0, 0, 100, 100);
        using var graph = new EffectGraph(
            [new EffectNode(new UnknownDescriptor(), bounds, bounds, 0, nestedPlanCache: null)],
            bounds, outputScale: 1f, workingScale: 1f, disposables: []);

        Assert.That(
            () => StructuralKey.Compute(graph),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains(nameof(UnknownDescriptor)));
    }

    [Test]
    public void StructuralKey_ComputeDispatchFailureBehaviorDiffers_ProducesDifferentKey()
    {
        var bounds = new Rect(0, 0, 100, 100);

        static ComputeNodeDescriptor Compute(ComputeDispatchFailureBehavior behavior) => ComputeNodeDescriptor.Create(
            static _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
            structuralToken: "dispatch-failure-key", dispatchFailureBehavior: behavior);

        using EffectGraph throwing = NewBuilder(bounds).Compute(Compute(ComputeDispatchFailureBehavior.Throw)).Build();
        using EffectGraph previewIdentity = NewBuilder(bounds)
            .Compute(Compute(ComputeDispatchFailureBehavior.IdentityInPreview)).Build();

        Assert.That(StructuralKey.Compute(throwing), Is.Not.EqualTo(StructuralKey.Compute(previewIdentity)),
            "changing an execution policy must not stale-hit a cached pass with different failure semantics");
    }

    // ---- Resource plan (peak-live) ----------------------------------------------------------------------

    [Test]
    public void ResourcePlan_ComputeDeclaresExactColorScratchMaximum()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var descriptor = ComputeNodeDescriptor.Create(
            static _ => { }, passCount: 4, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
            colorScratchCount: 3, structuralToken: "scratch-shape");
        CompiledPlan plan = Compile(NewBuilder(bounds).Compute(descriptor));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Resources.Intermediates.Count(x => x.Format == TextureFormat.RGBA16Float),
                Is.EqualTo(5), "materialized input + three declared color scratch + final output");
            Assert.That(plan.Resources.Intermediates.Count(x => x.Format == TextureFormat.Depth32Float),
                Is.Zero, "fullscreen compute passes do not declare an unused depth attachment");
            Assert.That(plan.Resources.PeakLiveCount, Is.EqualTo(5));
        });
    }

    [Test]
    public void Execute_ComputeAcquireBeyondDeclaration_ThrowsAndReleasesLeases()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 32, 32);
            var descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    ctx.AcquireColorScratch();
                    ctx.AcquireColorScratch();
                },
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                colorScratchCount: 1,
                structuralToken: "scratch-overrun",
                dispatchFailureBehavior: ComputeDispatchFailureBehavior.IdentityInPreview);
            CompiledPlan plan = Compile(NewBuilder(bounds).Compute(descriptor));
            FrameResources resources = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: 2f, diagnostics: null, pool: pool, renderIntent: RenderIntent.Preview))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("declared color scratch limit"));
                Assert.That(pool.LiveLeaseCount, Is.Zero, "the rejected callback releases every acquired lease");
            });
        });
    }

    [Test]
    public void Execute_ComputeColorScratch_PreparesOutputAndScratchBeforeDispatchWrites()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 32, 32);
            var descriptor = ComputeNodeDescriptor.Create(
                static ctx =>
                {
                    ctx.AcquireColorScratch();
                    ctx.CopySourceToDestination();
                },
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                colorScratchCount: 1,
                structuralToken: "scratch-prepare");
            CompiledPlan plan = Compile(NewBuilder(bounds).Compute(descriptor));
            FrameResources resources = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
            using var pool = new RenderTargetPool();
            int prepares = 0;

            RenderTarget.SetComputeWritePreparedObserverForTest(() => prepares++);
            try
            {
                RenderNodeOperation[] outputs = PlanExecutor.Execute(
                    plan, resources, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
                    maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);
                RenderNodeOperation.DisposeAll(outputs);
            }
            finally
            {
                RenderTarget.SetComputeWritePreparedObserverForTest(null);
            }

            Assert.That(prepares, Is.EqualTo(2),
                "the compute output and its pooled color scratch must both flush pending Skia work before Vulkan writes");
        });
    }

    [Test]
    public void ResourcePlan_LinearChain_PeakLiveIsTwo_IndependentOfLength()
    {
        var bounds = new Rect(0, 0, 100, 100);

        CompiledPlan four = Compile(NewBuilder(bounds)
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2)));
        CompiledPlan eight = Compile(NewBuilder(bounds)
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2))
            .Shader(Scale(1f)).Blur(new Size(2, 2)).Shader(Scale(1f)).Blur(new Size(2, 2)));

        Assert.That(four.Passes, Has.Length.EqualTo(4));
        Assert.That(eight.Passes, Has.Length.EqualTo(8));
        Assert.That(four.Resources.PeakLiveCount, Is.EqualTo(2));
        Assert.That(eight.Resources.PeakLiveCount, Is.EqualTo(2),
            "double-buffer bound: a longer linear chain does not raise peak-live intermediates (FR-007)");

        // Intervals: pass i writes decl i, consumed by pass i+1 (the tail's output is the frame result).
        ImmutableArray<IntermediateDecl> decls = eight.Resources.Intermediates;
        for (int i = 0; i < decls.Length; i++)
        {
            Assert.That(decls[i].FirstUse, Is.EqualTo(i));
            Assert.That(decls[i].LastUse, Is.EqualTo(i < decls.Length - 1 ? i + 1 : i));
        }
    }

    // ---- ROI backward propagation -----------------------------------------------------------------------

    [Test]
    public void ResolveResources_BackwardRoi_InflatesUpstreamFromRequestedRegion()
    {
        var bounds = new Rect(0, 0, 400, 400);
        // Fused (identity) pass, then a Skia pass whose backward inflates the required input by 20 on each side.
        var inflatingSkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner,
            BoundsContract.Create(static r => r, static r => r.Inflate(20)),
            structuralToken: "InflateBackward");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).SkiaFilter(inflatingSkia));

        var requested = new Rect(150, 150, 100, 100);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // Skia pass ROI == requested; fused pass ROI == requested inflated by 20 (clamped to the fused bounds).
        Assert.That(res.Passes[1].OutputRoi, Is.EqualTo(requested));
        Assert.That(res.Passes[0].Width, Is.EqualTo(140), "100 + 2×20 backward inflation");
        Assert.That(res.Passes[0].Height, Is.EqualTo(140));
    }

    [Test]
    public void ResolveResources_DropShadowBackward_CoversSourceAndShadowRegion()
    {
        var bounds = new Rect(0, 0, 400, 400);
        // DropShadow at position (30, 0), sigma 0: output region r needs input r ∪ (r − position).
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .DropShadow(new Point(30, 0), new Size(0, 0), Colors.Black));

        var requested = new Rect(150, 150, 100, 100);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // Upstream ROI = (150..250) ∪ (120..220) on x = (120, 150, 130, 100); identity backward would clip
        // the shadow's source pixels at x ∈ [120, 150).
        Assert.That(res.Passes[0].OutputRoi, Is.EqualTo(new Rect(120, 150, 130, 100)));
        Assert.That(res.Passes[0].Width, Is.EqualTo(130));
        Assert.That(res.Passes[0].Height, Is.EqualTo(100));
    }

    // MatrixConvolution forward bounds: Skia samples input at p + (i, j) − offset over i ∈ [0, kw), so an input
    // region produces an output inflated by (kw−1 − offsetX) on the leading edges and offsetX on the trailing edges
    // (the mirror of its backward map). A sign slip that negated the leading edges deflated the output bounds — the
    // convolved fringe was cropped. Kernel 3×3 (w = h = 2), offset (1, 0): forward inflate = (left 1, top 2, right 1,
    // bottom 0), so a 100×100 input maps to Rect(−1, −2, 102, 102).
    [Test]
    public void MatrixConvolution_ForwardBounds_InflatesLeadingEdgesByKernelExtentMinusOffset()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan plan = Compile(NewBuilder(bounds).MatrixConvolution(
            new PixelSize(3, 3), new float[9], gain: 1f, bias: 0f, new PixelPoint(1, 0),
            GradientSpreadMethod.Pad, convolveAlpha: true));

        Assert.That(plan.Passes, Has.Length.EqualTo(1));
        Assert.That(plan.Passes[0].OutputBounds, Is.EqualTo(new Rect(-1, -2, 102, 102)),
            "forward bounds inflate the leading edges by kernelExtent − offset and the trailing edges by offset");
    }

    [Test]
    public void ResolveResources_TransformBackward_MapsRoiThroughInverseMatrix()
    {
        var bounds = new Rect(0, 0, 400, 400);
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .Transform(Matrix.CreateScale(2f, 2f), BitmapInterpolationMode.Default));

        var requested = new Rect(200, 200, 200, 200);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, requested, workingScale: 1f);

        // The requested 200×200 output region under a 2× scale reads only a 100×100 input region; identity
        // backward would over-request (and mis-place) the upstream crop.
        Assert.That(res.Passes[1].OutputRoi, Is.EqualTo(requested));
        Assert.That(res.Passes[0].OutputRoi, Is.EqualTo(new Rect(100, 100, 100, 100)));
        Assert.That(res.Passes[0].Width, Is.EqualTo(100));
    }

    [Test]
    public void ResolveResources_NonInvertibleTransform_FallsBackToFullUpstreamBounds()
    {
        var bounds = new Rect(0, 0, 400, 400);
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .Transform(Matrix.CreateScale(0f, 0f), BitmapInterpolationMode.Default));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        // A singular matrix has no inverse: backward returns Rect.Invalid and the upstream pass resolves to
        // its full bounds (safe fallback); the degenerate transform output itself is skipped as empty.
        Assert.That(res.Passes[0].Width, Is.EqualTo(400));
        Assert.That(res.Passes[0].Height, Is.EqualTo(400));
        Assert.That(res.Passes[1].SkipEmpty, Is.True);
    }

    [Test]
    public void ResolveResources_FullFramePass_UsesFullInputBoundsWithoutGrowingOutput()
    {
        var bounds = new Rect(0, 0, 300, 200);
        var fullFrame = ShaderNodeDescriptor.WholeSource(
            "uniform shader src; half4 main(float2 coord){ return src.eval(coord); }",
            BoundsContract.FullFrame);
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(fullFrame));

        // A small requested region cannot narrow a full-frame pass: it uses the full input bounds.
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, new Rect(10, 10, 20, 20), workingScale: 1f);

        Assert.That(res.Passes[0].Width, Is.EqualTo(300));
        Assert.That(res.Passes[0].Height, Is.EqualTo(200));
        Assert.That(plan.Passes[0].OutputBounds, Is.EqualTo(bounds),
            "a full-frame requirement must not use an invalid forward bound or grow the logical output");
    }

    // A compute pass reads the whole materialized input at full-frame device coordinates (a non-local GLSL kernel:
    // PixelSort's row/column gather), so it must require the full frame — a downstream deflating pass must NOT
    // ROI-crop it to an offset sub-rect, which would feed truncated width/height push constants (crop-then-sort ≠
    // sort-then-crop). The compute pass's ROI must stay the full input frame under a deflating successor.
    [Test]
    public void ResolveResources_ComputePassBeforeDeflatingPass_KeepsFullFrameRoi()
    {
        var bounds = new Rect(0, 0, 160, 120);
        // A downstream fixed-Clipping analogue: forward deflates the output to an offset sub-rect, backward identity.
        var deflate = SkiaFilterNodeDescriptor.Create(
            static inner => inner,
            BoundsContract.Create(static r => r.Deflate(new Thickness(20, 30, 0, 0)), static r => r),
            structuralToken: "DeflateClip");
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Compute(ComputeNodeDescriptor.Create(
                static _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity, structuralToken: "roi-compute"))
            .SkiaFilter(deflate));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);

        Assert.Multiple(() =>
        {
            Assert.That(res.Passes[0].OutputRoi, Is.EqualTo(bounds),
                "the compute pass resolves to the full input frame, not the deflated sub-rect");
            Assert.That((res.Passes[0].Width, res.Passes[0].Height),
                Is.EqualTo(((int)bounds.Width, (int)bounds.Height)));
        });
    }

    [Test]
    public void ResolveResources_EmptyRoi_FlagsPassSkip()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)));

        // A requested region disjoint from the effect bounds resolves to an empty ROI -> runtime skip.
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, new Rect(500, 500, 50, 50), workingScale: 1f);

        Assert.That(res.Passes[0].SkipEmpty, Is.True);
    }

    // The flag test above only proves the resolver marks the pass; this proves the executor's behavior on the flag:
    // a pass whose resolved OUTPUT is empty (a shrinking pass, e.g. a fully-closed Clipping) must produce nothing,
    // not pass the still-present input through (legacy Clipping.Apply removed the target on an empty intersect).
    [Test]
    public void Execute_ShrinkingPassToEmptyOutput_DropsOutput_NotInputPassThrough()
    {
        var bounds = new Rect(0, 0, 100, 100);
        bool callbackRan = false;
        var closing = GeometryNodeDescriptor.Create(
            _ => callbackRan = true,
            BoundsContract.Create(static _ => Rect.Empty, static r => r),
            structuralToken: "CloseToEmpty");

        RenderNodeOperation[] outputs = Execute(
            NewBuilder(bounds).Geometry(closing), bounds, [MakeInput(bounds)], diagnostics: null, pool: null);

        Assert.That(outputs, Is.Empty,
            "an empty resolved output drops the pass result; returning the input would leak a full-size image");
        Assert.That(callbackRan, Is.False, "the geometry callback never runs for an empty output");
    }

    // The other skip cause: the INPUT op is itself empty. A coordinate-invariant identity pass over nothing is
    // nothing, so the empty op passes straight through unchanged (no crash, no phantom buffer).
    [Test]
    public void Execute_EmptyInputToInvariantPass_PassesEmptyOpThrough()
    {
        var bounds = new Rect(0, 0, 100, 100);
        RenderNodeOperation empty = MakeInput(new Rect(10, 10, 0, 0));

        RenderNodeOperation[] outputs = Execute(
            NewBuilder(bounds).Shader(Scale(1f)), bounds, [empty], diagnostics: null, pool: null);

        Assert.That(outputs, Has.Length.EqualTo(1), "an empty input to an identity pass survives as an empty op");
        Assert.That(outputs[0].Bounds.Width * outputs[0].Bounds.Height, Is.EqualTo(0));
        RenderNodeOperation.DisposeAll(outputs);
    }

    // ---- Working-scale carry + 16384 clamp --------------------------------------------------------------

    [Test]
    public void ResolveResources_NoClampNeeded_KeepsWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var identitySkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner, BoundsContract.Create(static r => r, static r => r), "Identity");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).SkiaFilter(identitySkia));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 2f);

        Assert.That(res.Passes, Has.Length.EqualTo(2));
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(2f));
        Assert.That(res.Passes[1].WorkingScale, Is.EqualTo(2f));
    }

    [Test]
    public void ResolveResources_OversizedChain_ClampsAndCarriesWorkingScaleMonotonically()
    {
        // 10000 px axis at w=2 -> 20000 px buffer, over the 16384 axis budget: the clamp fires and carries.
        var bounds = new Rect(0, 0, 10000, 10000);
        var identitySkia = SkiaFilterNodeDescriptor.Create(
            static inner => inner, BoundsContract.Create(static r => r, static r => r), "Identity");
        CompiledPlan plan = Compile(NewBuilder(bounds).SkiaFilter(identitySkia).Shader(Scale(1f)));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 2f);

        float expected = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, 2f);
        Assert.That(expected, Is.LessThan(2f), "sanity: this chain must trigger the clamp");
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(expected));
        Assert.That(res.Passes[1].WorkingScale, Is.LessThanOrEqualTo(res.Passes[0].WorkingScale),
            "the reduced working scale carries monotonically to downstream passes (legacy Flush parity)");
        Assert.That(res.Passes[0].Width, Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
    }

    // A full-frame Mosaic pass can be re-clamped below the effect-boundary density. Its device-space values must stay
    // late-bound so removing the old whole-input pre-clamp does not freeze uniforms at the describe-time density.
    [Test]
    public void MosaicEffect_AtBufferBudgetEdge_UsesLateBoundDeviceUniforms()
    {
        var bounds = new Rect(0, 0, 20000, 50);
        var effect = new MosaicEffect();
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        var stage = (RuntimeShaderStage)((FusedShaderPass)plan.Passes[0]).Stages[0];

        Assert.Multiple(() =>
        {
            Assert.That(res.Passes[0].Width, Is.EqualTo(RenderNodeContext.MaxBufferDimension));
            Assert.That(res.Passes[0].WorkingScale, Is.LessThan(1f),
                "sanity: the execution pass is re-clamped below its describe-time density");
            Assert.That(stage.Uniforms.Single(x => x.Name == "origin"), Is.TypeOf<DeferredUniform>());
            Assert.That(stage.Uniforms.Single(x => x.Name == "tileSize"), Is.TypeOf<DensityScaledFloat2Uniform>());
            Assert.That(stage.Uniforms.Single(x => x.Name == "resolution"), Is.TypeOf<DeferredUniform>());
        });
    }

    // FR-012/C3.2: the resolver clamps and carries the working scale monotonically along the chain. A
    // coordinate-invariant fused pass must EXECUTE at that carried density, not re-derive w from the effect-boundary
    // working scale (which discards an upstream clamp). Uses a small maxDimension override so the clamp fires on a
    // 100-px chain without allocating a 16384-px buffer — the identical carry code path.
    [Test]
    public void Execute_InvariantFusedPass_UsesCarriedClampedWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 50);
        float carried = res.Passes[0].WorkingScale;
        Assert.That(carried, Is.EqualTo(0.5f).Within(1e-4f), "sanity: maxDimension 50 clamps 100 px at w=1 down to w=0.5");

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].EffectiveScale.Value, Is.EqualTo(carried).Within(1e-4f),
                "the invariant fused pass executes at the carried clamped density, not the boundary working scale (1.0)");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    // FR-011: an invariant pass uses its resolved output ROI instead of expanding back to the input operation's
    // full bounds. A narrow request over a 20000-px input therefore stays at full density and produces only the
    // requested window.
    [Test]
    public void Execute_InvariantFusedPass_UsesResolvedRoiInsteadOfFullOperationBounds()
    {
        var bounds = new Rect(0, 0, 20000, 8);
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, new Rect(0, 0, 100, 8), workingScale: 1f);
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(1f),
            "sanity: the narrowed ROI fits the budget, so the resolver leaves the pass working scale at 1");

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].Bounds, Is.EqualTo(new Rect(0, 0, 100, 8)),
                "the invariant fused pass must preserve the resolver's requested ROI");
            Assert.That(outputs[0].EffectiveScale.Value, Is.EqualTo(1f).Within(1e-4f),
                "the ROI-sized pass must not lose density because the unrelated full input is oversized");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    [Test]
    public void PlanRenderNode_SeedsResolverFromParentRequestedBounds()
    {
        var bounds = new Rect(0, 0, 400, 400);
        var requested = new Rect(50, 60, 100, 100);
        var effect = new Brightness();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = new PlanFilterEffectRenderNode(resource);
        var context = new RenderNodeContext([MakeInput(bounds)], RenderIntent.Delivery, outputScale: 1f)
        {
            RequestedBounds = requested,
        };

        RenderNodeOperation[] outputs = node.Process(context);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].Bounds, Is.EqualTo(requested),
                "the production render-node path must not replace the parent's requested ROI with full bounds");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    // FR-037(b), grow sub-case: a render-time predecessor (a dynamic CustomRenderNode/NestedGraph) can emit an op
    // LARGER than the describe-time input the resolver sized resolution.WorkingScale against. The linear non-invariant
    // pass re-derives outBounds from that actual grown op, so it must re-clamp w against the grown rect too —
    // DeviceBufferSize(grownBounds, resolution.WorkingScale) would otherwise exceed the 16384-px budget. This mirrors
    // the invariant re-clamp test above but drives the shift/grow branch: a 100-px describe frame keeps WorkingScale
    // at 1, and a grown 20000-px op forces the clamp.
    [Test]
    public void Execute_LinearNonInvariantPass_GrownOp_ReClampsWorkingScaleAgainstOpBounds()
    {
        var describeBounds = new Rect(0, 0, 100, 100);
        var passthrough = ShaderNodeDescriptor.WholeSource(
            "uniform shader src; half4 main(float2 coord){ return src.eval(coord); }",
            BoundsContract.FullFrame);
        CompiledPlan plan = Compile(NewBuilder(describeBounds).Shader(passthrough));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(1f),
            "sanity: the 100-px describe frame fits the budget, so the resolver leaves the pass working scale at 1");

        var grownBounds = new Rect(0, 0, 20000, 8);
        float expected = RenderNodeContext.ClampWorkingScaleToBufferBudget(grownBounds, 1f);
        Assert.That(expected, Is.LessThan(1f),
            "sanity: the grown 20000-px op.Bounds must trigger the buffer-budget clamp");

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [MakeInput(grownBounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].EffectiveScale.Value, Is.EqualTo(expected).Within(1e-4f),
                "the grow sub-case re-clamps w against the grown op.Bounds, not the ROI-sized resolution working scale");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    // C3.2, single-op materialization: a linear non-invariant pass re-materializes its op's own pixels, so the
    // output density must min against the op's carried EffectiveScale — a dynamic predecessor emitting a
    // lower-density raster op (0.5) through the shift/grow branch must not be re-raised to the resolved scale (1.0),
    // which would over-allocate and over-tag the output above its actual supply. (The composite fan-in boundary
    // exception is multi-input only.)
    [Test]
    public void Execute_LinearNonInvariantPass_LowerDensityDynamicOp_KeepsCarriedDensity()
    {
        var describeBounds = new Rect(0, 0, 100, 100);
        var passthrough = ShaderNodeDescriptor.WholeSource(
            "uniform shader src; half4 main(float2 coord){ return src.eval(coord); }",
            BoundsContract.FullFrame);
        CompiledPlan plan = Compile(NewBuilder(describeBounds).Shader(passthrough));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(1f), "sanity: the resolver keeps the pass at 1");

        // A shifted low-density op drives the shift/grow branch (escapes the describe-time expectation).
        var shiftedBounds = new Rect(40, 40, 100, 100);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            shiftedBounds,
            canvas => canvas.DrawRectangle(shiftedBounds, Brushes.Resource.White, null),
            shiftedBounds.Contains,
            effectiveScale: EffectiveScale.At(0.5f));

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1));
            Assert.That(outputs[0].EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-4f),
                "a lower-density dynamic op's carried density caps the pass output density (C3.2 min-carry)");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    // B2 (FR-012/C3.2): once an upstream pass clamps w down, no downstream materializing/fan-out pass may raise it
    // again. The carried ceiling is the incoming op's own EffectiveScale (its pixels only exist at that density). A
    // maxDimension=50 resolve clamps the leading invariant pass to w=0.5; the executor's per-op re-clamps run at the
    // default budget (no local clamp on a 100-px op), so pre-fix they re-raise the input density back to the
    // boundary 1.0. Each test observes the density at the materialization boundary and asserts it stays 0.5.

    [Test]
    public void Execute_GeometryInputMaterialization_UsesCarriedClampedWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        float observed = -1f;
        var probe = GeometryNodeDescriptor.Create(
            session => observed = session.Inputs[0].Density.Value,
            BoundsContract.Identity, structuralToken: "b2-geometry-probe");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).Geometry(probe));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 50);
        float carried = res.Passes[1].WorkingScale;
        Assert.That(carried, Is.EqualTo(0.5f).Within(1e-4f), "sanity: maxDimension 50 clamps the chain to w=0.5");

        RenderNodeOperation.DisposeAll(PlanExecutor.Execute(
            plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));

        Assert.That(observed, Is.EqualTo(carried).Within(1e-4f),
            "the geometry input materializes at the carried clamped density, not the boundary working scale (1.0)");
    }

    [Test]
    public void Execute_ComputeCpuFallbackInputMaterialization_UsesCarriedClampedWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        float observed = -1f;
        var probe = ComputeNodeDescriptor.Create(
            dispatch: static _ => { }, passCount: 1, BoundsContract.FullFrame,
            ComputeFallbackPolicy.Cpu(session => observed = session.Inputs[0].Density.Value),
            structuralToken: "b2-compute-probe");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).Compute(probe));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 50);
        float carried = res.Passes[1].WorkingScale;
        Assert.That(carried, Is.EqualTo(0.5f).Within(1e-4f), "sanity: maxDimension 50 clamps the chain to w=0.5");

        PlanExecutor.ForceComputeFallbackForTests();
        try
        {
            RenderNodeOperation.DisposeAll(PlanExecutor.Execute(
                plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }

        Assert.That(observed, Is.EqualTo(carried).Within(1e-4f),
            "the compute CPU-fallback input materializes at the carried clamped density, not the boundary working scale (1.0)");
    }

    [Test]
    public void Execute_ComputeCpuFallback_ExposesTheSameLogicalSourceAndTargetContract()
    {
        var sourceBounds = new Rect(3, 5, 40, 30);
        var targetBounds = new Rect(10, 16, 40, 30);
        BoundsContract bounds = BoundsContract.Create(
            static input => new Rect(input.X + 7, input.Y + 11, input.Width, input.Height),
            static output => new Rect(output.X - 7, output.Y - 11, output.Width, output.Height));
        Rect observedSourceBounds = Rect.Invalid;
        Rect observedTargetBounds = Rect.Invalid;
        float observedSourceScale = float.NaN;
        float observedTargetScale = float.NaN;
        var probe = ComputeNodeDescriptor.Create(
            dispatch: static _ => { }, passCount: 1, bounds,
            ComputeFallbackPolicy.Cpu(session =>
            {
                observedSourceBounds = session.Inputs[0].Bounds;
                observedTargetBounds = session.Bounds;
                observedSourceScale = session.Inputs[0].Density.Value;
                observedTargetScale = session.WorkingScale;
            }),
            structuralToken: "cpu-fallback-logical-contract");
        CompiledPlan plan = Compile(NewBuilder(sourceBounds).Compute(probe));
        FrameResources resources = EffectGraphCompiler.ResolveResources(
            plan, Rect.Invalid, workingScale: 1.5f);

        PlanExecutor.ForceComputeFallbackForTests();
        try
        {
            RenderNodeOperation.DisposeAll(PlanExecutor.Execute(
                plan, resources, [MakeInput(sourceBounds)], outputScale: 1f, workingScale: 1.5f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
                renderIntent: RenderIntent.Delivery));
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }

        Assert.Multiple(() =>
        {
            Assert.That(observedSourceBounds, Is.EqualTo(sourceBounds));
            Assert.That(observedTargetBounds, Is.EqualTo(targetBounds));
            Assert.That(observedSourceScale, Is.EqualTo(1.5f).Within(1e-6f));
            Assert.That(observedTargetScale, Is.EqualTo(1.5f).Within(1e-6f));
        });
    }

    // The compute CPU fallback must honor a render-time SetOutputBounds shrink exactly as the geometry pass does
    // (EmitShrunkGeometry): the emitted operation's bounds match the tightened sub-rect, not the full allocated
    // buffer. Pre-fix the fallback ignored session.ShrunkOutputBounds and emitted the full input bounds.
    [Test]
    public void Execute_ComputeCpuFallbackHonorsSetOutputBounds()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var tight = new Rect(20, 20, 40, 40);
        var probe = ComputeNodeDescriptor.Create(
            dispatch: static _ => { }, passCount: 1, BoundsContract.FullFrame,
            ComputeFallbackPolicy.Cpu(session => session.SetOutputBounds(tight)),
            structuralToken: "cpu-fallback-shrink-probe");
        CompiledPlan plan = Compile(NewBuilder(bounds).Compute(probe));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        PlanExecutor.ForceComputeFallbackForTests();
        RenderNodeOperation[] outputs;
        try
        {
            outputs = PlanExecutor.Execute(
                plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        }
        finally
        {
            PlanExecutor.ResetComputeFallbackForTests();
        }
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(1), "the compute CPU fallback emits one output");
            Assert.That(outputs[0].Bounds, Is.EqualTo(tight),
                "the CPU fallback must honor SetOutputBounds and emit the shrunk sub-rect (pre-fix: the full input bounds)");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    [Test]
    public void Execute_SplitInputMaterialization_UsesCarriedClampedWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        float observed = -1f;
        var split = SplitNodeDescriptor.Static(
            emitter =>
            {
                observed = emitter.Input.Density.Value;
                emitter.Emit(bounds, static _ => { });
            },
            branchCount: 1, structuralToken: "b2-split-probe");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).Split(split));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 50);
        float carried = res.Passes[0].WorkingScale;
        Assert.That(carried, Is.EqualTo(0.5f).Within(1e-4f), "sanity: maxDimension 50 clamps the chain to w=0.5");

        RenderNodeOperation.DisposeAll(PlanExecutor.Execute(
            plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery));

        Assert.That(observed, Is.EqualTo(carried).Within(1e-4f),
            "the split input materializes at the carried clamped density, not the boundary working scale (1.0)");
    }

    // Fused-after-split: a fan-out invariant pass (current.Count > 1) sizes each branch by a local re-clamp, not the
    // resolver's per-pass carry. With two branches carrying w=0.5, the fused pass must keep 0.5, not re-raise to 1.0.
    [Test]
    public void Execute_FusedPassAfterSplit_UsesCarriedClampedWorkingScale()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var split = SplitNodeDescriptor.Static(
            emitter =>
            {
                emitter.Emit(bounds, static _ => { });
                emitter.Emit(bounds, static _ => { });
            },
            branchCount: 2, structuralToken: "b2-fanout-probe");
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).Split(split).Shader(Scale(1f)));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 50);
        Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(0.5f).Within(1e-4f),
            "sanity: maxDimension 50 clamps the chain to w=0.5");

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, res, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(outputs, Has.Length.EqualTo(2), "the two split branches survive the fused pass");
            foreach (RenderNodeOperation output in outputs)
            {
                Assert.That(output.EffectiveScale.Value, Is.EqualTo(0.5f).Within(1e-4f),
                    "the fan-out fused pass executes each branch at the carried clamped density, not the boundary 1.0");
            }
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    // A compute pass starts by materializing through Skia and counts its Skia->Vulkan transition at execution. The
    // compiler flag therefore represents the transition back to that entry backend, not the pass's superficial output
    // backend. In a Skia -> Compute -> Skia chain only the Vulkan->Skia exit is SyncBefore.
    [Test]
    public void Compile_MixedBackendPlan_SetsSyncBeforeAtEachBackendTransition()
    {
        var bounds = new Rect(0, 0, 64, 64);
        CompiledPlan plan = Compile(NewBuilder(bounds)
            .Shader(Scale(1f))
            .Compute(ComputeNodeDescriptor.Create(
                static _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity, structuralToken: "sync-test"))
            .Shader(Scale(1f)));

        Assert.That(plan.Passes.Select(p => p.Backend),
            Is.EqualTo(new[] { PassBackend.Skia, PassBackend.Vulkan, PassBackend.Skia }));
        Assert.Multiple(() =>
        {
            Assert.That(plan.Passes[0].SyncBefore, Is.False, "a leading Skia pass after the virtual Skia input needs no sync");
            Assert.That(plan.Passes[1].SyncBefore, Is.False,
                "compute materializes through the current Skia backend; its Skia->Vulkan transition is internal");
            Assert.That(plan.Passes[2].SyncBefore, Is.True, "the Vulkan->Skia transition syncs");
            Assert.That(plan.Passes.Count(p => p.SyncBefore), Is.EqualTo(1));
        });
    }

    [Test]
    public void Compile_ConsecutiveComputePasses_ReturnToSkiaBeforeTheSecondMaterialization()
    {
        var bounds = new Rect(0, 0, 64, 64);
        static ComputeNodeDescriptor Copy(string token) => ComputeNodeDescriptor.Create(
            static ctx => ctx.CopySourceToDestination(), 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity, structuralToken: token);

        CompiledPlan plan = Compile(NewBuilder(bounds).Compute(Copy("first")).Compute(Copy("second")));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Passes[0].SyncBefore, Is.False,
                "the first compute materializes the virtual Skia input directly");
            Assert.That(plan.Passes[1].SyncBefore, Is.True,
                "the second compute must transition the first Vulkan output back to Skia for materialization");
        });
    }

    [Test]
    public void Execute_DeclaredGeometryReadback_IsScheduledAndCountedOnce()
    {
        var bounds = new Rect(0, 0, 32, 32);
        var descriptor = GeometryNodeDescriptor.Create(
            session =>
            {
                using Bitmap snapshot = session.Inputs[0].Snapshot();
                session.Inputs[0].Draw(session.OpenCanvas());
            },
            BoundsContract.Identity,
            structuralToken: "declared-readback",
            requiresReadback: true);
        CompiledPlan plan = Compile(NewBuilder(bounds).Geometry(descriptor));
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        var diagnostics = new PipelineDiagnostics();

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, resources, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics, pool: null, renderIntent: RenderIntent.Delivery);
        try
        {
            Assert.That(diagnostics.Snapshot().FlushSyncs, Is.EqualTo(1),
                "the executor owns and observes the one declared CPU readback boundary");
        }
        finally
        {
            RenderNodeOperation.DisposeAll(outputs);
        }
    }

    [Test]
    public void Execute_UndeclaredGeometryReadback_IsRejected()
    {
        var bounds = new Rect(0, 0, 32, 32);
        var descriptor = GeometryNodeDescriptor.Create(
            session => session.Inputs[0].Snapshot().Dispose(),
            BoundsContract.Identity,
            structuralToken: "undeclared-readback");
        CompiledPlan plan = Compile(NewBuilder(bounds).Geometry(descriptor));
        FrameResources resources = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, resources, [MakeInput(bounds)], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null, renderIntent: RenderIntent.Delivery))!;

        Assert.That(error.Message, Does.Contain("requiresReadback"));
    }

    // U1: a forward-INFLATED pass (ColorShift-style, output wider than its input) can cross the buffer budget while
    // its input still fits. The resolver clamps only the inflated pass's own buffer and carries that reduced density
    // FORWARD; the fitting upstream pass keeps full density (the monotonic carry is one-directional, C3.2). No
    // divergence — the inflating pass bakes its source at its own output density, so the two densities stay coherent.
    [Test]
    public void ResolveResources_ForwardInflatedPassCrossesBudget_ClampsOnlyItselfAndCarriesForward()
    {
        var bounds = new Rect(0, 0, 100, 100);
        var inflateWidth = ShaderNodeDescriptor.WholeSource(
            "uniform shader src; half4 main(float2 coord){ return src.eval(coord); }",
            BoundsContract.Create(static r => r.Inflate(new Thickness(0, 0, 100, 0)), static r => r));
        CompiledPlan plan = Compile(NewBuilder(bounds).Shader(Scale(1f)).Shader(inflateWidth));

        FrameResources res = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f, maxDimension: 150);

        Assert.Multiple(() =>
        {
            Assert.That(res.Passes[0].WorkingScale, Is.EqualTo(1f), "the fitting 100-px upstream pass keeps full density");
            Assert.That(res.Passes[0].Width, Is.EqualTo(100));
            Assert.That(res.Passes[1].WorkingScale, Is.EqualTo(0.75f).Within(1e-4f),
                "the 200-px inflated output crosses the 150-px budget and clamps its own buffer to 0.75");
            Assert.That(res.Passes[1].Width, Is.EqualTo(150));
        });
    }

    // ---- End-to-end fused execution (raster, GPU-less) --------------------------------------------------

    [Test]
    public void FusedChain_ThreeInvariantSnippets_OneGpuPassOneIntermediate_MatchesUnfused()
    {
        var bounds = new Rect(0, 0, 96, 64);
        ShaderNodeDescriptor[] chain = [Scale(0.85f), Scale(1.15f), Scale(0.7f)];

        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();

        using Bitmap fused = RenderChain(chain, bounds, fuse: true, diagnostics, pool);
        using Bitmap unfused = RenderChain(chain, bounds, fuse: false, diagnostics: null, pool: null);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.GpuPasses, Is.EqualTo(1), "a fused run of 3 invariant snippets is one GPU pass");
            Assert.That(diagnostics.PoolAcquires, Is.LessThanOrEqualTo(1), "at most one intermediate target");
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1), "the 3 snippets merge into one program");

            double ssim = ImageMetrics.Ssim(unfused, fused);
            double mae = ImageMetrics.MeanAbsoluteError(unfused, fused);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    [Test]
    public void FusedChain_ArrayMatrixAndRawUniforms_BindThroughMergedProgram()
    {
        var bounds = new Rect(0, 0, 96, 64);

        // Snippet 1: a float[] uniform and a RawUniform-written scalar (the open binding seam);
        // snippet 2: a float3x3 uniform from a 2D scale matrix. Total per-channel gain:
        // rgb × (0.8 × 0.9 × 0.5) × diag(0.5, 0.5, 1) = rgb × (0.18, 0.18, 0.36).
        var arrayAndRaw = ShaderNodeDescriptor.Snippet(
            """
            uniform float gains[2];
            uniform float extraGain;
            half4 apply(half4 c) {
                return half4(c.rgb * gains[0] * gains[1] * extraGain, c.a);
            }
            """,
            u => u.FloatArray("gains", [0.8f, 0.9f])
                .Raw("extraGain", static (b, name) => b.Uniforms[name] = 0.5f));
        var matrixTint = ShaderNodeDescriptor.Snippet(
            """
            uniform float3x3 tint;
            half4 apply(half4 c) {
                return half4(half3(tint * c.rgb), c.a);
            }
            """,
            u => u.Matrix3x3("tint", Matrix.CreateScale(0.5f, 0.5f)));
        var equivalent = ShaderNodeDescriptor.Snippet(
            """
            uniform float3 mulv;
            half4 apply(half4 c) {
                return half4(c.rgb * mulv, c.a);
            }
            """,
            u => u.Float3("mulv", 0.18f, 0.18f, 0.36f));

        var diagnostics = new PipelineDiagnostics();
        using Bitmap fused = RenderChain([arrayAndRaw, matrixTint], bounds, fuse: true, diagnostics, pool: null);
        using Bitmap expected = RenderChain([equivalent], bounds, fuse: false, diagnostics: null, pool: null);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.ProgramCreations, Is.EqualTo(1),
                "the array/raw/matrix snippets merge into one program (prefixing covers array declarations)");

            double ssim = ImageMetrics.Ssim(expected, fused);
            double mae = ImageMetrics.MeanAbsoluteError(expected, fused);
            Assert.That(ssim, Is.GreaterThanOrEqualTo(GoldenThresholds.ExactSsimMin), $"SSIM {ssim}");
            Assert.That(mae, Is.LessThanOrEqualTo(GoldenThresholds.ExactMaeMax), $"MAE {mae}");
        });
    }

    // Renders a snippet chain over a fixed synthetic input. When fused, the whole chain compiles to one plan;
    // when unfused, each snippet is its own single-node plan run in sequence (the pre-fusion equivalent).
    private static Bitmap RenderChain(
        ShaderNodeDescriptor[] snippets, Rect bounds, bool fuse,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        RenderNodeOperation[] current = [MakeInput(bounds)];
        if (fuse)
        {
            EffectGraphBuilder builder = new(bounds, 1f, 1f, RenderIntent.Delivery);
            foreach (ShaderNodeDescriptor snippet in snippets)
                builder.Shader(snippet);
            current = Execute(builder, bounds, current, diagnostics, pool);
        }
        else
        {
            foreach (ShaderNodeDescriptor snippet in snippets)
            {
                var builder = new EffectGraphBuilder(bounds, 1f, 1f, RenderIntent.Delivery);
                builder.Shader(snippet);
                current = Execute(builder, bounds, current, diagnostics, pool);
            }
        }

        Bitmap result = Rasterize(current[0], bounds);
        current[0].Dispose();
        return result;
    }

    private static RenderNodeOperation[] Execute(
        EffectGraphBuilder builder, Rect bounds, RenderNodeOperation[] inputs,
        PipelineDiagnostics? diagnostics, RenderTargetPool? pool)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics);
        FrameResources res = EffectGraphCompiler.ResolveResources(plan, bounds, workingScale: 1f);
        return PlanExecutor.Execute(
            plan, res, inputs, outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
    }

    private static RenderNodeOperation MakeInput(Rect bounds)
    {
        return RenderNodeOperation.CreateLambda(
            bounds,
            canvas =>
            {
                canvas.DrawRectangle(bounds, Brushes.Resource.White, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height), Brushes.Resource.Red, null);
                canvas.DrawRectangle(new Rect(bounds.X, bounds.Y + bounds.Height / 2, bounds.Width, bounds.Height / 2), Brushes.Resource.Blue, null);
            },
            hitTest: bounds.Contains);
    }

    private static Bitmap Rasterize(RenderNodeOperation op, Rect bounds)
    {
        var size = PixelRect.FromRect(bounds);
        using RenderTarget target = RenderTarget.Create(size.Width, size.Height)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null (raster surface unavailable).");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: bounds.Size))
        {
            canvas.Clear();
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                op.Render(canvas);
            }
        }

        return target.Snapshot();
    }

    private sealed class MutableStructuralToken(string value)
    {
        public string Value { get; set; } = value;

        public override string ToString() => Value;
    }
}
