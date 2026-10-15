using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[NonParallelizable]
public class GraphicsContextResourceTests
{
    [Test]
    public void IsolatedSubmission_DoesNotConsumeTheOpenRecordingBatch()
    {
        VulkanContext vulkanContext = VulkanTestEnvironment.EnsureVulkanContext();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            vulkanContext.WaitIdle();
            int releaseCount = 0;
            var events = new List<VulkanCommandPoolEvent>();

            using (VulkanCommandPool.Observe(events.Add))
            {
                vulkanContext.RecordCommands(static _ => { });
                vulkanContext.DeferRelease(() => releaseCount++);

                vulkanContext.SubmitIsolatedCommands(static _ => { });

                Assert.Multiple(() =>
                {
                    Assert.That(releaseCount, Is.Zero,
                        "The isolated submission must not retire resources owned by the open batch.");
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                        Is.EqualTo(1));
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.FenceWait),
                        Is.EqualTo(1));
                });

                vulkanContext.FlushCommands(waitForCompletion: true);
            }

            Assert.Multiple(() =>
            {
                Assert.That(releaseCount, Is.EqualTo(1));
                Assert.That(
                    events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                    Is.EqualTo(2));
                Assert.That(
                    events.Count(static item => item == VulkanCommandPoolEvent.FenceWait),
                    Is.EqualTo(2));
            });
        });
    }

    [Test]
    public void DeferredRelease_CanReenterCommandPoolWithoutDoubleCompletion()
    {
        VulkanContext vulkanContext = VulkanTestEnvironment.EnsureVulkanContext();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            vulkanContext.WaitIdle();
            int outerReleaseCount = 0;
            int nestedReleaseCount = 0;

            vulkanContext.RecordCommands(static _ => { });
            vulkanContext.DeferRelease(() =>
            {
                outerReleaseCount++;
                vulkanContext.DeferRelease(() => nestedReleaseCount++);
            });

            vulkanContext.FlushCommands(waitForCompletion: true);

            Assert.Multiple(() =>
            {
                Assert.That(outerReleaseCount, Is.EqualTo(1));
                Assert.That(nestedReleaseCount, Is.EqualTo(1));
            });
        });
    }

    [Test]
    public void CreateTexture2D_RGBA8_HasMatchingDimensions()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var texture = ctx.CreateTexture2D(64, 32, TextureFormat.RGBA8Unorm);

            Assert.That(texture.Width, Is.EqualTo(64));
            Assert.That(texture.Height, Is.EqualTo(32));
            Assert.That(texture.Format, Is.EqualTo(TextureFormat.RGBA8Unorm));
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

            Assert.That(buffer.Size, Is.EqualTo(size));
            buffer.Upload<int>(payload);

            IntPtr ptr = ((VulkanBuffer)buffer).Map();
            try
            {
                Assert.That(ptr, Is.Not.EqualTo(IntPtr.Zero));
                int[] roundTrip = new int[payload.Length];
                System.Runtime.InteropServices.Marshal.Copy(ptr, roundTrip, 0, payload.Length);
                Assert.That(roundTrip, Is.EqualTo(payload));
            }
            finally
            {
                ((VulkanBuffer)buffer).Unmap();
            }
        });
    }

    [Test]
    public void CompileShader_TrivialFragment_ProducesSpirv()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var compiler = ctx.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            const string source = """
                #version 450
                layout(location = 0) out vec4 outColor;
                void main() { outColor = vec4(1.0); }
                """;

            var spirv = compiler.CompileToSpirv(source, ShaderStage.Fragment);

            Assert.That(spirv.Length, Is.GreaterThan(0));
            Assert.That(spirv.Length % 4, Is.EqualTo(0), "SPIR-V は 4 バイト単位の語の列であるべきです。");
        });
    }

    [Test]
    public void ColorOnlyRenderPass_UsesMatchingFramebufferAndPipelineState()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const string vertexSource = """
                #version 450
                void main() {
                    vec2 positions[3] = vec2[](
                        vec2(-1.0, -1.0),
                        vec2(3.0, -1.0),
                        vec2(-1.0, 3.0));
                    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
                }
                """;
            const string fragmentSource = """
                #version 450
                layout(location = 0) out vec4 outColor;
                void main() { outColor = vec4(0.0, 0.0, 1.0, 1.0); }
                """;

            IShaderCompiler compiler = ctx.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            byte[] vertexSpirv = compiler.CompileToSpirv(vertexSource, ShaderStage.Vertex);
            byte[] fragmentSpirv = compiler.CompileToSpirv(fragmentSource, ShaderStage.Fragment);

            using ITexture2D color = ctx.CreateTexture2D(8, 8, TextureFormat.RGBA8Unorm);
            using IRenderPass3D renderPass = ctx.CreateRenderPass3D(
                [TextureFormat.RGBA8Unorm],
                depthFormat: null);
            using IFramebuffer3D framebuffer = ctx.CreateFramebuffer3D(
                renderPass,
                [color],
                depthTexture: null);

            Assert.That(
                () => ctx.CreatePipeline3D(
                    renderPass,
                    vertexSpirv,
                    fragmentSpirv,
                    [],
                    VertexInputDescription.Empty),
                Throws.ArgumentException.With.Message.Contains("depth attachment"));

            using IPipeline3D pipeline = ctx.CreatePipeline3D(
                renderPass,
                vertexSpirv,
                fragmentSpirv,
                [],
                VertexInputDescription.Empty,
                PipelineOptions.Fullscreen);

            renderPass.Begin(framebuffer, [default]);
            renderPass.BindPipeline(pipeline);
            renderPass.Draw(3);
            renderPass.End();
            ctx.WaitIdle();
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
}
