using System.Text.Json.Serialization;

using Beutl.Animation.Easings;
using Beutl.Converters;
using Beutl.Media;

namespace Beutl.Animation;

[JsonConverter(typeof(KeyFrameJsonConverter))]
public interface IKeyFrame : ICoreObject, IAffectsRender
{
    event EventHandler? KeyTimeChanged;

    TimeSpan KeyTime { get; set; }

    //TimeSpan Duration { get; }

    object? Value { get; set; }

    Easing Easing { get; set; }

    void SetParent(IKeyFrameAnimation? parent);

    IKeyFrameAnimation? GetParent();

    //void SetDuration(TimeSpan timeSpan);
}
