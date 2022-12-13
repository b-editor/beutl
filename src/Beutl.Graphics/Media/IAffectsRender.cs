namespace Beutl.Media;

public interface IAffectsRender
{
    /// <summary>
    /// Raised when the resource changes visually.
    /// </summary>
    event EventHandler<RenderInvalidatedEventArgs>? Invalidated;
}
