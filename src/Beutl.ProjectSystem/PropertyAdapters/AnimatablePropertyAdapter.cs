using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;

namespace Beutl.PropertyAdapters;

public sealed class AnimatablePropertyAdapter<T>(AnimatableProperty<T> property, CoreObject obj)
    : ExpressionPropertyAdapterBase<T>(property, obj), IAnimatablePropertyAdapter<T>
{
    public AnimatablePropertyAdapter(AnimatableProperty<T> property, EngineObject obj) : this(property, (CoreObject)obj) { }

    public KeyFrameAnimation<T>? Animation
    {
        get => Property.GetKeyFrameAnimation();
        set => Property.SetKeyFrameAnimation(value);
    }

    [field: AllowNull]
    public IObservable<KeyFrameAnimation<T>?> ObserveAnimation => field ??= Observable.FromEvent<IExpression<T>?>(
            handler => Property.ExpressionChanged += handler,
            handler => Property.ExpressionChanged -= handler)
        .Select(_ => Animation)
        .DistinctUntilChanged()
        .Publish(Animation)
        .RefCount();
}
