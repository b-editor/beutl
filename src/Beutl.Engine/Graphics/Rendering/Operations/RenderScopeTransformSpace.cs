namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares the space a guarded target scope's replay transform is defined in.
/// </summary>
/// <remarks>
/// A scope's declared <see cref="RenderScaleContract"/> can carry an output demand back to its input only when
/// the transform between them is expressed in the input's own coordinates. A scope defined against the ambient
/// target transform - what <c>TransformOperator.Append</c> and <c>TransformOperator.Set</c> do - has that scale
/// carried by the destination matrix instead, which the value graph has no representation of, so raising the
/// input's demand there would rasterize it enlarged and then draw it enlarged again.
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
