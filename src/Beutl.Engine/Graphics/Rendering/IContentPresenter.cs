namespace Beutl.Graphics.Rendering;

/// <summary>
/// A compositor-owned drawable, such as a clip transition or a track matte, that draws evaluated document content
/// in place of that content's own drawables. Hit tests and boundaries look through it to the content it draws.
/// </summary>
internal interface IContentPresenter
{
    /// <summary>The content drawn, one list per side in the order a hit test tries them, each bottom first.</summary>
    IReadOnlyList<IReadOnlyList<Drawable.Resource>> PresentedSides { get; }

    /// <summary>
    /// What a hit on the presenter selects where none of its content is under the point, or
    /// <see langword="null"/> to continue to the layers beneath.
    /// </summary>
    Drawable? UnhitTarget { get; }

    /// <summary>The document drawable that stands for the presenter, for instance in another presenter's fallback.</summary>
    Drawable? RepresentativeTarget { get; }

    /// <summary>The document drawable that stands for <paramref name="resource"/>, looking through presenters.</summary>
    static Drawable? RepresentativeOf(Drawable.Resource? resource)
        => resource is IContentPresenter presenter ? presenter.RepresentativeTarget : resource?.GetOriginal();
}
