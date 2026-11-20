namespace Beutl.Engine.Expressions;

public class ExpressionGlobals<T>(ExpressionContext context, IExpression<T>? below) : ExpressionGlobals(context)
{
    public T Value => ValueAt(Context.Time);

    public T ValueAtTime(double time) => ValueAt(MediaTime.FromSeconds(Start + time));

    private T ValueAt(MediaTime sceneTime)
    {
        // Keyframes without a key give the animator's default, not a value; the stored value stands then.
        if (below is not null and not Beutl.Animation.IKeyFrameAnimation { KeyFrames.Count: 0 })
        {
            T value = below.Evaluate(Context.WithTime(sceneTime));
            if (value is not null)
                return value;
        }

        if (Context.CurrentProperty is IProperty<T> property)
            return property.Value;

        throw new ExpressionException("Value is not available in this expression.");
    }
}
