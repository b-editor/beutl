using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

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
    public void ExecuteSingleTarget_AfterDispose_Throws()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                shader.ExecuteSingleTarget<DummyPush>(null!, null!, null!, new DummyPush()));
        });
    }

    [Test]
    public void ExecuteSingleTarget_OverwritesDestinationWithShaderOutput()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // A 4x4 red source so the shader's blue overwrite is detectable.
            using var sourceRenderTarget = RenderTarget.Create(4, 4);
            Assume.That(sourceRenderTarget, Is.Not.Null);
            Assume.That(sourceRenderTarget!.Texture, Is.Not.Null);
            using (var canvas = new ImmediateCanvas(sourceRenderTarget))
            {
                canvas.Clear(Colors.Red);
            }

            sourceRenderTarget.PrepareForSampling();

            using Beutl.Graphics.Backend.ITexture2D destination =
                ctx.CreateTexture2D(4, 4, Beutl.Graphics.Backend.TextureFormat.RGBA16Float);
            using Beutl.Graphics.Backend.ITexture2D depth =
                ctx.CreateTexture2D(4, 4, Beutl.Graphics.Backend.TextureFormat.Depth32Float);

            using var shader = GLSLShader.Create(ConstantBlueFragment);
            shader.ExecuteSingleTarget(sourceRenderTarget.Texture!, destination, depth, new DummyPush());

            ctx.WaitIdle();

            byte[] pixels = destination.DownloadPixels();
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
    public void ExecuteSingleTarget_SourcePreparationFailureIsClassified()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D source = ctx.CreateTexture2D(4, 4, TextureFormat.RGBA16Float);
            using ITexture2D destination = ctx.CreateTexture2D(4, 4, TextureFormat.RGBA16Float);
            using ITexture2D depth = ctx.CreateTexture2D(4, 4, TextureFormat.Depth32Float);
            using var shader = GLSLShader.Create(ConstantBlueFragment);
            var failingSource = new SamplingFailureTexture(source);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                shader.ExecuteSingleTarget(failingSource, destination, depth, new DummyPush()))!;

            Assert.That(ComputeBackendPreparationFailure.IsMarked(error), Is.True,
                "a texture-layout failure inside GLSL dispatch must bypass identity preview fallback");
        });
    }

    private sealed class SamplingFailureTexture(ITexture2D inner) : ITexture2D
    {
        public int Width => inner.Width;

        public int Height => inner.Height;

        public TextureFormat Format => inner.Format;

        public IntPtr NativeHandle => inner.NativeHandle;

        public IntPtr NativeViewHandle => inner.NativeViewHandle;

        public void Upload(ReadOnlySpan<byte> data) => inner.Upload(data);

        public byte[] DownloadPixels() => inner.DownloadPixels();

        public SkiaSharp.SKSurface CreateSkiaSurface() => inner.CreateSkiaSurface();

        public void PrepareForRender() => inner.PrepareForRender();

        public void PrepareForSampling() =>
            throw new InvalidOperationException("simulated GLSL source preparation failure");

        public void Dispose()
        {
        }
    }
}
