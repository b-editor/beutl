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
    /// A test that deliberately asks for a buffer at the engine's own ceiling, which a device whose
    /// framebuffer limit is smaller cannot attach.
    /// </summary>
    /// <remarks>
    /// Planning clamps to <c>RenderScaleUtilities.MaxBufferDimension</c> so a plan means the same thing on
    /// every device, and nothing yet reduces that to what the device reports through
    /// <c>IGraphicsContext.MaxAttachmentDimension</c> at the point a real target is made. On a device that
    /// reports less - CI's reports 8192 - these ask for an attachment it cannot create, which Vulkan calls
    /// undefined rather than a failed allocation. They run normally in the ordinary suite; only the
    /// validation job skips them.
    /// </remarks>
    public const string KnownDeviceBufferLimit = "KnownDeviceBufferLimit";
}
