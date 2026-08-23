using Beutl.Graphics.Backend;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Pins that what the backend records about a texture's contents survives contact with a render pass.
/// </summary>
/// <remarks>
/// The record exists so a caller that wants a blank target can skip a clear that would change nothing. A
/// pass writes its attachments, so a record left saying "transparent" across one would make that caller
/// skip a clear it needed and read the pass's output instead.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class AttachmentContentRecordTests
{
    private const int Width = 16;
    private const int Height = 8;

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
