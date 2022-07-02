using System.Text.Json.Serialization;

using BeUtl.Converters;
using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
[JsonConverter(typeof(BrushJsonConverter))]
public interface IBrush
{
    /// <summary>
    /// Gets the opacity of the brush.
    /// </summary>
    float Opacity { get; }

    /// <summary>
    /// Gets the transform of the brush.
    /// </summary>
    ITransform? Transform { get; }

    /// <summary>
    /// Gets the origin of the brushes <see cref="Transform"/>
    /// </summary>
    RelativePoint TransformOrigin { get; }
}
