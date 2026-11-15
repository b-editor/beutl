namespace Beutl.Graphics.Effects;

/// <summary>Execution requirements of an opaque custom effect.</summary>
[Flags]
internal enum CustomFilterEffectOptions
{
    None = 0,
    /// <summary>The callback owns the recorded suffix and explicitly chooses whether and how often to execute it.</summary>
    ControlsContinuation = 1,
    /// <summary>The callback reads or replaces the enclosing composition target. Recorded as ordered target work.</summary>
    AccessesTarget = 2,
    /// <summary>
    /// The callback leaves the content where it is: it copies, flattens, crops or grows its targets in whole device
    /// pixels, draws over or around the content, and moves nothing. Its content then keeps the sub-pixel position the
    /// composition gives it, and reaches the callback on buffers laid on the device grid, whose bounds are the whole
    /// pixels the content touches. What the callback maps onto the content, such as a brush, goes on
    /// <see cref="EffectTarget.ContentBounds"/>, which it sets on the targets it makes when it knows where their
    /// content lies.
    /// </summary>
    KeepsPlacement = 4,
}
