using Beutl.Engine.Expressions;
using Beutl.Validation;

namespace Beutl.Animation;

// A KeyFrameAnimation<T> seen without its value type.
public interface IKeyFrameAnimation : IExpression, ICoreObject, INotifyEdited, IHierarchical
{
    IListProperty<IKeyFrame> KeyFrames { get; }

    IProperty<bool> UseGlobalClock { get; }

    Type ValueType { get; }

    MediaTime Duration { get; }

    MediaTime ToLocal(MediaTime sceneTime);

    MediaTime ToScene(MediaTime localTime);

    IValidator? Validator { get; set; }

    (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe);

    (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(MediaTime timeSpan);
}
