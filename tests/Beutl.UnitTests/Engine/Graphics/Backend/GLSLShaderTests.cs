using System.Runtime.InteropServices;
using Beutl.Composition;
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

    private const string ThreeInputFragment = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D first;
        layout(set = 0, binding = 1) uniform sampler2D second;
        layout(set = 0, binding = 2) uniform sampler2D third;
        layout(push_constant) uniform PC { float gain; } pc;
        void main() {
            outColor = vec4(texture(first, fragCoord).r * pc.gain,
                            texture(second, fragCoord).g,
                            texture(third, fragCoord).b, 1.0);
        }
        """;

    private const string InvertFragment = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() {
            vec4 color = texture(source, fragCoord);
            outColor = vec4(vec3(color.a) - color.rgb, color.a);
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
    public void CSharpScript_ReusesGlslProgramWhileTimeChangesAndDisposesItsCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new CSharpScriptEffect
            {
                Script = { CurrentValue = """"
                    const string fragment = """
                        #version 450
                        layout(location=0) out vec4 color;
                        layout(push_constant) uniform PC { float value; } pc;
                        void main() { color = vec4(0, pc.value, 1, 1); }
                        """;
                    Context.CustomEffect(Time, (float time, CustomFilterEffectContext execution) =>
                    {
                        using var shader = CreateGlslShader(fragment);
                        execution.ForEach((int index, EffectTarget input) =>
                            shader.Render(execution, new[] { input }, input.Bounds, time));
                    }, (float time, Rect bounds) => bounds);
                    """" },
            };
            using var resource = (CSharpScriptEffect.Resource)effect.ToResource(CompositionContext.Default);
            using RenderTarget source = CreateSolidTarget(4, 4, Colors.Red);
            foreach (float time in new[] { 0.25f, 0.75f })
            {
                bool updateOnly = false;
                resource.Update(effect, new CompositionContext(TimeSpan.FromSeconds(time)), ref updateOnly);
                using var context = new FilterEffectContext(new Rect(0, 0, 4, 4));
                context.ApplyTransactional(effect, resource);
                using var targets = new EffectTargets { new EffectTarget(source, new Rect(0, 0, 4, 4)) };
                using var builder = new SKImageFilterBuilder();
                using var executor = new FilterEffectExecutor(
                    targets, builder, RenderIntent.Delivery, RenderRequestPurpose.Auxiliary,
                    drawableBrushMaterializer: null);
                executor.Apply(context);
                executor.Flush(false);
                using Bitmap pixels = targets[0].RenderTarget!.Snapshot();
                RgbaF16 pixel = pixels.GetPixelSpan<RgbaF16>()[0];
                Assert.That((float)pixel.G, Is.EqualTo(time).Within(0.01), "new constants must reach a reused program");
                Assert.That((float)pixel.B, Is.EqualTo(1).Within(0.01), "the script must actually execute");
            }
            Assert.That(resource.GlslPrograms.Statistics.Creations, Is.EqualTo(1));
            Assert.That(resource.GlslPrograms.Statistics.Hits, Is.EqualTo(1));
            resource.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resource.GlslPrograms.Create(ConstantBlueFragment, 1));
        });
    }

    [Test]
    public void ScriptCache_ReusesProgramsSeparatesInputLayoutsAndKeepsLeasesAlive()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cache = new ScriptGlslProgramCache();
            using GLSLShader first = cache.Create(ConstantBlueFragment, 1);
            using GLSLShader second = cache.Create(ConstantBlueFragment, 1);
            using GLSLShader differentLayout = cache.Create(ConstantBlueFragment, 2);
            Assert.Multiple(() =>
            {
                Assert.That(second.Pipeline, Is.SameAs(first.Pipeline));
                Assert.That(differentLayout.Pipeline, Is.Not.SameAs(first.Pipeline));
                Assert.That(cache.Statistics.Creations, Is.EqualTo(2));
            });

            first.Dispose();
            cache.Dispose();
            Assert.Throws<ObjectDisposedException>(() => cache.Create(ConstantBlueFragment, 1));
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using var input = new EffectTarget(red, new Rect(0, 0, 4, 4));
            using var targets = new EffectTargets();
            using EffectTarget output = second.Render(CreateCustomContext(targets), [input], input.Bounds, new DummyPush());
            using Bitmap pixels = output.RenderTarget!.Snapshot();
            Assert.That((float)pixels.GetPixelSpan<RgbaF16>()[0].B, Is.EqualTo(1).Within(0.01),
                "a checked-out shader must survive cache disposal until its own wrapper is disposed");
        });
    }

    [Test]
    public void Render_CombinesThreeInputsIntoExpandedOutputAndFeedsAnotherPass()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using RenderTarget green = CreateSolidTarget(2, 2, Colors.Lime);
            using RenderTarget blue = CreateSolidTarget(8, 8, Colors.Blue);
            using var first = new EffectTarget(red, new Rect(0, 0, 4, 4));
            using var second = new EffectTarget(green, new Rect(0, 0, 2, 2));
            using var third = new EffectTarget(blue, new Rect(0, 0, 8, 8));
            using var targets = new EffectTargets();
            using var pool = new RenderTargetPool(factory: null);
            using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery, red);
            var context = CreateCustomContext(targets, session);
            var bounds = new Rect(-3, -2, 10, 8);
            using var combine = GLSLShader.Create(ThreeInputFragment, inputCount: 3);
            using var invert = GLSLShader.Create(InvertFragment);

            using EffectTarget combined = combine.Render(context, [first, second, third], bounds, output =>
            {
                Assert.That(output.Bounds, Is.EqualTo(bounds));
                Assert.That(output.RenderTarget!.Width, Is.EqualTo(10));
                Assert.That(output.RenderTarget.Height, Is.EqualTo(8));
                return new DummyPush { Dummy = 0.25f };
            });
            using EffectTarget result = invert.Render(context, [combined], bounds, new DummyPush());
            using Bitmap pixels = result.RenderTarget!.Snapshot();
            RgbaF16 center = pixels.GetPixelSpan<RgbaF16>()[4 * pixels.Width + 5];
            using Bitmap original = red.Snapshot();
            RgbaF16 sourcePixel = original.GetPixelSpan<RgbaF16>()[0];

            Assert.Multiple(() =>
            {
                Assert.That(combine.InputCount, Is.EqualTo(3));
                Assert.That(result.Bounds, Is.EqualTo(bounds));
                Assert.That((float)center.R, Is.EqualTo(0.75).Within(0.01));
                Assert.That((float)center.G, Is.EqualTo(0).Within(0.01));
                Assert.That((float)center.B, Is.EqualTo(0).Within(0.01));
                Assert.That((float)center.A, Is.EqualTo(1).Within(0.01));
                Assert.That((float)sourcePixel.R, Is.EqualTo(1).Within(0.01), "inputs must remain owned and unchanged");
                Assert.That((float)sourcePixel.G, Is.EqualTo(0).Within(0.01));
            });
        });
    }

    [Test]
    public void Render_SameBoundsPreservesFractionalRasterFootprint()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using var input = new EffectTarget(red, new Rect(-0.25f, 0.25f, 4, 4));
            using var targets = new EffectTargets();
            var context = CreateCustomContext(targets);
            using var shader = GLSLShader.Create(ConstantBlueFragment);
            using EffectTarget output = shader.Render(context, [input], input.Bounds, new DummyPush());

            Assert.Multiple(() =>
            {
                Assert.That(output.RasterBounds, Is.EqualTo(input.RasterBounds));
                Assert.That(output.DeviceBounds, Is.EqualTo(input.DeviceBounds));
                Assert.That(output.Scale, Is.EqualTo(input.Scale));
            });
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void Render_ExpandedBoundsUsesContextDensity(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using var input = new EffectTarget(red, new Rect(0, 0, 4, 4));
            using var targets = new EffectTargets();
            var context = new CustomFilterEffectContext(
                targets, RenderIntent.Delivery, RenderRequestPurpose.Auxiliary, workingScale: density);
            using var shader = GLSLShader.Create(ConstantBlueFragment);
            var bounds = new Rect(-2, -2, 8, 8);
            using EffectTarget output = shader.Render(context, [input], bounds, destination =>
            {
                Assert.That(destination.Scale.Value, Is.EqualTo(density));
                return new DummyPush();
            });
            using Bitmap pixels = output.RenderTarget!.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(pixels.Width, Is.EqualTo((int)(8 * density)));
                Assert.That(pixels.Height, Is.EqualTo((int)(8 * density)));
                Assert.That(output.Bounds, Is.EqualTo(bounds));
                Assert.That((float)pixels.GetPixelSpan<RgbaF16>()[0].B, Is.EqualTo(1).Within(0.01));
            });
        });
    }

    [Test]
    public void Render_RejectsInputCountAndReleasesOutputWhenConstantFactoryThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using var input = new EffectTarget(red, new Rect(0, 0, 4, 4));
            using var targets = new EffectTargets();
            using var pool = new RenderTargetPool(factory: null);
            using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Delivery, red);
            var context = CreateCustomContext(targets, session);
            using var shader = GLSLShader.Create(ConstantBlueFragment);

            Assert.Throws<ArgumentException>(() => shader.Render(context, [], input.Bounds, new DummyPush()));
            Assert.That(pool.Statistics.Creates, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => shader.Render<DummyPush>(
                context, [input], new Rect(-1, -1, 6, 6), _ => throw new InvalidOperationException("constant failure")));
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            using EffectTarget recovered = shader.Render(context, [input], input.Bounds, new DummyPush());
            using Bitmap bitmap = recovered.RenderTarget!.Snapshot();
            Assert.That((float)bitmap.GetPixelSpan<RgbaF16>()[0].B, Is.EqualTo(1).Within(0.01));
        });
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void Render_RespectsAllocationFailureAndLeavesInputsAlive(RenderIntent intent)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget red = CreateSolidTarget(4, 4, Colors.Red);
            using var input = new EffectTarget(red, new Rect(0, 0, 4, 4));
            using var targets = new EffectTargets();
            using var pool = new RenderTargetPool(new FailAtTargetFactory(0));
            using RenderTargetLeaseSession session = pool.BeginSession(intent, red);
            var context = new CustomFilterEffectContext(
                targets, intent, RenderRequestPurpose.Auxiliary, renderTargetLeaseSession: session);
            using var shader = GLSLShader.Create(ConstantBlueFragment);

            if (intent == RenderIntent.Preview)
            {
                using EffectTarget result = shader.Render(context, [input], new Rect(-1, -1, 6, 6), new DummyPush());
                Assert.That(result.IsEmpty, Is.True);
                Assert.That(session.ContentDropObserved, Is.True);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() =>
                    shader.Render(context, [input], new Rect(-1, -1, 6, 6), new DummyPush()));
            }
            Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
            using Bitmap original = input.RenderTarget!.Snapshot();
            Assert.That((float)original.GetPixelSpan<RgbaF16>()[0].R, Is.EqualTo(1).Within(0.01));
        });
    }

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

    [TestCase(0)]
    [TestCase(1)]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void ApplyMultiPass_DeclinedPreviewScratchKeepsTheSourceAndReleasesEarlierLeases(
        int declineAt)
    {
        IGraphicsContext graphicsContext = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget source = RenderTarget.Create(4, 4)
                ?? throw new InvalidOperationException("Could not create the GLSL source target.");
            var factory = new FailAtTargetFactory(declineAt);
            using var pool = new RenderTargetPool(factory);
            using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview, source);
            using var targets = new EffectTargets
            {
                new EffectTarget(source, new Rect(0, 0, 4, 4)),
            };
            EffectTarget original = targets[0];
            var context = new CustomFilterEffectContext(
                targets,
                RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary,
                renderTargetLeaseSession: session);
            using var shader = GLSLShader.Create(ConstantBlueFragment);

            Assert.That(
                () => shader.ApplyMultiPass<DummyPush>(context, 3, static (_, _) => new DummyPush()),
                Throws.Nothing);

            Assert.Multiple(() =>
            {
                Assert.That(targets[0], Is.SameAs(original));
                Assert.That(session.ContentDropObserved, Is.True);
                Assert.That(pool.Statistics.LeasedTargets, Is.Zero);
                Assert.That(factory.CreateCalls, Is.EqualTo(declineAt + 1));
            });
            graphicsContext.WaitIdle();
            shader.Dispose();
            targets.Dispose();
            session.Dispose();
            pool.Dispose();
            source.Dispose();
            GpuResourceReclaimQueue.FlushAndDrain();
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
            using var registry = new RenderTargetPool(factory: null);
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
            using var registry = new RenderTargetPool(factory: null);
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
        RenderTargetPool registry,
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

    private sealed class FailAtTargetFactory(int failAt) : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            int index = CreateCalls++;
            if (index == failAt)
                return null;

            return RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height)
                ?? throw new InvalidOperationException("Could not create a GLSL scratch target.");
        }
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
