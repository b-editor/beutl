using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderDescriptionSpirvEquivalenceTests
{
    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    private const string NativeIdentitySource =
        """
        #version 450
        layout(set = 0, binding = 0) uniform sampler2D src;
        layout(push_constant) uniform PushConstants { ivec4 sourceTexelOffset; } constants;
        layout(location = 0) out vec4 outColor;

        void main()
        {
            ivec2 sourceCoord = ivec2(gl_FragCoord.xy) + constants.sourceTexelOffset.xy;
            outColor = texelFetch(src, sourceCoord, 0);
        }
        """;

    private static readonly ShaderDescription s_nativeIdentity = ShaderDescription.CurrentPixel(
        new SkslSource("half4 apply(half4 color) { return color; }", ShaderDescriptionKind.CurrentPixel),
        new SpirvShaderLowering(NativeIdentitySource, []),
        bindings: null);

    [Test]
    public void ShaderCompiler_ReusesProcessLifetimeApiAfterInstanceDispose()
    {
        byte[] first;
        using (var compiler = new VulkanShaderCompiler())
            first = compiler.CompileToSpirv(NativeIdentitySource, ShaderStage.Fragment);

        using var secondCompiler = new VulkanShaderCompiler();
        byte[] second = secondCompiler.CompileToSpirv(NativeIdentitySource, ShaderStage.Fragment);

        Assert.That(second, Is.EqualTo(first).And.Not.Empty);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [Category("GpuPassFusionGpu")]
    public void LumaColor_AutoAlwaysUsesSksl(float outputScale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            (ushort[] expected, RenderExecutionStatistics expectedStatistics) =
                Render(ShaderBackendPreference.Sksl, BuiltInColorFilterShader.LumaColor(), outputScale);
            (ushort[] actual, RenderExecutionStatistics actualStatistics) =
                Render(ShaderBackendPreference.Auto, BuiltInColorFilterShader.LumaColor(), outputScale);

            Assert.Multiple(() =>
            {
                Assert.That(expectedStatistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(actualStatistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(
                    actual.Select(static value => (float)BitConverter.UInt16BitsToHalf(value)),
                    Has.All.Matches<float>(float.IsFinite));
                Assert.That(expected, Has.Some.Not.Zero, "the comparison must not be vacuous");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    [Platform("MacOSX")]
    public void Identity_ExplicitSpirvMatchesSksl()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            (ushort[] expected, _) = Render(
                ShaderBackendPreference.Sksl,
                s_nativeIdentity);
            (ushort[] actual, RenderExecutionStatistics statistics) = Render(
                ShaderBackendPreference.Spirv,
                s_nativeIdentity);

            Assert.Multiple(() =>
            {
                Assert.That(statistics.SpirvShaderRunExecutions, Is.EqualTo(1));
                Assert.That(actual, Is.EqualTo(expected));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    [Platform("MacOSX")]
    public void Identity_SubmitsNativeCommandsBeforeExecutionReturns()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var events = new List<VulkanCommandPoolEvent>();

            (_, RenderExecutionStatistics statistics) = Render(
                ShaderBackendPreference.Auto,
                s_nativeIdentity,
                commandEvents: events);

            Assert.Multiple(() =>
            {
                Assert.That(statistics.SpirvShaderRunExecutions, Is.EqualTo(1));
                Assert.That(
                    events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                    Is.GreaterThanOrEqualTo(1));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void Identity_AutoUsesSkslForACroppedFootprint()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var requestedRegion = new Rect(4, 3, 12, 8);
            (ushort[] expected, _) = Render(
                ShaderBackendPreference.Sksl,
                s_nativeIdentity,
                requestedRegion: requestedRegion);
            (ushort[] actual, RenderExecutionStatistics statistics) = Render(
                ShaderBackendPreference.Auto,
                s_nativeIdentity,
                requestedRegion: requestedRegion);

            Assert.Multiple(() =>
            {
                Assert.That(statistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(actual, Is.EqualTo(expected));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AutoFallsBackAndRetriesWhenNativeCompilationFails()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var description = ShaderDescription.CurrentPixel(
                new SkslSource(
                    "half4 apply(half4 color) { return color; }",
                    ShaderDescriptionKind.CurrentPixel),
                new SpirvShaderLowering("#version 450\nthis is not valid GLSL", []),
                bindings: null);
            (ushort[] expected, _) = Render(
                ShaderBackendPreference.Sksl,
                description);
            using ProgramCache<GLSLFilterPipeline> cache = SpirvShaderProgramCache.Create();
            (ushort[] actual, RenderExecutionStatistics statistics) = Render(
                ShaderBackendPreference.Auto,
                description,
                spirvProgramCache: cache);
            (ushort[] retried, RenderExecutionStatistics retryStatistics) = Render(
                ShaderBackendPreference.Auto,
                description,
                spirvProgramCache: cache);

            Assert.Multiple(() =>
            {
                Assert.That(statistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(retryStatistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(retried, Is.EqualTo(expected));
                Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
                Assert.That(cache.Statistics.Creations, Is.Zero);
                Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    [Platform("MacOSX")]
    public void Renderer_ReusesNativeTestProgram()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Brush.Resource gradient = CreateGradient();
            using var source = new RectangleRenderNode(s_bounds, gradient, pen: null);
            using var root = new MaterializedShaderNode(
                source,
                s_nativeIdentity);
            using var renderer = new RenderNodeRenderer(
                root,
                new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                });

            using RenderNodeRasterization cold = renderer.Rasterize();
            ProgramCacheStatistics coldCache = renderer.ProgramCacheStatistics;
            RenderTargetPoolStatistics coldTargets = renderer.TargetPoolStatistics;
            RenderExecutionStatistics coldExecution = renderer.LastExecutionStatistics;
            using RenderNodeRasterization warm = renderer.Rasterize();
            ProgramCacheStatistics warmCache = renderer.ProgramCacheStatistics;
            RenderTargetPoolStatistics warmTargets = renderer.TargetPoolStatistics;
            RenderExecutionStatistics warmExecution = renderer.LastExecutionStatistics;

            Assert.Multiple(() =>
            {
                Assert.That(cold.Bitmap, Is.Not.Null);
                Assert.That(cold.Bitmap!.GetPixelSpan<ushort>().ToArray(), Has.Some.Not.Zero);
                Assert.That(coldExecution.SpirvShaderRunExecutions, Is.EqualTo(1));
                Assert.That(warmExecution.SpirvShaderRunExecutions, Is.EqualTo(1));
                Assert.That(coldCache.Creations, Is.EqualTo(1));
                Assert.That(warmCache.Creations, Is.EqualTo(1));
                Assert.That(warmCache.Hits, Is.GreaterThanOrEqualTo(1));
                Assert.That(warmCache.RetainedPrograms, Is.EqualTo(1));
                Assert.That(warmTargets.Creates, Is.EqualTo(coldTargets.Creates));
            });
        });
    }

    private static (ushort[] Pixels, RenderExecutionStatistics Statistics) Render(
        ShaderBackendPreference backendPreference,
        ShaderDescription description,
        float outputScale = 1,
        Rect? requestedRegion = null,
        ProgramCache<GLSLFilterPipeline>? spirvProgramCache = null,
        ICollection<VulkanCommandPoolEvent>? commandEvents = null)
    {
        using Brush.Resource gradient = CreateGradient();
        using var source = new RectangleRenderNode(s_bounds, gradient, pen: null);
        using var root = new MaterializedShaderNode(source, description);
        using CompiledRenderRequest compiled = Compile(root, outputScale, requestedRegion);
        using var registry = new RenderTargetPool(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview);
        using RenderTargetLease output = targets.Acquire(PixelRect.FromRect(s_bounds, outputScale).Size);
        using var canvas = new ImmediateCanvas(
            output.Target,
            RenderIntent.Preview,
            outputScale,
            outputScale,
            s_bounds.Size);
        canvas.Clear();
        var executor = new RenderRequestExecutor(
            targets,
            spirvProgramCache: spirvProgramCache,
            shaderBackendPreference: backendPreference);
        if (commandEvents is null)
        {
            executor.Execute(compiled, canvas, replayBounds: requestedRegion ?? s_bounds);
        }
        else
        {
            using (VulkanCommandPool.Observe(commandEvents.Add))
                executor.Execute(compiled, canvas, replayBounds: requestedRegion ?? s_bounds);
        }
        using Bitmap bitmap = output.Target.Snapshot();
        return (bitmap.GetPixelSpan<ushort>().ToArray(), executor.Statistics);
    }

    private static Brush.Resource CreateGradient()
    {
        var gradient = new LinearGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 17, 31), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(192, 23, 240, 83), 0.33f));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(96, 19, 47, 255), 0.67f));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(255, 241, 193, 7), 1));
        return (Brush.Resource)gradient.ToResource(CompositionContext.Default);
    }

    private static CompiledRenderRequest Compile(
        RenderNode root,
        float outputScale,
        Rect? requestedRegion)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            requestedRegion: requestedRegion,
            outputScale: outputScale,
            maxWorkingScale: outputScale,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
            return new RenderRequestCompiler().Compile(
                request,
                graph,
                SkslBackendBudgetResolver.Portable);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private sealed class MaterializedShaderNode(
        RenderNode source,
        ShaderDescription description) : RenderNode
    {
        private readonly ShaderDescription _description = description;

        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.RecordSubtree(source))
            {
                RenderFragmentHandle output = context.Shader(input, _description);
                context.Publish(context.Blend(output, BlendMode.SrcOver));
            }
        }
    }
}
