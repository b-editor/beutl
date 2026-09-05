namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares whether a guarded target scope replays its input onto the device pixel grid the input would have
/// been rasterized against without the scope.
/// </summary>
/// <remarks>
/// A scope callback's whole permitted vocabulary is save/restore, transform, and clip, so moving the replayed
/// content onto a different grid is an ordinary thing for a scope to do rather than an exception. The planner
/// therefore assumes <see cref="Remapped"/> unless the scope states otherwise: upstream content that declares
/// <see cref="RenderDeviceGridSensitivity.PhaseDependent"/> is re-rasterized under a remapping scope instead
/// of being resampled out of an output cache.
/// </remarks>
public enum RenderDeviceGridMapping : byte
{
    /// <summary>
    /// The scope may replay its input onto a different device pixel grid. Declaring this for a scope that in
    /// fact preserves the grid only costs upstream cache reuse; it never produces wrong pixels.
    /// </summary>
    Remapped,

    /// <summary>
    /// The scope replays its input onto the same device pixel grid, so device-grid phase dependent content
    /// upstream keeps the phase its cached output was captured at.
    /// </summary>
    Preserved,
}
