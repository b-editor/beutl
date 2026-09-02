using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Media.Pixel;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="GLSLShader"/> は <c>GraphicsContextFactory.SharedContext</c> がないと一切動作しない。
/// Vulkan 経由で実コンパイル/実行できるかをテストする。
/// </summary>
[NonParallelizable]
public class GLSLShaderTests
{
    private const string ConstantBlueFragment = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() {
            outColor = vec4(0.0, 0.0, 1.0, 1.0);
        }
        """;

    private const string MalformedFragment = """
        #version 450
        layout(location = 0) out vec4 outColor;
        void main() {
            outColor = NOT_A_VALID_GLSL_TOKEN;
        }
        """;

    private const string DiscardLeftHalfFragment = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() {
            if (fragCoord.x < 0.5) {
                discard;
            }
            outColor = vec4(0.0, 1.0, 0.0, 1.0);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct DummyPush { public float Dummy; }

    [Test]
    public void TryCreate_ValidShader_Succeeds()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            bool ok = GLSLShader.TryCreate(ConstantBlueFragment, out var shader, out var error);

            try
            {
                Assert.That(ok, Is.True, $"Compile failed: {error}");
                Assert.That(shader, Is.Not.Null);
                Assert.That(error, Is.Null);
            }
            finally
            {
                shader?.Dispose();
            }
        });
    }

    [Test]
    public void TryCreate_EmptySource_ReturnsFailureSynchronously()
    {
        VulkanTestEnvironment.EnsureAvailable();

        bool ok = GLSLShader.TryCreate("   ", out var shader, out var error);

        Assert.That(ok, Is.False);
        Assert.That(shader, Is.Null);
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TryCreate_InvalidSource_ReturnsFailureWithErrorText()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            bool ok = GLSLShader.TryCreate(MalformedFragment, out var shader, out var error);

            Assert.That(ok, Is.False);
            Assert.That(shader, Is.Null);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Create_InvalidSource_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.Throws<InvalidOperationException>(() => GLSLShader.Create(MalformedFragment));
        });
    }

    [Test]
    public void Apply_AfterDispose_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.Dispose();

            using var targets = new EffectTargets();
            var ctx = CreateCustomContext(targets);

            Assert.Throws<ObjectDisposedException>(() =>
                shader.Apply<DummyPush>(ctx, new DummyPush()));
        });
    }

    [Test]
    public void Apply_OverwritesTargetWithShaderOutput()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var targets = new EffectTargets();

            // Set up a 4x4 red EffectTarget so we can detect the shader's blue overwrite.
            using var sourceRenderTarget = RenderTarget.Create(4, 4);
            Assume.That(sourceRenderTarget, Is.Not.Null);
            using (var canvas = new ImmediateCanvas(sourceRenderTarget!, RenderIntent.Preview))
            {
                canvas.Clear(Colors.Red);
            }

            targets.Add(new EffectTarget(sourceRenderTarget!, new Rect(0, 0, 4, 4)));

            var customCtx = CreateCustomContext(targets);

            using var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.Apply<DummyPush>(customCtx, new DummyPush());

            // After Apply, the EffectTarget at index 0 should be replaced with the shader output.
            var resultTarget = targets[0];
            Assert.That(resultTarget.RenderTarget, Is.Not.Null);
            Assert.That(resultTarget.RenderTarget!.Texture, Is.Not.Null);
            Assert.That(resultTarget.RenderTarget.Width, Is.EqualTo(4));

            ctx.WaitIdle();

            // Sample the resulting texture pixels.
            byte[] pixels = resultTarget.RenderTarget.Texture!.DownloadPixels();
            // RGBA16Float: 8 bytes per pixel
            Assert.That(pixels.Length, Is.EqualTo(4 * 4 * 8));

            // First pixel should be (0, 0, 1, 1).
            float r = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 0));
            float g = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 2));
            float b = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels, 4));
            Assert.That(r, Is.EqualTo(0f).Within(0.01f));
            Assert.That(g, Is.EqualTo(0f).Within(0.01f));
            Assert.That(b, Is.EqualTo(1f).Within(0.01f));
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void ConsecutiveEffects_SubmitEachEffectAndWaitOnlyAtTheReadbackBoundary()
    {
        IGraphicsContext graphicsContext = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var targets = new EffectTargets();
            using RenderTarget source = RenderTarget.Create(4, 4)
                ?? throw new InvalidOperationException("Could not create the GLSL source target.");
            using (var canvas = new ImmediateCanvas(source, RenderIntent.Preview))
            {
                canvas.Clear(Colors.Red);
            }

            targets.Add(new EffectTarget(source, new Rect(0, 0, 4, 4)));
            var customContext = CreateCustomContext(targets);
            using var shader = GLSLShader.Create(ConstantBlueFragment);

            // Exclude setup transitions and shader creation from the measured chain.
            graphicsContext.WaitIdle();
            var events = new List<VulkanCommandPoolEvent>();
            var allocations = new List<TextureFormat>();
            Bitmap result;
            using (VulkanContext.ObserveTextureAllocations(allocations.Add))
            using (VulkanCommandPool.Observe(events.Add))
            {
                shader.Apply<DummyPush>(customContext, new DummyPush());
                shader.Apply<DummyPush>(customContext, static _ => new DummyPush());
                shader.ApplyMultiPass<DummyPush>(customContext, 3, static (_, _) => new DummyPush());
                result = targets[0].RenderTarget!.Snapshot();
            }
            using (result)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                        Is.EqualTo(3),
                        "Each native effect must submit its output, while multi-pass work stays in one batch.");
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.FenceWait),
                        Is.EqualTo(1),
                        "Only the CPU readback boundary may wait for the native effect chain.");
                    Assert.That(
                        allocations,
                        Does.Contain(TextureFormat.RGBA16Float),
                        "The allocation observer must see the filter destinations.");
                    Assert.That(
                        allocations,
                        Has.None.EqualTo(TextureFormat.Depth32Float),
                        "Fullscreen filter passes must not allocate unused depth textures.");

                    RgbaF16 pixel = result.GetPixelSpan<RgbaF16>()[0];
                    Assert.That((float)pixel.R, Is.EqualTo(0).Within(0.01f));
                    Assert.That((float)pixel.G, Is.EqualTo(0).Within(0.01f));
                    Assert.That((float)pixel.B, Is.EqualTo(1).Within(0.01f));
                    Assert.That((float)pixel.A, Is.EqualTo(1).Within(0.01f));
                });
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void RepeatedNativeEffectChain_AllocatesOnlyWhileWarmingTheTargetPool()
    {
        IGraphicsContext graphicsContext = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            using RenderTarget source = CreateSolidTarget(4, 4, Colors.Red);
            using var registry = new RenderTargetLeaseRegistry(factory: null);
            using var shader = GLSLShader.Create(ConstantBlueFragment);

            graphicsContext.WaitIdle();
            List<TextureFormat> firstAllocations = RunPooledEffectChain(source, registry, shader);
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0));
            GpuResourceReclaimQueue.FlushAndDrain();
            List<TextureFormat> secondAllocations = RunPooledEffectChain(source, registry, shader);

            Assert.Multiple(() =>
            {
                Assert.That(
                    firstAllocations.Count(static format => format == TextureFormat.RGBA16Float),
                    Is.EqualTo(4),
                    "The first chain must allocate its two destinations, two ping-pong buffers, and final destination with one intra-chain reuse.");
                Assert.That(
                    secondAllocations,
                    Has.None.EqualTo(TextureFormat.RGBA16Float),
                    "An identical warmed chain must use only retained pool slots.");
                Assert.That(registry.Statistics.Creates, Is.EqualTo(4));
                Assert.That(registry.Statistics.Reuses, Is.GreaterThanOrEqualTo(4));
            });
            GpuResourceReclaimQueue.FlushAndDrain();
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void DiscardingShader_ClearsAReusedTargetBeforeRendering()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = CreateSolidTarget(4, 4, Colors.Red);
            using var registry = new RenderTargetLeaseRegistry(factory: null);
            using var warmupShader = GLSLShader.Create(ConstantBlueFragment);
            using var discardingShader = GLSLShader.Create(DiscardLeftHalfFragment);

            using (RenderTargetLeaseSession warmup = registry.BeginSession(
                       RenderIntent.Delivery,
                       source))
            using (var warmupTargets = new EffectTargets
                   {
                       new EffectTarget(source, new Rect(0, 0, 4, 4)),
                   })
            {
                var warmupContext = CreateCustomContext(warmupTargets, warmup);
                warmupShader.Apply<DummyPush>(warmupContext, new DummyPush());
                using Bitmap completedWarmup = warmupTargets[0].RenderTarget!.Snapshot();
            }
            Assert.That(GpuResourceReclaimQueue.PendingCount, Is.GreaterThan(0));
            GpuResourceReclaimQueue.FlushAndDrain();

            var reuseAllocations = new List<TextureFormat>();
            using RenderTargetLeaseSession reuse = registry.BeginSession(
                RenderIntent.Delivery,
                source);
            using var targets = new EffectTargets
            {
                new EffectTarget(source, new Rect(0, 0, 4, 4)),
            };
            var context = CreateCustomContext(targets, reuse);
            using (VulkanContext.ObserveTextureAllocations(reuseAllocations.Add))
                discardingShader.Apply<DummyPush>(context, new DummyPush());
            using Bitmap result = targets[0].RenderTarget!.Snapshot();

            ReadOnlySpan<RgbaF16> pixels = result.GetPixelSpan<RgbaF16>();
            RgbaF16 discarded = pixels[0];
            RgbaF16 written = pixels[3];
            Assert.Multiple(() =>
            {
                Assert.That(reuseAllocations, Is.Empty, "The discard pass must reuse the warmed slot.");
                Assert.That(registry.Statistics.Reuses, Is.EqualTo(1));
                Assert.That((float)discarded.R, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)discarded.G, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)discarded.B, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)discarded.A, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)written.R, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)written.G, Is.EqualTo(1).Within(0.01f));
                Assert.That((float)written.B, Is.EqualTo(0).Within(0.01f));
                Assert.That((float)written.A, Is.EqualTo(1).Within(0.01f));
            });
            targets.Dispose();
            reuse.Dispose();
            GpuResourceReclaimQueue.FlushAndDrain();
        });
    }

    private static List<TextureFormat> RunPooledEffectChain(
        RenderTarget source,
        RenderTargetLeaseRegistry registry,
        GLSLShader shader)
    {
        using RenderTargetLeaseSession session = registry.BeginSession(
            RenderIntent.Delivery,
            source);
        using var targets = new EffectTargets
        {
            new EffectTarget(source, new Rect(0, 0, 4, 4)),
        };
        var context = CreateCustomContext(targets, session);
        var allocations = new List<TextureFormat>();
        using (VulkanContext.ObserveTextureAllocations(allocations.Add))
        {
            shader.Apply<DummyPush>(context, new DummyPush());
            shader.Apply<DummyPush>(context, static _ => new DummyPush());
            shader.ApplyMultiPass<DummyPush>(context, 3, static (_, _) => new DummyPush());
            using Bitmap result = targets[0].RenderTarget!.Snapshot();
        }

        return allocations;
    }

    private static RenderTarget CreateSolidTarget(int width, int height, Color color)
    {
        RenderTarget target = RenderTarget.Create(width, height)
            ?? throw new InvalidOperationException("Could not create the GLSL source target.");
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear(color);
        }

        return target;
    }

    private static CustomFilterEffectContext CreateCustomContext(
        EffectTargets targets,
        RenderTargetLeaseSession? session = null)
        => new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary,
            renderTargetLeaseSession: session);
}
