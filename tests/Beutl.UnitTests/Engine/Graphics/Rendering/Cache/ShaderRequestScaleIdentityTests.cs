using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class ShaderRequestScaleIdentityTests
{
    private const string ShaderSource =
        """
        uniform float tint;

        half4 apply(half4 color) {
            return half4(half3(tint), half(1.0));
        }
        """;

    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    private static readonly RenderResourceSlot<ExecutionProbe> s_probeSlot = new();

    // Read once here rather than inside the callback: Colors.White is a get-only property whose getter
    // this compilation cannot see, so a callback naming it is not shown to answer the same way twice.
    private static readonly Color s_fill = Colors.White;

    private static readonly ShaderDescription s_binderShader =
        ShaderDescription.CurrentPixel(
            ShaderSource,
            static bindings => bindings.Uniform(
                "tint",
                0.5f,
                static (writer, _, context) => writer.Set(context.OutputScale >= 2f ? 1f : 0.25f)));

    private static readonly ShaderDescription s_maxWorkingScaleShader =
        ShaderDescription.CurrentPixel(
            ShaderSource,
            static bindings => bindings.Uniform(
                "tint",
                0.5f,
                static (writer, _, context) => writer.Set(
                    float.IsFinite(context.MaxWorkingScale) ? 1f : 0.25f)));

    private static readonly ShaderDescription s_directShader =
        ShaderDescription.CurrentPixel(
            ShaderSource,
            static bindings => bindings.Uniform("tint", 0.5f));

    [Test]
    public void OutputScaleReadByAnExecutionTimeBinder_SeparatesTheCacheIdentity()
    {
        using var node = new ProbedShaderNode(s_binderShader);
        node.Cache.RecordStableRequests();
        using RenderNodeRenderer renderer = CreateRenderer(node);

        ulong atScale1 = RenderTopLeft(renderer, outputScale: 1);
        ulong atScale2 = RenderTopLeft(renderer, outputScale: 2);

        Assert.Multiple(() =>
        {
            Assert.That(node.Probe.Count, Is.EqualTo(2),
                "a binder that reads the request's output scale must not be served the other scale's pixels");
            Assert.That(atScale2, Is.Not.EqualTo(atScale1),
                "the second request must show the colour its own output scale binds");
        });
    }

    [Test]
    public void MaxWorkingScaleReadByAnExecutionTimeBinder_SeparatesTheCacheIdentity()
    {
        using var node = new ProbedShaderNode(s_maxWorkingScaleShader);
        node.Cache.RecordStableRequests();
        using RenderNodeRenderer renderer = CreateRenderer(node);

        ulong unbounded = RenderTopLeft(renderer, outputScale: 1, maxWorkingScale: float.PositiveInfinity);
        ulong bounded = RenderTopLeft(renderer, outputScale: 1, maxWorkingScale: 4);

        Assert.Multiple(() =>
        {
            Assert.That(node.Probe.Count, Is.EqualTo(2),
                "both ceilings leave the stage at the same density, so only the identity can keep the "
                + "binder's two answers apart");
            Assert.That(bounded, Is.Not.EqualTo(unbounded));
        });
    }

    [Test]
    public void ShaderWithoutAnExecutionTimeBinder_SharesItsCachedPixelsAcrossRequestScales()
    {
        using var node = new ProbedShaderNode(s_directShader);
        node.Cache.RecordStableRequests();
        using RenderNodeRenderer renderer = CreateRenderer(node);

        ulong first = RenderTopLeft(renderer, outputScale: 1, maxWorkingScale: float.PositiveInfinity);
        ulong second = RenderTopLeft(renderer, outputScale: 2, maxWorkingScale: 4);

        Assert.Multiple(() =>
        {
            Assert.That(node.Probe.Count, Is.EqualTo(1),
                "a shader whose uniforms are fixed at recording time reads no request scale, so both "
                + "requests must share one cache entry");
            Assert.That(second, Is.EqualTo(first));
        });
    }

    private static ulong RenderTopLeft(
        RenderNodeRenderer renderer,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity)
    {
        using RenderNodeRasterization rasterization = renderer.Rasterize(new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            CacheOptions = RenderCacheOptions.Enabled,
            Purpose = RenderRequestPurpose.Frame,
            OutputScale = outputScale,
            MaxWorkingScale = maxWorkingScale,
        });
        Assert.That(rasterization.IsEmpty, Is.False);
        return ReadFirstPixel(rasterization.Bitmap!);
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_bounds,
            CacheOptions = RenderCacheOptions.Enabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuTargetFactory());

    private static ulong ReadFirstPixel(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return ((ulong)pixels[0] << 48)
               | ((ulong)pixels[1] << 32)
               | ((ulong)pixels[2] << 16)
               | pixels[3];
    }

    /// <summary>
    /// A fixed-density source under one current-pixel shader. The density is pinned so the two requests
    /// differ in nothing the identity already tracks.
    /// </summary>
    private sealed class ProbedShaderNode(ShaderDescription description) : RenderNode
    {
        public ExecutionProbe Probe { get; } = new();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probeToken = context.Borrow(Probe);
            OpaqueRenderDescription source = OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) => session.UseResource(s_probeSlot, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.Clear(s_fill));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.None,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(static _ => 2f),
                resources: [s_probeSlot.Bind(probeToken)],
                slots: [s_probeSlot]);

            RenderFragmentHandle input = context.OpaqueSource(source);
            context.Publish(context.ContributeValues(context.Shader(input, description)));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
