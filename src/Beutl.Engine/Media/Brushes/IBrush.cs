using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Serialization;
using Beutl.Styling;

namespace Beutl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
[JsonConverter(typeof(BrushJsonConverter))]
[DummyType(typeof(DummyBrush))]
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
