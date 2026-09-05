namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares one typed resource address a render description binds and its callbacks read.
/// </summary>
/// <typeparam name="T">The raw resource type leased to the execution callback.</typeparam>
public sealed class RenderResourceSlot<T> : IRenderResourceSlot
    where T : class
{
    /// <summary>Binds this declared slot to a resource token from the active render context.</summary>
    /// <param name="resource">The request-scoped resource token to bind.</param>
    /// <returns>A binding suitable for a description that declares this slot.</returns>
    public RenderResourceBinding Bind(RenderResource<T> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(this, resource);
    }
}

internal interface IRenderResourceSlot;
