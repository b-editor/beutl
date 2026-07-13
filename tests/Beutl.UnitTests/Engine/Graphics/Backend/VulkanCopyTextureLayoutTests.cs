using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="VulkanContext.CopyTexture"/> transitions the destination inside its own command buffer
/// (Undefined → TransferDst → ColorAttachmentOptimal) without going through <c>TransitionTo</c>, so the
/// destination's tracked layout went stale; the next <c>TransitionTo</c> then issued its barrier from the wrong
/// <c>oldLayout</c> — undefined behavior on strict drivers (the compute shader-init fallback
/// <c>CopySourceToDestination</c> reaches this path in-tree).
/// </summary>
[NonParallelizable]
[TestFixture]
public class VulkanCopyTextureLayoutTests
{
    [Test]
    public void CopyTexture_SyncsTheDestinationsTrackedLayout()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            IGraphicsContext gfx = GraphicsContextFactory.SharedContext
                ?? throw new InvalidOperationException("no shared graphics context");

            using ITexture2D source = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);
            using ITexture2D destination = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);

            // Drive the destination's tracked layout away from ColorAttachmentOptimal so a stale tracker is visible.
            destination.PrepareForSampling();
            Assert.That(((VulkanTexture2D)destination).CurrentLayoutForTest,
                Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal), "sanity: sampling left the tracker at ShaderReadOnly");

            gfx.CopyTexture(source, destination);

            Assert.That(((VulkanTexture2D)destination).CurrentLayoutForTest,
                Is.EqualTo(ImageLayout.ColorAttachmentOptimal),
                "the tracker must follow the copy's in-command transition, or the next TransitionTo issues its "
                + "barrier from a stale oldLayout");
        });
    }
}
