namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares one typed resource address a render description binds and its callbacks read.
/// </summary>
/// <typeparam name="T">The raw resource type leased to the execution callback.</typeparam>
public sealed class RenderResourceSlot<T> : RenderResourceSlot
    where T : class
{
    /// <summary>Initializes a resource slot.</summary>
    public RenderResourceSlot()
    {
    }

    /// <summary>Binds this declared slot to a resource token from the active render context.</summary>
    /// <param name="resource">The request-scoped resource token to bind.</param>
    /// <returns>A binding suitable for a description that declares this slot.</returns>
    public RenderResourceBinding Bind(RenderResource<T> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(this, resource);
    }

    internal override Type ValueType => typeof(T);

    internal override bool Accepts(RenderResource resource)
        => resource is RenderResource<T>;
}
