using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Pins that a transfer issued while a render pass is recording splits that pass instead of taking its
/// own submission.
/// </summary>
/// <remarks>
/// Meshes upload their vertex and index buffers lazily from inside the draw loop, which runs between
/// <see cref="IRenderPass3D.Begin"/> and <see cref="IRenderPass3D.End"/>. Vulkan forbids a transfer
/// inside a render pass instance, so appending one to the pass's own batch loses the upload: the mesh
/// then draws undefined vertices over the whole framebuffer, which a deferred renderer turns into a
/// black frame. Giving the transfer its own batch avoids that but submits it ahead of the pass, so every
/// draw already recorded in the pass runs after work requested later than it. Ending the instance,
/// recording the transfer, and beginning it again keeps the whole sequence on one command buffer in
/// recording order, which is the only arrangement that is right for both. The submission count is the
/// observable part of that contract; the rendering consequence is covered by the lit-framebuffer
/// assertions in <see cref="Renderer3DTests"/>.
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

    /// <summary>Reads its geometry from a vertex buffer, its texture from a descriptor set, and its tint
    /// from push constants, so a draw is only correct when all three are bound.</summary>
    private const string TexturedQuadVertexShader = """
        #version 450

        layout(location = 0) in vec2 inPosition;
        layout(location = 0) out vec2 fragCoord;

        void main() {
            gl_Position = vec4(inPosition, 0.0, 1.0);
            fragCoord = inPosition * 0.5 + 0.5;
        }
        """;

    private const string TintedFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D sourceTexture;
        layout(push_constant) uniform PushConstants { vec4 tint; } pc;

        void main() {
            outColor = texture(sourceTexture, fragCoord) * pc.tint;
        }
        """;

    // Past VulkanRenderPass3D's 128-byte push-constant limit, so SetPushConstants rejects it.
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct OversizedPushConstants
    {
        public byte First;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TintPushConstants
    {
        public float Red;
        public float Green;
        public float Blue;
        public float Alpha;
    }

    /// <remarks>
    /// A claimed render-pass scope sends every barrier through the split path, and a scope claimed before
    /// its instance is open cannot split, so it falls back to a batch of its own submitted ahead. That is
    /// wrong for the pass's own attachment transitions: those have to stay in recording order behind
    /// whatever was recorded before them, or the queue runs them against an image that has since moved to a
    /// different layout. The batch is therefore claimed at the command that opens the instance, not before.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void BeginningAPass_KeepsItsPreparationBarriersInTheRecordedBatch()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IRenderPass3D pass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, [color], null);

            // Put the attachment somewhere Begin has to transition it back from, so the barrier this test is
            // about is recorded on every backend. A texture that is already an attachment - which is how the
            // Metal-backed one arrives - would make the observation below vacuous.
            framebuffer.PrepareForSampling();

            var duringSampling = new List<VulkanCommandPoolEvent>();
            var duringBegin = new List<VulkanCommandPoolEvent>();
            using (VulkanCommandPool.Observe(duringSampling.Add))
            {
                framebuffer.PrepareForSampling();
            }

            using (VulkanCommandPool.Observe(duringBegin.Add))
            {
                pass.Begin(framebuffer, [Colors.Transparent]);
            }

            pass.End();
            context.WaitIdle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    duringSampling.Count(static item => item == VulkanCommandPoolEvent.Submission),
                    Is.Zero,
                    "precondition: an ordinary barrier outside a pass joins the recorded batch");
                Assert.That(
                    duringBegin.Count(static item => item == VulkanCommandPoolEvent.Submission),
                    Is.Zero,
                    "Opening a render pass must not submit anything: its attachment transitions belong to "
                    + "the batch already being recorded, in the order they were recorded.");
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void CopyBufferInsideARenderPass_SplitsThatPassInsteadOfSubmittingAheadOfIt()
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
                Is.Zero,
                "A transfer recorded inside a render pass must split that pass and stay on its batch, so "
                + "it lands after the draws already recorded there rather than ahead of all of them.");
        });
    }

    /// <remarks>
    /// A readback records its image-to-buffer copy on the recording batch and then maps the staging buffer,
    /// so it is only correct if the synchronous flush in between actually submits that batch. Inside a
    /// render pass the batch belongs to the pass, and withholding it hands the caller the memory as it was
    /// before the copy.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void AReadbackInsideARenderPass_SeesWhatWasRecordedBeforeIt()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D probe = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            byte[] opaque = new byte[Width * Height * 4];
            Array.Fill(opaque, (byte)0xFF);
            probe.Upload(opaque);

            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using IRenderPass3D renderPass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(renderPass, [color], null);

            renderPass.Begin(framebuffer, [Colors.Transparent]);
            byte[] insidePass = probe.DownloadPixels();
            renderPass.End();
            context.WaitIdle();

            byte[] outsidePass = probe.DownloadPixels();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    outsidePass,
                    Is.EqualTo(opaque),
                    "precondition: the probe really does hold what was uploaded");
                Assert.That(
                    insidePass,
                    Is.EqualTo(opaque),
                    "a readback inside a render pass must return the pixels, not whatever the staging "
                    + "buffer happened to hold before the copy ran");
            }
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


    /// <remarks>
    /// A flush during a suspension submits the batch, so the instance resumes on a command buffer that was
    /// allocated after the caller made its bindings. Everything a draw reads other than the pipeline - the
    /// vertex and index buffers, the descriptor set, the push constants - is state of that command buffer,
    /// not of any object, so a resume that restores only the pipeline hands the following draw undefined
    /// vertices, no texture, and undefined push constants. Drawing what was bound before the split is the
    /// whole point of splitting rather than submitting ahead, and under the validation layer this test also
    /// fails on the missing bindings themselves rather than only on the pixels they produce.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ADrawAfterAResumedPass_StillSeesEveryBindingMadeBeforeTheSplit()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            IShaderCompiler compiler = context.CreateShaderCompiler();
            byte[] vertexSpirv = compiler.CompileToSpirv(TexturedQuadVertexShader, ShaderStage.Vertex);
            byte[] fragmentSpirv = compiler.CompileToSpirv(TintedFragmentShader, ShaderStage.Fragment);

            // Opaque white, so the drawn color is the push-constant tint alone.
            using ITexture2D sourceTexture = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            byte[] white = new byte[Width * Height * 4];
            Array.Fill(white, (byte)0xFF);
            sourceTexture.Upload(white);

            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using IRenderPass3D renderPass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(renderPass, [color], null);
            using ISampler sampler = context.CreateSampler();

            var vertexInput = new VertexInputDescription
            {
                Bindings =
                [
                    new VertexBindingDescription
                    {
                        Binding = 0,
                        Stride = sizeof(float) * 2,
                        InputRate = VertexInputRate.Vertex,
                    }
                ],
                Attributes =
                [
                    new VertexAttributeDescription
                    {
                        Location = 0,
                        Binding = 0,
                        Format = VertexFormat.Float2,
                        Offset = 0,
                    }
                ],
            };

            using IPipeline3D pipeline = context.CreatePipeline3D(
                renderPass,
                vertexSpirv,
                fragmentSpirv,
                [new DescriptorBinding(0, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)],
                vertexInput,
                PipelineOptions.Fullscreen);
            using IDescriptorSet descriptorSet = context.CreateDescriptorSet(
                pipeline,
                [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 1)]);
            descriptorSet.UpdateTexture(0, sourceTexture, sampler);

            float[] corners = [-1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f];
            uint[] quadIndices = [0, 1, 2, 0, 2, 3];
            using IBuffer vertexBuffer = context.CreateBuffer(
                (ulong)(corners.Length * sizeof(float)),
                BufferUsage.VertexBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            using IBuffer indexBuffer = context.CreateBuffer(
                (ulong)(quadIndices.Length * sizeof(uint)),
                BufferUsage.IndexBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            vertexBuffer.Upload<float>(corners);
            indexBuffer.Upload<uint>(quadIndices);

            var tint = new TintPushConstants { Red = 1f, Green = 0f, Blue = 0.5f, Alpha = 1f };

            renderPass.Begin(framebuffer, [Colors.Transparent]);
            renderPass.BindPipeline(pipeline);
            renderPass.BindVertexBuffer(vertexBuffer);
            renderPass.BindIndexBuffer(indexBuffer);
            renderPass.BindDescriptorSet(pipeline, descriptorSet);
            renderPass.SetPushConstants(tint);

            // Splits the pass onto a freshly allocated command buffer, between the bindings and the draw.
            context.WaitIdle();

            renderPass.DrawIndexed((uint)quadIndices.Length);
            renderPass.End();
            context.WaitIdle();

            byte[] drawn = color.DownloadPixels();
            int center = (((Height / 2) * Width) + (Width / 2)) * 4;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    drawn[center],
                    Is.EqualTo(255).Within(2),
                    "the push-constant tint must survive the split: red");
                Assert.That(
                    drawn[center + 1],
                    Is.EqualTo(0).Within(2),
                    "the push-constant tint must survive the split: green");
                Assert.That(
                    drawn[center + 2],
                    Is.EqualTo(128).Within(2),
                    "the push-constant tint must survive the split: blue");
                Assert.That(
                    drawn[center + 3],
                    Is.EqualTo(255).Within(2),
                    "the quad must actually be drawn, which needs the vertex and index buffers and the "
                    + "descriptor set to be bound on the command buffer the pass resumed onto");
            }
        });
    }

    /// <remarks>
    /// A caller that waits is owed everything it recorded. Withholding the batch because a pass owns it
    /// leaves a readback mapping memory the copy has not reached, so the flush ends the instance, submits,
    /// and begins the instance again on the next batch.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void WaitIdleInsideARenderPass_SubmitsWhatWasRecordedAndResumesThePass()
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

            Assert.That(
                duringPass.Count(static item => item == VulkanCommandPoolEvent.Submission),
                Is.GreaterThan(0),
                "A synchronous flush inside a render pass must submit what the caller recorded, or a "
                + "readback maps memory the copy has not reached.");

            renderPass.End();
            context.WaitIdle();
        });
    }
}
