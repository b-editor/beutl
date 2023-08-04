namespace Beutl.Animation;

public interface IKeyFrameAnimation : IAnimation
{
    KeyFrames KeyFrames { get; }
}
