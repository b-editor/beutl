using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;

namespace Beutl.Operation;

public sealed class AnimatablePropertyAdapter<T>(AnimatableProperty<T> property, EngineObject obj)
    : EnginePropertyAdapter<T>(property, obj), IAnimatablePropertyAdapter<T>
{
    public IAnimation<T>? Animation
    {
        get => Property.Animation;
        set => Property.Animation = value;
    }

    [field: AllowNull]
    public IObservable<IAnimation<T>?> ObserveAnimation => field ??= Observable.FromEvent<IAnimation<T>?>(
            handler => ((AnimatableProperty<T>)Property).AnimationChanged += handler,
            handler => ((AnimatableProperty<T>)Property).AnimationChanged -= handler)
        .Publish(Animation)
        .RefCount();

    public IExpression<T>? Expression
    {
        get => ((AnimatableProperty<T>)Property).Expression;
        set => ((AnimatableProperty<T>)Property).Expression = value;
    }

    public bool HasExpression => ((AnimatableProperty<T>)Property).HasExpression;

    [field: AllowNull]
    public IObservable<IExpression<T>?> ObserveExpression => field ??= Observable.FromEvent<IExpression<T>?>(
            handler => ((AnimatableProperty<T>)Property).ExpressionChanged += handler,
            handler => ((AnimatableProperty<T>)Property).ExpressionChanged -= handler)
        .Publish(Expression)
        .RefCount();
}
