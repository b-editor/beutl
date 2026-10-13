namespace Beutl.Graphics.Rendering;

/// <summary>
/// Names which allocation a device buffer budget is being resolved for.
/// </summary>
/// <remarks>
/// A device answers the same limit either way, but the two answers describe different allocations, and the wrong
/// one differs exactly when it matters: before the first frame, or off the render thread.
/// </remarks>
public enum BufferBudgetScope
{
    /// <summary>
    /// The allocation the caller is about to make, on the thread it is calling from.
    /// </summary>
    /// <remarks>
    /// An intermediate must fit the device's framebuffer and image limits, either of which may be below
    /// <see cref="BufferDimensionBudget.EngineCeiling"/>. Off the render thread <see cref="RenderTarget.Create"/>
    /// rasters on the CPU; on it, the limit is that of the context the allocation would build, even before one exists.
    /// </remarks>
    Allocation,

    /// <summary>
    /// What <see cref="Allocation"/> is expected to answer on the render thread, asked from a thread that
    /// is not it.
    /// </summary>
    /// <remarks>
    /// For pre-validation, such as an export dialog predicting the render thread's limit. Another thread cannot build
    /// a context, so before any GPU work this answers <see cref="BufferDimensionBudget.EngineCeiling"/>, the bound
    /// every resolved limit satisfies; the allocation itself still refuses authoritatively.
    /// </remarks>
    Prediction,
}
