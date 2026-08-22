using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

/// <summary>
/// Records how migrated color adjustments land in the compiled plan as CurrentPixel shader stages.
/// </summary>
/// <remarks>
/// This is a characterization test: a Skia blur remains a compatibility segment, while a following migrated
/// adjustment becomes a shader run after coverage resolution. Adjacent CurrentPixel adjustments fuse together,
/// including the resource-heavy Curves case under the Vulkan profile.
/// </remarks>
[TestFixture]
public sealed class ColorFilterIslandSplitTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    /// <summary>
    /// Saturate now follows the same CurrentPixel path as Brightness: the blur stays in its compatibility segment,
    /// and Saturate starts a shader run once that segment has resolved coverage.
    /// </summary>
    [Test]
    public void BlurThenSaturate_CompilesAfterTheSingleTargetSegment()
    {
        using CompiledRenderRequest compiled = Compile(
            new Blur { Sigma = { CurrentValue = new(2, 2) } },
            new Saturate { Amount = { CurrentValue = 50f } });

        Report("Blur -> Saturate (shader stage)", compiled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(3));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.Kind),
                Is.EqualTo(new[]
                {
                    ExecutionIslandKind.Compatibility,
                    ExecutionIslandKind.Compatibility,
                    ExecutionIslandKind.ShaderRun,
                }));
            Assert.That(Reasons(compiled), Is.EqualTo(new[]
            {
                ExecutionIslandBoundaryReason.Opaque,
                ExecutionIslandBoundaryReason.FilterEffectSegment,
                ExecutionIslandBoundaryReason.CoverageResolution,
            }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(1));
        }
    }

    /// <summary>
    /// Brightness is a shader stage, so it cannot be folded into the blur's segment. The blur stays in a
    /// compatibility island, while Brightness becomes a one-stage shader run after the segment has resolved
    /// its coverage.
    /// </summary>
    /// <remarks>
    /// A pure Skia segment preserves its one input target and therefore publishes
    /// <c>RenderValueCardinality.Single</c>. That permits the downstream CurrentPixel stage to compile. The
    /// boundary after the segment is <c>CoverageResolution</c>, because the Skia materialization establishes
    /// the coverage that Brightness consumes; it is not a topology rejection.
    /// </remarks>
    [Test]
    public void BlurThenBrightness_CompilesAfterTheSingleTargetSegment()
    {
        using CompiledRenderRequest compiled = Compile(
            new Blur { Sigma = { CurrentValue = new(2, 2) } },
            new Brightness { Amount = { CurrentValue = 50f } });

        Report("Blur -> Brightness (shader stage)", compiled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(3));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.Kind),
                Is.EqualTo(new[]
                {
                    ExecutionIslandKind.Compatibility,
                    ExecutionIslandKind.Compatibility,
                    ExecutionIslandKind.ShaderRun,
                }));
            Assert.That(Reasons(compiled), Is.EqualTo(new[]
            {
                ExecutionIslandBoundaryReason.Opaque,
                ExecutionIslandBoundaryReason.FilterEffectSegment,
                ExecutionIslandBoundaryReason.CoverageResolution,
            }));

            // The blur segment is what forces the split, so no custom effect is blamed for it.
            Assert.That(Reasons(compiled), Does.Not.Contain(ExecutionIslandBoundaryReason.LegacyCustomEffect));

            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(1));
        }
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

    /// <summary>
    /// Two adjacent CurrentPixel stages do fuse: Brightness followed by Gamma is a single shader run holding
    /// both stages, with no boundary between them.
    /// </summary>
    [Test]
    public void BrightnessThenGamma_FusesIntoASingleTwoStageShaderRun()
    {
        using CompiledRenderRequest compiled = Compile(
            new Brightness { Amount = { CurrentValue = 50f } },
            new Gamma { Amount = { CurrentValue = 220f } });

        Report("Brightness -> Gamma", compiled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(2));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.Kind),
                Is.EqualTo(new[] { ExecutionIslandKind.Compatibility, ExecutionIslandKind.ShaderRun }));

            // Only the source-materialization boundaries; nothing separates the two color stages.
            Assert.That(Reasons(compiled), Is.EqualTo(new[]
            {
                ExecutionIslandBoundaryReason.Opaque,
                ExecutionIslandBoundaryReason.CoverageResolution,
            }));
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(2));
        }
    }

    /// <summary>
    /// Curves binds nine curve resources in addition to the implicit source. The Portable policy admits those ten
    /// resources under its 12/12 budget, so Curves can remain in the same shader run as the two color stages that
    /// follow it while four slots below the backend guarantee remain reserved for Skia's surrounding program.
    /// </summary>
    [Test]
    public void CurvesThenBrightnessThenGamma_FusesWithinThePortableBudget()
    {
        using CompiledRenderRequest compiled = Compile(
            SkslBackendBudgetResolver.Portable,
            new Curves(),
            new Brightness { Amount = { CurrentValue = 50f } },
            new Gamma { Amount = { CurrentValue = 220f } });

        Report("Curves -> Brightness -> Gamma", compiled);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands, Has.Length.EqualTo(2));
            Assert.That(
                compiled.ExecutionPlan.Islands.Select(static island => island.Kind),
                Is.EqualTo(new[] { ExecutionIslandKind.Compatibility, ExecutionIslandKind.ShaderRun }));

            // Only source materialization remains; no backend limit separates the three shader stages.
            Assert.That(Reasons(compiled), Is.EqualTo(new[]
            {
                ExecutionIslandBoundaryReason.Opaque,
                ExecutionIslandBoundaryReason.CoverageResolution,
            }));
            Assert.That(Reasons(compiled), Does.Not.Contain(ExecutionIslandBoundaryReason.FilterEffectSegment));
            Assert.That(Reasons(compiled), Does.Not.Contain(ExecutionIslandBoundaryReason.BackendLimit));

            Assert.That(compiled.ExecutionPlan.ShaderRuns, Has.Exactly(1).Items);
            Assert.That(compiled.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(3));
        }
    }

    [Test]
    public void MosaicGammaOpacityInvert_CompileAsOneWholeSourceHeadedRun()
    {
        using CompiledRenderRequest compiled = CompileMosaicGammaOpacityInvert(FusionMode.Enabled);

        Report("Mosaic -> Gamma -> Opacity -> Invert", compiled);
        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        TestContext.WriteLine("Generated SkSL:\n" + run.Program.Source);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compiled.ExecutionPlan.Islands.Select(static island => island.Kind),
                Is.EqualTo(new[] { ExecutionIslandKind.Compatibility, ExecutionIslandKind.ShaderRun }));
            Assert.That(Reasons(compiled), Is.EqualTo(new[]
            {
                ExecutionIslandBoundaryReason.Opaque,
                ExecutionIslandBoundaryReason.CoverageResolution,
            }));
            Assert.That(Reasons(compiled), Does.Not.Contain(ExecutionIslandBoundaryReason.WholeSourceShader));
            Assert.That(run.Stages.Select(static stage => stage.Kind),
                Is.EqualTo(new[]
                {
                    RenderFragmentKind.Shader,
                    RenderFragmentKind.Shader,
                    RenderFragmentKind.Opacity,
                    RenderFragmentKind.Shader,
                }));
            Assert.That(run.Stages.Select(static stage => stage.Description.Kind),
                Is.EqualTo(new[]
                {
                    ShaderDescriptionKind.WholeSource,
                    ShaderDescriptionKind.CurrentPixel,
                    ShaderDescriptionKind.CurrentPixel,
                    ShaderDescriptionKind.CurrentPixel,
                }));
            Assert.That(run.WholeSourceHead, Is.SameAs(run.Stages[0].Description));
            Assert.That(run.Output.Bounds, Is.EqualTo(run.Stages[0].Fragment.Bounds));
            Assert.That(run.Output.EffectiveScale, Is.EqualTo(run.Stages[0].Fragment.EffectiveScale));
        }
    }

    [Test]
    public void ColorShiftHead_MapsRequestedRegionBackToItsInput()
    {
        Rect requestedRegion = new(8, 5, 6, 4);
        var colorShift = new ColorShift();
        colorShift.RedOffset.CurrentValue = new PixelPoint(3, 0);
        colorShift.GreenOffset.CurrentValue = new PixelPoint(-2, 0);
        colorShift.BlueOffset.CurrentValue = new PixelPoint(0, 2);
        colorShift.AlphaOffset.CurrentValue = new PixelPoint(0, -1);

        using CompiledRenderRequest compiled = Compile(
            FusionMode.Enabled,
            requestedRegion,
            colorShift,
            new Gamma { Amount = { CurrentValue = 180f } });

        CompiledShaderRun run = compiled.ExecutionPlan.ShaderRuns.Single();
        CompiledShaderStage head = run.Stages[0];
        Rect headRequirement = compiled.Regions.GetFragmentRequirement(head.Fragment).Resolve(head.Fragment.Bounds);
        Rect expectedInput = head.Description.Bounds
            .GetRequiredInputBounds(headRequirement)
            .Intersect(run.Input.Bounds);

        Assert.Multiple(() =>
        {
            Assert.That(run.WholeSourceHead, Is.SameAs(head.Description));
            Assert.That(headRequirement, Is.EqualTo(requestedRegion));
            Assert.That(expectedInput, Is.EqualTo(new Rect(5, 3, 11, 7)));
            Assert.That(compiled.Regions.GetFragmentRequirement(run.Input),
                Is.EqualTo(RequiredRegion.Region(expectedInput)));
        });
    }

    private static void Report(string label, CompiledRenderRequest compiled)
    {
        TestContext.WriteLine(
            $"{label}: {compiled.ExecutionPlan.Islands.Length} islands "
            + $"[{string.Join(", ", compiled.ExecutionPlan.Islands.Select(static island => island.Kind))}], "
            + $"boundary reasons [{string.Join(", ", Reasons(compiled))}], "
            + $"shader runs {compiled.ExecutionPlan.ShaderRuns.Count()} "
            + $"[{string.Join(", ", compiled.ExecutionPlan.ShaderRuns.Select(static run => run.Stages.Length))}]");
    }

    private static ExecutionIslandBoundaryReason[] Reasons(CompiledRenderRequest compiled)
        => [.. compiled.ExecutionPlan.Boundaries.Select(static boundary => boundary.Reason)];

    private static Bitmap RenderBlurThenBrightness(
        FusionMode fusionMode,
        out RenderExecutionStatistics statistics)
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                new Blur { Sigma = { CurrentValue = new(2, 2) } },
                new Brightness { Amount = { CurrentValue = 50 } },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_bounds, Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Disabled,
                    FusionMode = fusionMode,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        statistics = renderer.LastExecutionStatistics;
        return rasterization.Bitmap?.Clone()
               ?? throw new InvalidOperationException("The filter-effect render produced no bitmap.");
    }

    private static CompiledRenderRequest Compile(params FilterEffect[] effects)
        => Compile(SkslBackendBudgetResolver.Portable, effects);

    private static CompiledRenderRequest Compile(
        FusionMode fusionMode,
        Rect? requestedRegion,
        params FilterEffect[] effects)
        => Compile(SkslBackendBudgetResolver.Portable, fusionMode, requestedRegion, effects);

    private static CompiledRenderRequest Compile(
        SkslBackendBudget budget,
        params FilterEffect[] effects)
        => Compile(budget, FusionMode.Enabled, requestedRegion: null, effects);

    private static CompiledRenderRequest Compile(
        SkslBackendBudget budget,
        FusionMode fusionMode,
        Rect? requestedRegion,
        params FilterEffect[] effects)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect effect in effects)
            group.Children.Add(effect);

        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_bounds, Brushes.Resource.White, null));

        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: s_bounds,
            requestedRegion: requestedRegion,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: fusionMode));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler().Compile(request, graph, budget);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static CompiledRenderRequest CompileMosaicGammaOpacityInvert(FusionMode fusionMode)
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(10, 10);
        mosaic.Origin.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
        var headEffects = new FilterEffectGroup
        {
            Children =
            {
                mosaic,
                new Gamma { Amount = { CurrentValue = 180f } },
            },
        };
        var tailEffects = new FilterEffectGroup
        {
            Children =
            {
                new Invert
                {
                    Amount = { CurrentValue = 65f },
                    ExcludeAlphaChannel = { CurrentValue = true },
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
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: fusionMode));
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

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize size = allocation.DeviceSize;
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU filter-effect test surface.");
            return new CpuRenderTarget(surface, size);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
