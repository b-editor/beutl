using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Reads or replaces the composition target beneath a target effect, during its callback only.</summary>
public sealed class CompositionTargetAccess
{
    private readonly RenderExecutionContext _execution;

    internal CompositionTargetAccess(RenderExecutionContext execution) => _execution = execution;

    /// <summary>Samples the preceding composition target without a CPU pixel readback.</summary>
    public RenderTargetImageLease Capture() => _execution.CaptureTarget();

    /// <summary>Replaces the composition target, including transparent pixels.</summary>
    public void Replace(SKImage image) => _execution.ReplaceTarget(image);
}
