using Beutl.Engine.Expressions;
using Beutl.Validation;

namespace Beutl.Animation;

// A KeyFrameAnimation<T> seen without its value type.
public interface IKeyFrameAnimation : IExpression, ICoreObject, INotifyEdited, IHierarchical
{
    KeyFrames KeyFrames { get; }

    bool UseGlobalClock { get; set; }

    Type ValueType { get; }

    MediaTime Duration { get; }

    IValidator? Validator { get; set; }

    (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe);

    (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(MediaTime timeSpan);
}
