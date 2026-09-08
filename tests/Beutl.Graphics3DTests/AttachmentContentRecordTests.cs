using Beutl.Graphics.Backend;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class AttachmentContentRecordTests
{
    private const int Width = 16;
    private const int Height = 8;

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TheBufferBudget_DoesNotExceedWhatTheDeviceCanAttach()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int budget = Beutl.Graphics.Rendering.RenderScaleUtilities.ResolveMaxBufferDimension();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    context.MaxAttachmentDimension,
                    Is.GreaterThan(0),
                    "precondition: the device has to report what it can attach");
                Assert.That(
                    budget,
                    Is.LessThanOrEqualTo(context.MaxAttachmentDimension),
                    "the budget decides how large a render target may be, so it cannot exceed the device");
                Assert.That(
                    budget,
                    Is.LessThanOrEqualTo(
                        Beutl.Graphics.Rendering.RenderScaleUtilities.MaxBufferDimension),
                    "nor the engine's own ceiling");
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void UsingAClearedTextureAsAnAttachment_StopsItReportingTransparentContents()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IRenderPass3D pass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using ITexture2D color = context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
            using IFramebuffer3D framebuffer = context.CreateFramebuffer3D(pass, [color], null);

            var clearable = (ITransparentClearableTexture)color;
            clearable.ClearToTransparent();
            bool transparentBeforeThePass = clearable.HasTransparentContents;

            framebuffer.PrepareForRendering();
            bool transparentAfterThePass = clearable.HasTransparentContents;

            context.WaitIdle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    transparentBeforeThePass,
                    Is.True,
                    "precondition: a recorded clear is what the record is for");
                Assert.That(
                    transparentAfterThePass,
                    Is.False,
                    "a pass writes its attachments, so the record cannot still say transparent");
            }
        });
    }
}
