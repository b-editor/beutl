namespace Beutl.UnitTests;

/// <summary>Category names shared across the suite.</summary>
internal static class TestCategories
{
    /// <summary>
    /// Exercises a render target whose Vulkan image is driven by both Skia and the backend, which the two
    /// track independently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>RenderTarget</c> builds its <c>SKSurface</c> once from a <c>GRVkImageInfo</c> and keeps it for the
    /// target's life. Skia tracks that image's layout from there on, while <c>VulkanTexture2D</c> tracks the
    /// same image separately as the backend transitions it for its own passes, sampling and readbacks.
    /// Neither side is told what the other did, so the two records drift apart and a barrier ends up naming
    /// an <c>oldLayout</c> the image is no longer in. Vulkan leaves that undefined; the validation gate
    /// reports it as <c>UNASSIGNED-CoreValidation-DrawState-InvalidImageLayout</c>.
    /// </para>
    /// <para>
    /// This is a pre-existing defect in the Skia interop, not in what these tests assert, and closing it
    /// needs a way to read back or command the layout Skia holds. No SkiaSharp release exposes one:
    /// <c>GRBackendRenderTarget</c> takes a <c>GRVkImageInfo</c> in its constructor and never gives it back,
    /// and the native C ABI has no Vulkan layout or mutable-state entry point at all — it carries a GL
    /// framebuffer-info getter with no Vulkan counterpart. Native Skia does have
    /// <c>GrBackendRenderTargets::GetVkImageInfo</c> and <c>SetVkImageLayout</c>, so closing this means
    /// contributing the C ABI and the binding upstream, not upgrading the package.
    /// Until then the validation job skips this category so the gate still covers everything else; the tests
    /// themselves run normally in the ordinary suite. Tracked as b-editor/beutl#2263, which is also where
    /// the condition for deleting this category is recorded.
    /// </para>
    /// </remarks>
    public const string KnownVulkanSkiaLayoutInterop = "KnownVulkanSkiaLayoutInterop";
}
