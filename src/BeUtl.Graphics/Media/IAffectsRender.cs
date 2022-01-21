namespace BeUtl.Media;

public interface IAffectsRender
{
    /// <summary>
    /// Raised when the resource changes visually.
    /// </summary>
    event EventHandler? Invalidated;
}
