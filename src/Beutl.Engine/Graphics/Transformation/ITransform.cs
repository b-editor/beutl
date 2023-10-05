using System.Text.Json.Serialization;

using Beutl.Converters;

namespace Beutl.Graphics.Transformation;

[JsonConverter(typeof(TransformJsonConverter))]
public interface ITransform
{
    bool IsEnabled { get; }

    Matrix Value { get; }
}
