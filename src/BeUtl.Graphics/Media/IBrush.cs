using System.Text.Json.Serialization;

using BeUtl.Converters;
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
}
