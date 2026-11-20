namespace Beutl.Engine.Expressions;

public class ExpressionGlobals<T>(ExpressionContext context, IExpression<T>? below) : ExpressionGlobals(context)
{
    public T Value => ValueAt(Context.Time);

    public T ValueAtTime(double time) => ValueAt(MediaTime.FromSeconds(Start + time));

    private T ValueAt(MediaTime sceneTime)
    {
        if (below != null)
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
