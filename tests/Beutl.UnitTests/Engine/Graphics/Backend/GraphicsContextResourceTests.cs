using Beutl.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[NonParallelizable]
public class GraphicsContextResourceTests
{
    [Test]
    public void CreateTexture2D_RGBA8_HasMatchingDimensions()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var texture = ctx.CreateTexture2D(64, 32, TextureFormat.RGBA8Unorm);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.Width, Is.EqualTo(64));
            Assert.That(texture.Height, Is.EqualTo(32));
            Assert.That(texture.Format, Is.EqualTo(TextureFormat.RGBA8Unorm));
            Assert.That(texture.NativeHandle, Is.Not.EqualTo(IntPtr.Zero));
        });
    }

    [Test]
    public void CreateTexture2D_DepthFormat_Succeeds()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var depth = ctx.CreateTexture2D(8, 8, TextureFormat.Depth32Float);
            Assert.That(depth.Format, Is.EqualTo(TextureFormat.Depth32Float));
        });
    }

    [Test]
    public void CreateTexture2D_UploadAndDownload_RoundTrips()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int width = 4;
            const int height = 4;
            using var texture = ctx.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)(i & 0xFF);

            texture.Upload(pixels);
            var downloaded = texture.DownloadPixels();

            Assert.That(downloaded.Length, Is.EqualTo(pixels.Length));
            Assert.That(downloaded, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void CreateBuffer_HostVisible_AllocatesRequestedSize()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var buffer = ctx.CreateBuffer(
                256,
                BufferUsage.UniformBuffer | BufferUsage.TransferDestination,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            Assert.That(buffer.Size, Is.EqualTo(256));
        });
    }

    [Test]
    public void CreateBuffer_HostVisible_UploadAndMap_RoundTrips()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            int[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
            ulong size = (ulong)(sizeof(int) * payload.Length);
            using var buffer = ctx.CreateBuffer(
                size,
                BufferUsage.UniformBuffer | BufferUsage.TransferDestination,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            buffer.Upload<int>(payload);

            IntPtr ptr = buffer.Map();
            try
            {
                Assert.That(ptr, Is.Not.EqualTo(IntPtr.Zero));
                int[] roundTrip = new int[payload.Length];
                System.Runtime.InteropServices.Marshal.Copy(ptr, roundTrip, 0, payload.Length);
                Assert.That(roundTrip, Is.EqualTo(payload));
            }
            finally
            {
                buffer.Unmap();
            }
        });
    }

    [Test]
    public void CreateShaderCompiler_ReturnsCompilerInstance()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var compiler = ctx.CreateShaderCompiler();
            Assert.That(compiler, Is.Not.Null);
        });
    }

    [Test]
    public void CompileShader_TrivialFragment_ProducesSpirv()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var compiler = ctx.CreateShaderCompiler();
            const string source = """
                #version 450
                layout(location = 0) out vec4 outColor;
                void main() { outColor = vec4(1.0); }
                """;

            var spirv = compiler.CompileToSpirv(source, ShaderStage.Fragment);

            Assert.That(spirv, Is.Not.Null);
            Assert.That(spirv.Length, Is.GreaterThan(0));
            Assert.That(spirv.Length % 4, Is.EqualTo(0), "SPIR-V は 4 バイト単位の語の列であるべきです。");
        });
    }

    [Test]
    public void CreateSampler_ReturnsSamplerInstance()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var sampler = ctx.CreateSampler();
            Assert.That(sampler, Is.Not.Null);
        });
    }

    [Test]
    public void CopyTexture_PreservesContents()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const int width = 4;
            const int height = 4;
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)((i * 7) & 0xFF);

            using var src = ctx.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
            using var dst = ctx.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
            src.Upload(pixels);

            ctx.CopyTexture(src, dst);
            ctx.WaitIdle();

            var copied = dst.DownloadPixels();
            Assert.That(copied, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void WaitIdle_DoesNotThrow()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() => ctx.WaitIdle());
    }
}
