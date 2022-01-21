using BeUtl.Styling;

namespace BeUtl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public interface IBrush : IStyleable, IAffectsRender
{
    /// <summary>
    /// Gets the opacity of the brush.
    /// </summary>
    float Opacity { get; }
}
