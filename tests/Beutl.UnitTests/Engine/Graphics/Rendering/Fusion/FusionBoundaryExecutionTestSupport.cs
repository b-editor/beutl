using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

internal enum FusionBoundaryRuntimeScenario
{
    MaterializedInput,
    WholeSource,
    Geometry,
    OpaqueCallback,
    TargetReadback,
    DestinationBlend,
    DynamicExpansion,
    Graphics3D,
}

internal sealed class FusionBoundaryRuntimeNode(
    RenderTarget source,
    Rect bounds,
    FusionBoundaryRuntimeScenario scenario) : RenderNode
{
    private static readonly ShaderDescription s_firstShader = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return half4(color.rg * 0.875, color.ba); }");

    private static readonly ShaderDescription s_secondShader = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return half4(color.b, color.g, color.r, color.a); }");

    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(
            source,
            (typeof(FusionBoundaryRuntimeNode), scenario, "source"),
            1);
        RenderFragmentHandle current = context.MaterializedInput(
            MaterializedInputDescription.FromRenderTarget(
                resource,
                bounds,
                EffectiveScale.At(1),
                RenderHitTestContract.OutputBounds));
        current = context.Shader(current, s_firstShader);

        switch (scenario)
        {
            case FusionBoundaryRuntimeScenario.MaterializedInput:
                context.Publish(current);
                return;

            case FusionBoundaryRuntimeScenario.WholeSource:
                current = context.Shader(current, ShaderDescription.WholeSource(
                    "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                    RenderBoundsContract.Identity));
                break;

            case FusionBoundaryRuntimeScenario.Geometry:
                current = context.Geometry(current, GeometryDescription.Create(
                    session => session.Canvas.Use(session.Input.Draw),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    structuralKey: typeof(FusionBoundaryRuntimeNode),
                    runtimeIdentity: new RenderRuntimeIdentity("geometry-identity")));
                break;

            case FusionBoundaryRuntimeScenario.OpaqueCallback:
                current = context.OpaqueMap(current, CreateOpaqueMap(
                    RenderBackendBoundary.None,
                    RenderValueCardinality.Single,
                    "opaque-identity"));
                break;

            case FusionBoundaryRuntimeScenario.TargetReadback:
                context.Publish(current);
                context.Publish(context.TargetCommand(
                    [current],
                    TargetCommandDescription.Create(
                        session => session.UseSnapshot(static _ => { }),
                        TargetRegion.Full,
                        Rect.Empty,
                        RenderHitTestContract.None,
                        TargetAccess.Readback,
                        structuralKey: typeof(FusionBoundaryRuntimeNode),
                        runtimeIdentity: new RenderRuntimeIdentity("target-readback"))));
                return;

            case FusionBoundaryRuntimeScenario.DestinationBlend:
                context.Publish(context.Blend(current, BlendMode.DstOver));
                return;

            case FusionBoundaryRuntimeScenario.DynamicExpansion:
                current = context.OpaqueExpand(
                    [current],
                    OpaqueRenderDescription.Create(
                        CopySingleInput,
                        RenderOperationBoundsContract.FullInputs(
                            static inputs => inputs.Single(),
                            typeof(FusionBoundaryRuntimeNode)),
                        RenderHitTestContract.AnyInput,
                        RenderValueCardinality.Dynamic,
                        RenderScaleContract.MaterializeAtWorkingScale,
                        structuralKey: (typeof(FusionBoundaryRuntimeNode), "dynamic"),
                        runtimeIdentity: new RenderRuntimeIdentity("dynamic")));
                break;

            case FusionBoundaryRuntimeScenario.Graphics3D:
                current = context.OpaqueMap(current, CreateOpaqueMap(
                    RenderBackendBoundary.Graphics3D,
                    RenderValueCardinality.Single,
                    "graphics-3d"));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        context.Publish(context.Shader(current, s_secondShader));
    }

    private static OpaqueRenderDescription CreateOpaqueMap(
        RenderBackendBoundary backendBoundary,
        RenderValueCardinality cardinality,
        string identity)
    {
        if (backendBoundary == RenderBackendBoundary.None)
        {
            return OpaqueRenderDescription.Create(
                CopySingleInput,
                RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                RenderHitTestContract.AnyInput,
                cardinality,
                RenderScaleContract.PreserveInputSupply,
                structuralKey: (typeof(FusionBoundaryRuntimeNode), identity),
                runtimeIdentity: new RenderRuntimeIdentity(identity));
        }

        return OpaqueRenderDescription.CreateBackendBoundary(
            backendBoundary,
            CopySingleInput,
            RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            cardinality,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: (typeof(FusionBoundaryRuntimeNode), identity),
            runtimeIdentity: new RenderRuntimeIdentity(identity));
    }

    private static void CopySingleInput(OpaqueRenderSession session)
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
        output.Canvas.Use(session.Inputs.Single().Draw);
        session.Publish(output);
    }
}

internal sealed class AntialiasedCoverageBoundaryNode(Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription source = OpaqueRenderDescription.Create(
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
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(AntialiasedCoverageBoundaryNode),
            runtimeIdentity: new RenderRuntimeIdentity("aa-thin-stroke"));
        RenderFragmentHandle current = context.OpaqueSource(source);
        current = context.Shader(current, ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color * color.a; }"));
        context.Publish(current);
    }
}

internal sealed class CachedBoundaryRoot(RenderTarget source, Rect bounds) : RenderNode
{
    private readonly CachedBoundaryShaderNode _cached = new();
    private readonly BoundaryShaderNode _after = new(
        ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return half4(color.bgr, color.a); }"));

    public CachedBoundaryShaderNode Cached => _cached;

    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(
            source,
            (typeof(CachedBoundaryRoot), "source"),
            1);
        RenderFragmentHandle current = context.MaterializedInput(
            MaterializedInputDescription.FromRenderTarget(
                resource,
                bounds,
                EffectiveScale.At(1),
                RenderHitTestContract.OutputBounds));
        current = context.RecordNode(_cached, [current]).Single();
        current = context.RecordNode(_after, [current]).Single();
        context.Publish(current);
    }

    protected override void OnDispose(bool disposing)
    {
        _after.Dispose();
        _cached.Dispose();
        base.OnDispose(disposing);
    }
}

internal sealed class CachedBoundaryShaderNode : RenderNode
{
    private static readonly ShaderDescription s_description = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return color * 0.75; }");

    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.Shader(context.Inputs.Single(), s_description));
    }
}

internal sealed class BoundaryShaderNode(ShaderDescription description) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.Shader(context.Inputs.Single(), description));
    }
}

internal sealed class BackendOverflowBoundaryNode(RenderTarget source, Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(
            source,
            (typeof(BackendOverflowBoundaryNode), "source"),
            1);
        RenderFragmentHandle current = context.MaterializedInput(
            MaterializedInputDescription.FromRenderTarget(
                resource,
                bounds,
                EffectiveScale.At(1),
                RenderHitTestContract.OutputBounds));
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            static bindings => bindings.Uniform("gain", 0.625f));
        context.Publish(context.Shader(current, description));
    }
}

internal sealed record FusionBoundaryExecutionResult(
    Bitmap Bitmap,
    RenderExecutionStatistics Statistics,
    RenderPipelineDiagnosticSnapshot Diagnostics) : IDisposable
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

    public static FusionBoundaryExecutionResult Execute(
        RenderNode node,
        Rect bounds,
        FusionMode fusionMode,
        bool useRenderCache = false)
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = useRenderCache,
                FusionMode = fusionMode,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap?.Clone()
            ?? throw new InvalidOperationException("The fusion-boundary render unexpectedly produced no bitmap.");
        return new FusionBoundaryExecutionResult(
            bitmap,
            renderer.LastExecutionStatistics,
            diagnostics.Latest);
    }

    public static FusionBoundaryExecutionResult ExecuteWithBudget(
        RenderNode node,
        Rect bounds,
        FusionMode fusionMode,
        SkslBackendBudget budget)
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        var options = new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: bounds,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: fusionMode,
            diagnostics: diagnostics);
        var request = new RenderRequest(options);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        using CompiledRenderRequest compiled = new RenderRequestCompiler().Compile(request, graph, budget);
        using var targetRegistry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = targetRegistry.BeginSession(RenderIntent.Preview);
        PixelRect deviceBounds = PixelRect.FromRect(compiled.ExecutionTargetBounds, 1);
        using RenderTargetLease root = targets.Acquire(deviceBounds.Size);
        using var canvas = new ImmediateCanvas(root.Target, 1, 1, compiled.ExecutionTargetBounds.Size);
        canvas.Clear();
        using (canvas.PushTransform(Matrix.CreateTranslation(
                   -compiled.ExecutionTargetBounds.X,
                   -compiled.ExecutionTargetBounds.Y)))
        {
            var executor = new RenderRequestExecutor(targets);
            executor.Execute(compiled, canvas);
            using Bitmap complete = root.Target.Snapshot();
            Bitmap bitmap = complete.Clone();
            return new FusionBoundaryExecutionResult(bitmap, executor.Statistics, diagnostics.Latest);
        }
    }

    public static double SumAbsoluteChannels(Bitmap bitmap)
    {
        double result = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
            result += Math.Abs((float)BitConverter.UInt16BitsToHalf(bits));
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
