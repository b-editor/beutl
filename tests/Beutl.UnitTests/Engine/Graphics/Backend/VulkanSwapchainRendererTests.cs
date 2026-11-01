using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public class VulkanSwapchainRendererTests
{
    private static readonly Extent2D s_view = new(1760, 990);
    private static readonly Extent2D s_old = new(1, 1);

    // The native view took its new size after the last frame was presented: the swapchain is rebuilt at the view's
    // size and the frame drawn again.
    [Test]
    public void APausedPreviewRebuildsAndRedrawsWhenTheViewGrewAfterTheLastFrame()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_old), Is.True);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_old), Is.True);
    }

    // A suboptimal present rebuilt the swapchain at the view's size but showed a frame drawn at the old one: the frame
    // is drawn again without another rebuild.
    [Test]
    public void APausedPreviewRedrawsAFrameShownBeforeTheSwapchainWasRebuilt()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_view), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_old), Is.True);
    }

    [Test]
    public void APausedPreviewKeepsAFrameDrawnAtTheSurfacesSize()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(s_view, swapchain: s_view), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, s_view, presented: s_view), Is.False);
    }

    // A surface without a size to follow (Wayland, a minimized window) changes nothing, while a changed display still
    // redraws the frame.
    [Test]
    public void OnlyAChangedDisplayRedrawsWhenTheSurfaceReportsNoSize()
    {
        Assert.That(VulkanSwapchainRenderer.ShouldRebuild(null, swapchain: s_old), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(false, null, presented: s_old), Is.False);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(true, null, presented: s_old), Is.True);
        Assert.That(VulkanSwapchainRenderer.ShouldRedrawPaused(true, s_view, presented: s_view), Is.True);
    }
}
