namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderTargetPoolOptions
{
    public const long DefaultMaximumRetainedBytes = 256L * 1024 * 1024;

    public long MaximumRetainedBytes { get; init; } = DefaultMaximumRetainedBytes;

    public int MaximumIdleRequests { get; init; } = 120;

    /// <summary>
    /// The largest extent this pool will allocate, or <see langword="null"/> to bound each allocation by
    /// whatever its own allocator answers to.
    /// </summary>
    /// <remarks>
    /// Naming one lets a test pin a limit below every device it runs on, so the refusal is observable without
    /// depending on the machine's GPU, and it binds a caller-supplied allocator too.
    /// </remarks>
    public int? MaxBufferDimension { get; init; }

    internal Action<RenderTargetPoolRegistrationStage>? AfterTargetRegistrationStep { get; init; }

    internal Action? BeforeLeaseRegistration { get; init; }
}
