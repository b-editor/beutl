using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
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
    /// <summary>
    /// Adjacent RgbaF16 codes. The two paths are compiled by different shader compilers, so they are
    /// only guaranteed to agree to within their rounding: a backend whose <c>half</c> is real fp16
    /// (Metal through MoltenVK) settles a result on either neighbouring code, while one that
    /// evaluates <c>half</c> at float precision (SwiftShader) reproduces the bits exactly. A lowering
    /// that dropped the premultiply, transposed a channel, or lost the uniform moves a channel by far
    /// more than one code.
    /// </summary>
    private const int MaximumLoweringStorageCodeDistance = 1;

    private static readonly Rect s_bounds = new(0, 0, 24, 16);

    [TestCase(0f)]
    [TestCase(0.125f)]
    [TestCase(0.375f)]
    [TestCase(0.73f)]
    [TestCase(1f)]
    [Category("GpuPassFusionGpu")]
    public void OpacityDescription_NativeSpirvMatchesSkslExactly(float opacity)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            (ushort[] source, RenderExecutionStatistics sourceStatistics) =
                Render(ShaderBackendPreference.Sksl, 1);
            (ushort[] sksl, RenderExecutionStatistics expectedStatistics) =
                Render(ShaderBackendPreference.Sksl, opacity);
            ushort[] spirv = RenderNativeSpirv(source, opacity);

            Assert.Multiple(() =>
            {
                Assert.That(sourceStatistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(expectedStatistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(
                    MaximumStorageCodeDistance(spirv, sksl),
                    Is.LessThanOrEqualTo(MaximumLoweringStorageCodeDistance),
                    "The native SPIR-V lowering must reproduce the SkSL premultiplied-linear RGBA16F "
                    + "result to within the rounding the two shader compilers are free to differ by.");
                if (opacity > 0)
                    Assert.That(sksl, Has.Some.Not.Zero, "the comparison must not be vacuous");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void OpacityDescription_AutoUsesBitExactSkslFallback()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            (ushort[] expected, _) = Render(ShaderBackendPreference.Sksl, 0.375f);
            (ushort[] actual, RenderExecutionStatistics statistics) =
                Render(ShaderBackendPreference.Auto, 0.375f);

            Assert.Multiple(() =>
            {
                Assert.That(statistics.SpirvShaderRunExecutions, Is.Zero);
                Assert.That(actual, Is.EqualTo(expected));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void OpacityDescription_ExplicitSpirvReportsNonExactSkiaHandoff()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Assert.That(
                () => Render(ShaderBackendPreference.Spirv, 0.375f),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("cannot be handed to the Skia compositor bit-exactly"));
        });
    }

    // Half is sign-magnitude, so its raw codes are not monotonic across zero. Mirroring the negative
    // half restores the ordering that makes adjacent representable values exactly one apart.
    private static int MaximumStorageCodeDistance(ushort[] left, ushort[] right)
    {
        Assert.That(left, Has.Length.EqualTo(right.Length));
        int maximum = 0;
        for (int i = 0; i < left.Length; i++)
            maximum = Math.Max(maximum, Math.Abs(OrderedHalfCode(left[i]) - OrderedHalfCode(right[i])));
        return maximum;
    }

    private static int OrderedHalfCode(ushort bits)
    {
        int magnitude = bits & 0x7FFF;
        return (bits & 0x8000) != 0 ? -magnitude : magnitude;
    }

    private static ushort[] RenderNativeSpirv(ushort[] sourcePixels, float opacity)
    {
        IGraphicsContext context = GpuTestEnvironment.SharedContext;
        using ITexture2D source = context.CreateTexture2D(24, 16, TextureFormat.RGBA16Float);
        using ITexture2D destination = context.CreateTexture2D(24, 16, TextureFormat.RGBA16Float);
        source.Upload(MemoryMarshal.AsBytes(sourcePixels.AsSpan()));

        SpirvShaderLowering lowering = OpacityRenderNode.CreateFusionDescription(opacity).SpirvLowering
            ?? throw new AssertionException("The opacity description must provide a native SPIR-V lowering.");
        using GLSLFilterPipeline pipeline = GLSLFilterPipeline.Create(
                context,
                lowering.FragmentShaderSource,
                ShaderOutputCoverage.ProvablyFull)
            ?? throw new AssertionException("The native opacity pipeline could not be created.");
        SpirvPushConstants pushConstants = default;
        Span<byte> bytes = pushConstants;
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.Slice(SpirvPushConstants.UserByteOffset, sizeof(float)),
            BitConverter.SingleToInt32Bits(opacity));

        pipeline.Execute(source, destination, pushConstants);
        byte[] result = destination.DownloadPixels();
        return MemoryMarshal.Cast<byte, ushort>(result).ToArray();
    }

    private static (ushort[] Pixels, RenderExecutionStatistics Statistics) Render(
        ShaderBackendPreference backendPreference,
        float opacity)
    {
        using Brush.Resource gradient = CreateGradient();
        using var source = new RectangleRenderNode(s_bounds, gradient, pen: null);
        using var root = new OpacityShaderNode(source, opacity);
        using CompiledRenderRequest compiled = Compile(root);
        using var registry = new RenderTargetLeaseRegistry(factory: null);
        using RenderTargetLeaseSession targets = registry.BeginSession(RenderIntent.Preview);
        using RenderTargetLease output = targets.Acquire(PixelRect.FromRect(s_bounds, 1).Size);
        using var canvas = new ImmediateCanvas(output.Target, RenderIntent.Preview, 1, 1, s_bounds.Size);
        canvas.Clear();
        var executor = new RenderRequestExecutor(
            targets,
            shaderBackendPreference: backendPreference);
        executor.Execute(compiled, canvas, replayBounds: s_bounds);
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

    private static CompiledRenderRequest Compile(RenderNode root)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            targetDomain: s_bounds,
            outputScale: 1,
            maxWorkingScale: 1,
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

    private sealed class OpacityShaderNode(RenderNode source, float opacity) : RenderNode
    {
        private readonly ShaderDescription _description =
            OpacityRenderNode.CreateFusionDescription(opacity);

        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.RecordSubtree(source))
                context.Publish(context.Shader(input, _description));
        }
    }
}
