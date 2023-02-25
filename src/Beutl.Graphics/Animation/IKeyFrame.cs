using Beutl.Animation.Easings;
using Beutl.Media;

namespace Beutl.Animation;

public interface IKeyFrame : IJsonSerializable, IAffectsRender
{
    event EventHandler? KeyTimeChanged;

    TimeSpan KeyTime { get; set; }

    object? Value { get; set; }

    Easing Easing { get; set; }
}
