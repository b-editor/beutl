namespace Beutl.UnitTests;

/// <summary>Category names shared across the suite.</summary>
internal static class TestCategories
{
    /// <summary>Exercises Vulkan images whose layout is tracked independently by Skia and the backend.</summary>
    /// <remarks>
    /// Validation excludes this category until mutable Vulkan layout state is exposed through SkiaSharp.
    /// The ordinary suite still runs it. Tracked by b-editor/beutl#2263.
    /// </remarks>
    public const string KnownVulkanSkiaLayoutInterop = "KnownVulkanSkiaLayoutInterop";
}
