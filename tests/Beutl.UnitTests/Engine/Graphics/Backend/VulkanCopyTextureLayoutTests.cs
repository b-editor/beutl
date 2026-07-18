using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
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
    public void CopyTexture_RecordsTransitionsAndBlitInOneSubmissionAndKeepsTrackersSynchronized()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gfx = GetVulkanContext();
            int copySubmissions = 0;

            using ITexture2D source = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);
            using ITexture2D destination = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);

            // Drive both trackers away from their transfer layouts so the copy must record both pre-copy barriers.
            source.PrepareForSampling();
            destination.PrepareForSampling();
            VulkanContext.SetBeforeImmediateSubmitForTest(() => copySubmissions++);

            try
            {
                gfx.CopyTexture(source, destination);
            }
            finally
            {
                VulkanContext.SetBeforeImmediateSubmitForTest(null);
            }

            Assert.Multiple(() =>
            {
                Assert.That(copySubmissions, Is.EqualTo(1),
                    "all transitions and the blit must share one immediate command buffer submission");
                Assert.That(((VulkanTexture2D)source).CurrentLayoutForTest,
                    Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal));
                Assert.That(((VulkanTexture2D)destination).CurrentLayoutForTest,
                    Is.EqualTo(ImageLayout.ColorAttachmentOptimal));
            });
        });
    }

    [Test]
    public void CopyTexture_SubmissionFailureLeavesLayoutTrackersUnchanged()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gfx = GetVulkanContext();
            using ITexture2D source = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);
            using ITexture2D destination = gfx.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);
            source.PrepareForSampling();
            destination.PrepareForSampling();
            var injected = new InvalidOperationException("copy submission failed");
            VulkanContext.SetBeforeImmediateSubmitForTest(() => throw injected);

            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => gfx.CopyTexture(source, destination));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(((VulkanTexture2D)source).CurrentLayoutForTest,
                        Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal));
                    Assert.That(((VulkanTexture2D)destination).CurrentLayoutForTest,
                        Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal));
                });
            }
            finally
            {
                VulkanContext.SetBeforeImmediateSubmitForTest(null);
            }
        });
    }

    private static VulkanContext GetVulkanContext()
    {
        return VulkanTestEnvironment.SharedContext switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            var context => throw new InvalidOperationException(
                $"Expected a Vulkan-capable test context, but got {context.GetType().Name}.")
        };
    }
}
