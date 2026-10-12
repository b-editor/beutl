using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics3D.Materials;

/// <summary>The uniform buffer and descriptor set one material draw reads.</summary>
internal sealed class MaterialDrawBindings(IBuffer buffer, IDescriptorSet descriptors) : IDisposable
{
    public IBuffer Buffer { get; } = buffer;

    public IDescriptorSet Descriptors { get; } = descriptors;

    public void Dispose()
    {
        Descriptors.Dispose();
        Buffer.Dispose();
    }
}

/// <summary>
/// Supplies a material's per-draw bindings, reusing the ones whose recorded draws the device has completed.
/// </summary>
/// <remarks>
/// A draw reads its bindings when the device executes it, not when it is recorded, so replaced bindings are reused only
/// once the submission that recorded them completes. They retire only after their replacement is bound, since a pass
/// split by a flush binds them again on a fresh command buffer. The pool is used from the render thread only.
/// </remarks>
internal sealed class MaterialDrawBindingPool : IDisposable
{
    private readonly Func<MaterialDrawBindings> _create;
    private readonly Action<Action>? _deferUntilRecordedWorkCompletes;
    private readonly Stack<MaterialDrawBindings> _free = new();
    private MaterialDrawBindings? _acquired;
    private MaterialDrawBindings? _bound;
    private bool _disposed;

    /// <param name="create">Creates bindings when none are free.</param>
    /// <param name="deferUntilRecordedWorkCompletes">
    /// Runs an action once the command buffers recorded so far have completed, or <see langword="null"/> when the
    /// backend has no such queue, in which case retired bindings are disposed instead of reused.
    /// </param>
    public MaterialDrawBindingPool(
        Func<MaterialDrawBindings> create,
        Action<Action>? deferUntilRecordedWorkCompletes)
    {
        _create = create;
        _deferUntilRecordedWorkCompletes = deferUntilRecordedWorkCompletes;
    }

    /// <summary>Gets the number of bindings this pool has created. Test seam: only the reuse tests read this.</summary>
    public int CreatedCount { get; private set; }

    public static MaterialDrawBindingPool Create<TUbo>(IGraphicsContext context, IPipeline3D pipeline, uint textureCount)
        where TUbo : struct
    {
        Action<Action>? deferRelease = context switch
        {
            VulkanContext vulkan => vulkan.DeferRelease,
            CompositeContext composite => composite.Vulkan.DeferRelease,
            _ => null,
        };

        return new MaterialDrawBindingPool(
            () => MaterialGpuResources.CreateDrawBindings<TUbo>(context, pipeline, textureCount),
            deferRelease);
    }

    /// <summary>
    /// Returns bindings that no pending command buffer references. Pass them to <see cref="MarkBound"/> once the
    /// render pass has bound them.
    /// </summary>
    public MaterialDrawBindings Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // A draw that threw before MarkBound may still have recorded its bindings, so they wait like any other.
        if (_acquired is { } abandoned)
        {
            _acquired = null;
            Retire(abandoned);
        }

        if (!_free.TryPop(out MaterialDrawBindings? bindings))
        {
            bindings = _create();
            CreatedCount++;
        }

        _acquired = bindings;
        return bindings;
    }

    /// <summary>Records that the render pass now binds <paramref name="bindings"/> and retires the ones it replaced.</summary>
    public void MarkBound(MaterialDrawBindings bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(bindings, _acquired))
            throw new InvalidOperationException("Only the bindings returned by the latest Acquire can be marked bound.");

        _acquired = null;
        MaterialDrawBindings? replaced = _bound;
        _bound = bindings;
        if (replaced is not null)
            Retire(replaced);
    }

    /// <summary>
    /// Uploads <paramref name="ubo"/> into <paramref name="bindings"/>, binds <paramref name="pipeline"/> and the
    /// bindings' descriptor set, then marks the bindings bound - only once bound may they replace the previous ones.
    /// </summary>
    public void UploadAndBind<TUbo>(
        MaterialDrawBindings bindings,
        in TUbo ubo,
        IRenderPass3D renderPass,
        IPipeline3D pipeline)
        where TUbo : unmanaged
    {
        bindings.Buffer.Upload(new ReadOnlySpan<TUbo>(in ubo));
        renderPass.BindPipeline(pipeline);
        renderPass.BindDescriptorSet(pipeline, bindings.Descriptors);
        MarkBound(bindings);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Disposing defers each object's destruction past the command buffers that recorded it.
        _acquired?.Dispose();
        _acquired = null;
        _bound?.Dispose();
        _bound = null;
        while (_free.TryPop(out MaterialDrawBindings? bindings))
            bindings.Dispose();
    }

    private void Retire(MaterialDrawBindings bindings)
    {
        if (_deferUntilRecordedWorkCompletes is null)
        {
            bindings.Dispose();
            return;
        }

        _deferUntilRecordedWorkCompletes(() => Return(bindings));
    }

    private void Return(MaterialDrawBindings bindings)
    {
        if (_disposed)
            bindings.Dispose();
        else
            _free.Push(bindings);
    }
}
