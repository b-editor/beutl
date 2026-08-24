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
    /// needs a way to read back or command the layout Skia holds — which SkiaSharp 3.119 does not expose.
    /// Until then the validation job skips this category so the gate still covers everything else; the tests
    /// themselves run normally in the ordinary suite. Tracked as b-editor/beutl#2263, which is also where
    /// the condition for deleting this category is recorded.
    /// </para>
    /// </remarks>
    public const string KnownVulkanSkiaLayoutInterop = "KnownVulkanSkiaLayoutInterop";

    /// <summary>
    /// A test that asks the backend for an attachment the device cannot make, which Vulkan calls undefined
    /// rather than a failed allocation.
    /// </summary>
    /// <remarks>
    /// Planning clamps to <c>RenderScaleUtilities.MaxBufferDimension</c> so a plan means the same thing on
    /// every device, and the site that turns a density into real pixels re-clamps to
    /// <c>RenderScaleUtilities.ResolveMaxBufferDimension</c> - the smaller of that ceiling and what
    /// <c>IGraphicsContext.MaxAttachmentDimension</c> reports - so an over-ceiling working scale no longer
    /// reaches a device that reports less (CI's reports 8192). What is left here asks for a zero-extent
    /// attachment, which is illegal at any device limit and which no clamp rounds up into a legal one. They
    /// run normally in the ordinary suite; only the validation job skips them.
    /// </remarks>
    public const string KnownDeviceBufferLimit = "KnownDeviceBufferLimit";
}
