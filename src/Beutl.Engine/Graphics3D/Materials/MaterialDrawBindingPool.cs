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
/// <para>
/// A draw reads its uniform buffer and descriptor set when the device executes the command buffer, not when
/// <see cref="Material3D.Resource.Bind"/> records it, so bindings cannot be written for another draw while a command
/// buffer that recorded them is pending. Rather than creating a buffer, a device memory allocation and a descriptor
/// pool for every draw, the pool retires replaced bindings into the backend's deferred-release queue, which runs once
/// the submission holding the current recording completes, and hands them out again from there.
/// </para>
/// <para>
/// Bindings are retired only after their replacement is bound. Until then the render pass still counts them as bound
/// and replays them onto a fresh command buffer when a flush splits the pass, so retiring them at
/// <see cref="Acquire"/> would free them while that later command buffer references them.
/// </para>
/// <para>
/// The pool keeps as many bindings as the most draws that were pending at once, and releases them when the material
/// rebuilds its pipeline or is disposed. It is used from the render thread only.
/// </para>
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

    /// <summary>Gets the number of bindings this pool has created.</summary>
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
