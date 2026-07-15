using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// <see cref="VulkanContext.CopyTexture"/> must transition a reused destination from its tracked layout before the
/// blit, then keep that tracker synchronized with its final in-command transition. The compute shader-init fallback
/// <c>CopySourceToDestination</c> reaches this path in-tree.
/// </summary>
[NonParallelizable]
[TestFixture]
public class VulkanCopyTextureLayoutTests
{
    [Test]
    public void CopyTexture_TransitionsReusedDestinationAndKeepsItsTrackerSynchronized()
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
                "the copy must transition from the tracked sampling layout and leave the tracker at its final "
                + "attachment layout");
        });
    }
}
