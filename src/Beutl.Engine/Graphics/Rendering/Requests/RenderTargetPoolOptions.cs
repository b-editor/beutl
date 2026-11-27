namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderTargetPoolOptions
{
    public const long DefaultMaximumRetainedBytes = 256L * 1024 * 1024;

    // Five full-size 8K UHD RGBA16F targets use about 1.24 GiB. Leave room for effect padding.
    public const long DefaultMaximumLiveBytes = 1536L * 1024 * 1024;

    public const int DefaultMaximumIdleRequests = 120;

    public long MaximumRetainedBytes { get; init; } = DefaultMaximumRetainedBytes;

    /// <summary>Maximum RGBA16F bytes one request can hold in its own render targets concurrently.</summary>
    public long MaximumLiveBytes { get; init; } = DefaultMaximumLiveBytes;

    public int MaximumIdleRequests { get; init; } = DefaultMaximumIdleRequests;

    /// <summary>
    /// The per-axis extent limit, or <see langword="null"/> to bound each allocation by
    /// whatever its own allocator answers to.
    /// </summary>
    /// <remarks>
    /// Naming one lets a test pin a limit below every device it runs on, so the refusal is observable without
    /// depending on the machine's GPU, and it binds a caller-supplied allocator too.
    /// </remarks>
    public BufferDimensionBudget? Budget { get; init; }
}
