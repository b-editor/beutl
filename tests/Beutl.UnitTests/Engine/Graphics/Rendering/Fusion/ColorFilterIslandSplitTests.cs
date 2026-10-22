using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class ColorFilterIslandSplitTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [TestCaseSource(nameof(ColorStagesAfterBlur))]
    public void BlurThenColorStage_CompilesAfterTheSingleTargetSegment(Func<FilterEffect> colorStage)
    {
        using CompiledRenderRequest compiled = Compile(
            [new Blur { Sigma = { Value = new(2, 2) } }, colorStage()]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(3));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, false, true }));
            Assert.That(SemanticKinds(compiled), Is.EqualTo(new[]
            {
                RenderFragmentKind.OpaqueSource,
                RenderFragmentKind.FilterEffectSegment,
            }));

            // The blur segment is what forces the split; it runs no custom effect item.
            Assert.That(
                compiled.Graph.Fragments
                    .Select(static fragment => fragment.Payload)
                    .OfType<FilterEffectSegmentRenderFragmentPayload>()
                    .Single()
                    .HasImperativeItem,
                Is.False);

            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().StageFragmentIndices, Has.Length.EqualTo(1));
        }
    }

    private static IEnumerable<TestCaseData> ColorStagesAfterBlur()
    {
        yield return new TestCaseData((Func<FilterEffect>)(() => new Saturate { Amount = { Value = 50f } }))
            .SetName("BlurThenSaturate_CompilesAfterTheSingleTargetSegment");
        yield return new TestCaseData((Func<FilterEffect>)(() => new Brightness { Amount = { Value = 50f } }))
            .SetName("BlurThenBrightness_CompilesAfterTheSingleTargetSegment");
    }

    [Test]
    public void BlurThenBrightness_ExecutesTheShaderRunFromOneSegmentTarget()
    {
        using Bitmap disabled = RenderBlurThenBrightness(FusionMode.Disabled, out _);
        using Bitmap enabled = RenderBlurThenBrightness(
            FusionMode.Enabled,
            out RenderExecutionStatistics statistics);

        Assert.Multiple(() =>
        {
            Assert.That(enabled.GetPixelSpan().SequenceEqual(disabled.GetPixelSpan()), Is.True);
            Assert.That(enabled.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
            Assert.That(statistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(statistics.ShaderStageExecutions, Is.EqualTo(1));
        });
    }

    [Test]
    public void BrightnessThenGamma_FusesIntoASingleTwoStageShaderRun()
    {
        using CompiledRenderRequest compiled = Compile(
            [new Brightness { Amount = { Value = 50f } }, new Gamma { Amount = { Value = 220f } }]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(2));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, true }));

            // Only the source is materialized; nothing separates the two color stages.
            Assert.That(SemanticKinds(compiled), Is.EqualTo(new[] { RenderFragmentKind.OpaqueSource }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().StageFragmentIndices, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public void CurvesThenBrightnessThenGamma_FusesWithinThePortableBudget()
    {
        using CompiledRenderRequest compiled = Compile(
            [
                new Curves(),
                new Brightness { Amount = { Value = 50f } },
                new Gamma { Amount = { Value = 220f } },
            ],
            SkslBackendBudgetResolver.Portable);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(2));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, true }));

            // Only the source is materialized; no backend limit splits the three shader stages.
            Assert.That(SemanticKinds(compiled), Is.EqualTo(new[] { RenderFragmentKind.OpaqueSource }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().StageFragmentIndices, Has.Length.EqualTo(3));
        }
    }

    [Test]
    public void MosaicGammaOpacityInvert_CompileAsOneWholeSourceHeadedRun()
    {
        using CompiledRenderRequest compiled = CompileMosaicGammaOpacityInvert();

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands.Select(static island => island.ShaderRun is not null),
                Is.EqualTo(new[] { false, true }));
            Assert.That(SemanticKinds(compiled), Is.EqualTo(new[] { RenderFragmentKind.OpaqueSource }));
            Assert.That(run.StageFragmentIndices.Select(index => compiled.Graph.Fragments[index].Kind),
                Is.EqualTo(new[]
                {
                    RenderFragmentKind.Shader,
                    RenderFragmentKind.Shader,
                    RenderFragmentKind.Opacity,
                    RenderFragmentKind.Shader,
                }));
            Assert.That(Enumerable.Range(0, run.StageFragmentIndices.Length)
                    .Select(index => run.GetDescription(compiled.Graph, index).Kind),
                Is.EqualTo(new[]
                {
                    ShaderDescriptionKind.WholeSource,
                    ShaderDescriptionKind.CurrentPixel,
                    ShaderDescriptionKind.CurrentPixel,
                    ShaderDescriptionKind.CurrentPixel,
                }));
            Assert.That(run.GetWholeSourceHead(compiled.Graph), Is.SameAs(run.GetDescription(compiled.Graph, 0)));
            Assert.That(run.GetOutput(compiled.Graph).Bounds, Is.EqualTo(run.GetStage(compiled.Graph, 0).Bounds));
            Assert.That(run.GetOutput(compiled.Graph).EffectiveScale,
                Is.EqualTo(run.GetStage(compiled.Graph, 0).EffectiveScale));
        }
    }

    [Test]
    public void ColorShiftHead_MapsRequestedRegionBackToItsInput()
    {
        Rect requestedRegion = new(8, 5, 6, 4);
        var colorShift = new ColorShift();
        colorShift.RedOffset.Value = new PixelPoint(3, 0);
        colorShift.GreenOffset.Value = new PixelPoint(-2, 0);
        colorShift.BlueOffset.Value = new PixelPoint(0, 2);
        colorShift.AlphaOffset.Value = new PixelPoint(0, -1);

        using CompiledRenderRequest compiled = Compile(
            [colorShift, new Gamma { Amount = { Value = 180f } }],
            requestedRegion: requestedRegion);

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        RenderFragmentReference head = run.GetStage(compiled.Graph, 0);
        ShaderDescription headDescription = run.GetDescription(compiled.Graph, 0);
        RenderFragmentReference input = run.GetInput(compiled.Graph);
        Rect headRequirement = compiled.Regions.GetFragmentRequirement(head).Resolve(head.Bounds);
        Rect expectedInput = headDescription.Bounds
            .GetRequiredInputBounds(headRequirement)
            .Intersect(input.Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(run.GetWholeSourceHead(compiled.Graph), Is.SameAs(headDescription));
            Assert.That(headRequirement, Is.EqualTo(requestedRegion));
            Assert.That(expectedInput, Is.EqualTo(new Rect(5, 3, 11, 7)));
            Assert.That(compiled.Regions.GetFragmentRequirement(input),
                Is.EqualTo(RequiredRegion.Region(expectedInput)));
        });
    }

    private static RenderFragmentKind[] SemanticKinds(CompiledRenderRequest compiled)
        => [.. compiled.ExecutionPlan.Islands
            .Where(static island => island.ShaderRun is null)
            .Select(island => compiled.Graph.Fragments[island.FragmentIndices[0]].Kind)];

    private static Bitmap RenderBlurThenBrightness(
        FusionMode fusionMode,
        out RenderExecutionStatistics statistics)
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                new Blur { Sigma = { Value = new(2, 2) } },
                new Brightness { Amount = { Value = 50 } },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_bounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            CacheOptions = RenderCacheOptions.Disabled,
            FusionMode = fusionMode,
        }, new CpuRenderTargetFactory());

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        statistics = renderer.LastExecutionStatistics;
        return rasterization.Bitmap?.Clone()
               ?? throw new InvalidOperationException("The filter-effect render produced no bitmap.");
    }

    private static CompiledRenderRequest Compile(
        FilterEffect[] effects,
        SkslBackendBudget? budget = null,
        Rect? requestedRegion = null)
        => FusionBoundaryExecutionTestSupport.CompileEffects(
            s_bounds,
            effects,
            budget ?? SkslBackendBudgetResolver.Portable,
            requestedRegion);

    private static CompiledRenderRequest CompileMosaicGammaOpacityInvert()
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.Value = new Size(10, 10);
        mosaic.Origin.Value = new RelativePoint(0, 0, RelativeUnit.Absolute);
        var headEffects = new FilterEffectGroup
        {
            Children =
            {
                mosaic,
                new Gamma { Amount = { Value = 180f } },
            },
        };
        var tailEffects = new FilterEffectGroup
        {
            Children =
            {
                new Invert
                {
                    Amount = { Value = 65f },
                    ExcludeAlphaChannel = { Value = true },
                },
            },
        };

        using FilterEffect.Resource headResource = headEffects.ToResource(CompositionContext.Default);
        using FilterEffect.Resource tailResource = tailEffects.ToResource(CompositionContext.Default);
        var head = new FilterEffectRenderNode(headResource);
        head.AddChild(new RectangleRenderNode(s_bounds, Brushes.Resource.White, null));
        var opacity = new OpacityRenderNode(0.625f);
        opacity.AddChild(head);
        using var root = new FilterEffectRenderNode(tailResource);
        root.AddChild(opacity);

        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            cachePolicy: RenderCacheOptions.Disabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
            return new RenderRequestCompiler().Compile(request, graph, SkslBackendBudgetResolver.Portable);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }
}
