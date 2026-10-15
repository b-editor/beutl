using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

internal sealed class AntialiasedCoverageBoundaryNode(Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription source = OpaqueRenderDescription.CreateRequestLocal(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas =>
                {
                    using var paint = new SKPaint
                    {
                        Color = new SKColor(196, 96, 224, 208),
                        IsAntialias = true,
                        StrokeWidth = 1,
                        Style = SKPaintStyle.Stroke,
                    };
                    canvas.Canvas.DrawLine(2.25f, 2.75f, 21.25f, 13.25f, paint);
                });
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);
        RenderFragmentHandle current = context.OpaqueSource(source);
        current = context.Shader(current, ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color * color.a; }"));
        context.Publish(current);
    }
}

internal sealed class BackendOverflowBoundaryNode(RenderTarget source, Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        RenderFragmentHandle current = FusionBoundaryExecutionTestSupport.RecordSourcePixels(
            context,
            source,
            bounds);
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.625f));
        context.Publish(context.Shader(current, description));
    }
}


internal sealed record FusionBoundaryExecutionResult(
    Bitmap Bitmap,
    RenderExecutionStatistics Statistics) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

internal static class FusionBoundaryExecutionTestSupport
{
    public static RenderTarget CreatePatternSource(Rect bounds)
    {
        RenderTarget target = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("Could not allocate the fusion-boundary source.");
        target.Value.Canvas.Clear(new SKColor(20, 32, 56, 112));
        using var paint = new SKPaint
        {
            Color = new SKColor(176, 92, 212, 192),
            IsAntialias = true,
        };
        target.Value.Canvas.DrawOval(SKRect.Create(3, 2, 16, 11), paint);
        return target;
    }

    public static RenderNodeRenderer CreateRenderer(RenderNode node, Rect bounds, FusionMode fusionMode)
        => new(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = bounds,
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
            FusionMode = fusionMode,
            Purpose = RenderRequestPurpose.Frame,
        });

    public static FusionBoundaryExecutionResult Execute(
        RenderNode node,
        Rect bounds,
        FusionMode fusionMode)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node, bounds, fusionMode);
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap?.Clone()
            ?? throw new InvalidOperationException("The fusion-boundary render unexpectedly produced no bitmap.");
        return new FusionBoundaryExecutionResult(bitmap, renderer.LastExecutionStatistics);
    }

    public static FusionBoundaryExecutionResult ExecuteWithBudget(
        RenderNode node,
        Rect bounds,
        FusionMode fusionMode,
        SkslBackendBudget budget)
    {
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: fusionMode);
        var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph, budget);
        using var targetRegistry = new RenderTargetPool(factory: null);
        using RenderTargetLeaseSession targets = targetRegistry.BeginSession(
            RenderIntent.Preview);
        PixelRect deviceBounds = PixelRect.FromRect(compiled.ExecutionTargetBounds, 1);
        using RenderTargetLease root = targets.Acquire(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(root.Target, RenderIntent.Preview, 1, 1, compiled.ExecutionTargetBounds.Size);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(
                   -compiled.ExecutionTargetBounds.X,
                   -compiled.ExecutionTargetBounds.Y)))
        {
            var executor = new RenderRequestExecutor(targets);
            executor.Execute(compiled, canvas);
            using Bitmap complete = root.Target.Snapshot();
            Bitmap bitmap = complete.Clone();
            return new FusionBoundaryExecutionResult(bitmap, executor.Statistics);
        }
    }

    // Records the source's pixels at density 1 over the given bounds.
    public static RenderFragmentHandle RecordSourcePixels(
        RenderNodeContext context,
        RenderTarget source,
        Rect bounds)
        => TargetPixelSourceTestSupport.RecordTargetPixels(context, source, bounds);

    // An opaque source leaf over the given bounds, for a graph built by hand.
    public static RenderFragmentReference SourceFragment(Rect bounds, EffectiveScale scale)
        => Fragment(
            RenderFragmentKind.OpaqueSource,
            bounds,
            scale,
            new OpaqueRenderFragmentPayload(
                OpaqueRenderTopology.Source,
                OpaqueRenderDescription.CreateRequestLocal(
                    static _ => { },
                    OpaqueRenderBoundsContract.Source(bounds),
                    RenderHitTestContract.OutputBounds,
                    RenderValueCardinality.Single,
                    RenderScaleContract.MaterializeAtWorkingScale),
                Array.Empty<RenderInputReadback>()));

    public static RenderFragmentReference Fragment(
        RenderFragmentKind kind,
        Rect bounds,
        EffectiveScale scale,
        object? payload,
        params RenderFragmentReference[] inputs)
        => new(
            kind,
            bounds,
            scale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [.. inputs],
            payload,
            RenderFragmentHitTest.Bounds);

    // Commits the fragments in order and publishes the roots, registering a cache candidate for each
    // fragment in cacheCandidates.
    public static RecordedRenderGraph BuildGraph(
        RenderRequestId requestId,
        IReadOnlyList<RenderFragmentReference> references,
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentReference>? cacheCandidates = null)
    {
        var builder = new RecordedRenderGraphBuilder(requestId);
        foreach (RenderFragmentReference reference in references)
        {
            builder.AddFragment(reference);
            if (cacheCandidates?.Contains(reference) == true)
            {
                builder.AddCacheCandidate(
                    reference.Id!.Value,
                    (typeof(FusionBoundaryExecutionTestSupport), reference.Id!.Value.Value));
            }
        }

        foreach (RenderFragmentReference root in roots)
            builder.PublishRoot(root.Id!.Value);
        return builder.Build();
    }

    // Compiles a hand-built graph; without a budget the compiler uses the portable one.
    public static CompiledRenderRequest CompileGraph(
        RenderRequestOptions options,
        Func<RenderRequestId, RecordedRenderGraph> createGraph,
        SkslBackendBudget? budget = null)
    {
        var request = new RenderRequest(options);
        RecordedRenderGraph graph = createGraph(request.Id);
        request.TransitionTo(RenderRequestState.Recording);
        request.TransitionTo(RenderRequestState.Recorded);
        return budget is null
            ? new RenderRequestCompiler().Compile(request, graph)
            : new RenderRequestCompiler().Compile(request, graph, budget);
    }

    // Records the effect over white ellipses, each offset one unit right of the last, and compiles it.
    public static CompiledRenderRequest CompileEffect(
        FilterEffect effect,
        Rect bounds,
        SkslBackendBudget budget,
        Rect? requestedRegion = null,
        int childCount = 1)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        for (int index = 0; index < childCount; index++)
        {
            node.AddChild(new EllipseRenderNode(
                bounds.Translate(new Vector(index, 0)),
                Brushes.Resource.White,
                null));
        }

        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain: bounds,
            requestedRegion: requestedRegion,
            cachePolicy: RenderCacheOptions.Disabled));
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

    public static CompiledRenderRequest CompileEffects(
        Rect bounds,
        IEnumerable<FilterEffect> effects,
        SkslBackendBudget budget,
        Rect? requestedRegion = null)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect effect in effects)
            group.Children.Add(effect);

        return CompileEffect(group, bounds, budget, requestedRegion);
    }

    // A pass-through CurrentPixel stage that declares resourceCount shader resources.
    public static ShaderDescription ResourceHeavyShader(
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

    // A budget that leaves every limit it is not given unbounded.
    public static SkslBackendBudget Budget(
        object? capabilityClass = null,
        int maxStages = int.MaxValue,
        int maxUniformVectors = int.MaxValue,
        int maxSamplers = int.MaxValue,
        int maxSourceBytes = int.MaxValue,
        int maxProgramTokens = int.MaxValue)
        => new(
            capabilityClass ?? "unit-test-backend",
            maxStages,
            maxUniformVectors,
            maxSamplers,
            maxSourceBytes,
            maxProgramTokens);

    public static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int index = 0; index < pixels.Length; index++)
        {
            float value = (float)BitConverter.UInt16BitsToHalf(pixels[index]);
            if (!float.IsFinite(value))
            {
                throw new AssertionException(
                    $"Fusion-boundary bitmap channel {index} is non-finite: {value}.");
            }

            result += Math.Abs(value);
        }

        return result;
    }

    public static int CountFractionalAlphaPixels(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int result = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[index]);
            if (alpha > 0.001f && alpha < 0.999f)
                result++;
        }
        return result;
    }
}

[TestFixture]
public sealed class FusionBoundaryExecutionTestSupportTests
{
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void SumAbsoluteChannels_RejectsInfiniteChannel(float value)
    {
        using var bitmap = new Bitmap(
            1,
            1,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        bitmap.GetPixelSpan<ushort>()[0] = BitConverter.HalfToUInt16Bits((Half)value);

        AssertionException? exception = Assert.Throws<AssertionException>(
            () => FusionBoundaryExecutionTestSupport.SumAbsoluteChannels(bitmap));
        Assert.That(exception!.Message, Does.Contain("channel 0").And.Contain("non-finite"));
    }
}
