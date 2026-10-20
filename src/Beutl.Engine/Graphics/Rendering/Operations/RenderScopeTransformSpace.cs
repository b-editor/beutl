namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares the space a guarded target scope's replay transform is defined in.
/// </summary>
/// <remarks>
/// Only a transform defined in the input's logical space lets the backward map of the scope's
/// <see cref="RenderScaleContract"/> carry an output demand to the input.
/// </remarks>
public enum RenderScopeTransformSpace : byte
{
    /// <summary>
    /// The replay transform is defined against the ambient target transform. The scale contract's backward
    /// map is not applied, because the destination already carries whatever the scope contributes.
    /// </summary>
    AmbientTarget,

    /// <summary>
    /// The replay transform is defined in the input's own logical space, so the scale contract describes the
    /// step between them completely and its backward map reaches the input.
    /// </summary>
    InputLogical,
}
