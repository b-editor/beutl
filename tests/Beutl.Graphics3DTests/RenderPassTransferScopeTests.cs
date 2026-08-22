using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Pins that a transfer issued while a render pass is recording takes its own submission.
/// </summary>
/// <remarks>
/// Meshes upload their vertex and index buffers lazily from inside the draw loop, which runs between
/// <see cref="IRenderPass3D.Begin"/> and <see cref="IRenderPass3D.End"/>. Vulkan forbids a transfer
/// inside a render pass instance, so appending one to the pass's own batch loses the upload: the mesh
/// then draws undefined vertices over the whole framebuffer, which a deferred renderer turns into a
/// black frame. The submission count is the observable part of that contract; the rendering
/// consequence is covered by the lit-framebuffer assertions in <see cref="Renderer3DTests"/>.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RenderPassTransferScopeTests
{
    private const int Width = 16;
    private const int Height = 8;

    private const string PassthroughFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D sourceTexture;

        void main() {
            outColor = texture(sourceTexture, fragCoord);
        }
        """;

    // Past VulkanRenderPass3D's 128-byte push-constant limit, so SetPushConstants rejects it.
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct OversizedPushConstants
    {
        public byte First;
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void CopyBufferInsideARenderPass_SubmitsAheadOfThatPass()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            uint[] payload = [0x11223344u, 0x55667788u, 0x99AABBCCu, 0xDDEEFF00u];
            ulong size = (ulong)(payload.Length * sizeof(uint));

            using IBuffer source = context.CreateBuffer(
                size,
                BufferUsage.TransferSource,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            using IBuffer destination = context.CreateBuffer(
                size,
                BufferUsage.VertexBuffer | BufferUsage.TransferDestination,
                MemoryProperty.DeviceLocal);
            source.Upload<uint>(payload);

            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using ITexture2D depth = context.CreateTexture2D(Width, Height, TextureFormat.Depth32Float);
            using IRenderPass3D renderPass = context.CreateRenderPass3D(
                [TextureFormat.RGBA8Unorm],
                TextureFormat.Depth32Float);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(renderPass, [color], depth);

            var duringPass = new List<VulkanCommandPoolEvent>();
            renderPass.Begin(framebuffer, [Colors.Transparent]);
            using (VulkanCommandPool.Observe(duringPass.Add))
            {
                context.CopyBuffer(source, destination, size);
            }

            renderPass.End();
            context.WaitIdle();

            Assert.That(
                duringPass.Count(static item => item == VulkanCommandPoolEvent.Submission),
                Is.EqualTo(1),
                "A transfer recorded inside a render pass must be submitted as its own batch, "
                + "not appended to the batch the pass is still recording.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void APassBodyFailure_ReleasesTheRenderPassScope()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using GLSLFilterPipeline pipeline = GLSLFilterPipeline.Create(
                    context,
                    PassthroughFragmentShader,
                    ShaderOutputCoverage.ProvablyFull)
                ?? throw new AssertionException("The passthrough filter pipeline could not be created.");
            using ITexture2D shaderSource = context.CreateTexture2D(Width, Height, TextureFormat.RGBA16Float);
            using ITexture2D shaderDestination = context.CreateTexture2D(Width, Height, TextureFormat.RGBA16Float);

            // Throws from VulkanRenderPass3D.SetPushConstants, between the pass's Begin and End.
            Assert.Throws<ArgumentException>(
                () => pipeline.Execute(shaderSource, shaderDestination, new OversizedPushConstants { First = 1 }));

            uint[] payload = [0x11223344u, 0x55667788u];
            ulong size = (ulong)(payload.Length * sizeof(uint));
            using IBuffer source = context.CreateBuffer(
                size,
                BufferUsage.TransferSource,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            using IBuffer destination = context.CreateBuffer(
                size,
                BufferUsage.VertexBuffer | BufferUsage.TransferDestination,
                MemoryProperty.DeviceLocal);
            source.Upload<uint>(payload);

            var afterFailure = new List<VulkanCommandPoolEvent>();
            using (VulkanCommandPool.Observe(afterFailure.Add))
            {
                context.CopyBuffer(source, destination, size);
            }

            Assert.That(
                afterFailure.Count(static item => item == VulkanCommandPoolEvent.Submission),
                Is.Zero,
                "A render pass whose body threw must release the render-pass scope, otherwise every later "
                + "transfer in the process takes its own out-of-band submission and the shared batch keeps "
                + "an unterminated render pass.");

            context.WaitIdle();
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void WaitIdleInsideARenderPass_DoesNotSubmitThatPass()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using ITexture2D depth = context.CreateTexture2D(Width, Height, TextureFormat.Depth32Float);
            using IRenderPass3D renderPass = context.CreateRenderPass3D(
                [TextureFormat.RGBA8Unorm],
                TextureFormat.Depth32Float);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(renderPass, [color], depth);

            var duringPass = new List<VulkanCommandPoolEvent>();
            renderPass.Begin(framebuffer, [Colors.Transparent]);
            using (VulkanCommandPool.Observe(duringPass.Add))
            {
                // What an out-of-tree Material3D.Resource can reach from EnsurePipeline or Bind.
                context.WaitIdle();
            }

            // Assert before End: unguarded, the pass's command buffer has already been freed by here,
            // so End would record into freed memory and take the test host down with it.
            Assert.That(
                duringPass.Count(static item => item == VulkanCommandPoolEvent.Submission),
                Is.Zero,
                "A synchronous flush inside a render pass must not submit the batch that pass is still "
                + "recording into.");

            renderPass.End();
            context.WaitIdle();
        });
    }
}
