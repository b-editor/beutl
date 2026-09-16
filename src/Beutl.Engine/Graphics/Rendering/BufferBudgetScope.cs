namespace Beutl.Graphics.Rendering;

/// <summary>
/// Names which allocation a device buffer budget is being resolved for.
/// </summary>
/// <remarks>
/// A device answers the same limit either way; what differs is whose allocation the answer describes, and
/// the two are not interchangeable. A caller that resolves the wrong one gets the other one's answer
/// exactly when it matters - before the first frame, or from off the render thread - so the axis is named
/// here rather than left to be inferred from a method name.
/// </remarks>
public enum BufferBudgetScope
{
    /// <summary>
    /// The allocation the caller is about to make, on the thread it is calling from.
    /// </summary>
    /// <remarks>
    /// An intermediate is drawn into and then sampled, so it has to satisfy the device's framebuffer limit
    /// as well as its image limit, and a device may report either below
    /// <see cref="BufferDimensionBudget.EngineCeiling"/>. Budgeting against a fixed number instead asks such
    /// a device for an attachment it cannot make, which it reports as undefined behaviour rather than as a
    /// failed allocation.
    /// <para>
    /// A buffer allocated off the render thread never reaches that device at all -
    /// <see cref="RenderTarget.Create"/> rasters it on the CPU - so the shared context applies only where
    /// that allocation would attach to it. Where it does, the context is the one that allocation would
    /// build rather than whichever is installed now: before any GPU work there is none installed, and
    /// answering the engine ceiling there admits a buffer the device built moments later cannot attach.
    /// </para>
    /// </remarks>
    Allocation,

    /// <summary>
    /// What <see cref="Allocation"/> is expected to answer on the render thread, asked from a thread that
    /// is not it.
    /// </summary>
    /// <remarks>
    /// Pre-validation is not allocation. A dialog that asks whether an export will fit is predicting the
    /// limit the render thread will resolve later, and <see cref="Allocation"/> cannot be that prediction:
    /// it answers for the caller's own allocation, which off the render thread
    /// <see cref="RenderTarget.Create"/> rasters on the CPU, so it correctly reports the engine ceiling
    /// there. Pre-validating against that clears a size the render then refuses once the user has already
    /// chosen a file.
    /// <para>
    /// A context reports a limit fixed for its lifetime, so reading an installed one from another thread is
    /// sound. What that thread cannot do is build one -
    /// <see cref="Backend.GraphicsContextFactory.GetOrCreateShared"/> is render-thread-only - so before any
    /// GPU work the answer is <see cref="BufferDimensionBudget.EngineCeiling"/>. That is not a measurement
    /// standing in for one: a resolved limit is the smaller of the ceiling and the device, so the ceiling is
    /// the bound every answer satisfies, and it is only ever loose until the first frame is drawn. Erring
    /// open is also the right direction for a courtesy check - the allocation itself refuses
    /// authoritatively, so a warning not given costs a late failure, while one invented for a device that
    /// turns out to be larger would refuse an export that would have worked.
    /// </para>
    /// </remarks>
    Prediction,
}
