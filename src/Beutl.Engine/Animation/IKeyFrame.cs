using System.Text.Json.Serialization;

using Beutl.Animation.Easings;
using Beutl.Converters;
using Beutl.Media;

namespace Beutl.Animation;

[JsonConverter(typeof(KeyFrameJsonConverter))]
public interface IKeyFrame : ICoreObject, INotifyEdited, IHierarchical
{
    event EventHandler? KeyTimeChanged;

    TimeSpan KeyTime { get; set; }

    //TimeSpan Duration { get; }

    object? Value { get; set; }

    /// <summary>
    /// Installs <paramref name="value"/>, replacing distinct reference-type values even when they
    /// compare equal while preserving the normal validation and notification semantics.
    /// </summary>
    void ReplaceValue(object? value);

    Easing Easing { get; set; }

    //void SetDuration(TimeSpan timeSpan);
}
