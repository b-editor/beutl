namespace Beutl.Graphics.Rendering;

public sealed class RenderHitTestContext
{
    private readonly IReadOnlyList<RenderResourceBinding> _resources;

    // Nothing here is copied: both lists are read for the duration of one hit test, and the caller that
    // builds them must not hand over anything it lets others mutate while the test runs.
    internal RenderHitTestContext(
        Rect outputBounds,
        IReadOnlyList<RenderHitTestInput> inputs,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        OutputBounds = outputBounds;
        Inputs = inputs;
        _resources = resources;
    }

    public Rect OutputBounds { get; }

    public IReadOnlyList<RenderHitTestInput> Inputs { get; }

    /// <summary>
    /// Reads the resource that the call being hit-tested bound to <paramref name="slot"/>.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <typeparam name="TResult">The value the reader produces.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="use">Reads the bound resource. The raw value must not outlive this call.</param>
    /// <exception cref="KeyNotFoundException">The call bound no resource to that slot.</exception>
    public TResult UseResource<T, TResult>(RenderResourceSlot<T> slot, Func<T, TResult> use)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(use);

        foreach (RenderResourceBinding binding in _resources)
        {
            if (ReferenceEquals(binding.Slot, slot))
            {
                var resource = (RenderResource<T>)binding.Resource;
                return resource.Registry.Use(resource, use);
            }
        }

        throw new KeyNotFoundException(
            "No resource was bound to the requested slot for this hit test.");
    }
}
