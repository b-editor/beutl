namespace Beutl.Graphics3DTests;

/// <summary>Category names shared across the suite.</summary>
internal static class TestCategories
{
    /// <inheritdoc cref="Beutl.UnitTests.TestCategories.KnownVulkanSkiaLayoutInterop"/>
    /// <remarks>
    /// The string has to match the one in <c>Beutl.UnitTests</c>, because the validation job filters both
    /// assemblies on the same category name.
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
