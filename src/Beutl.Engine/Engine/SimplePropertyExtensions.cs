namespace Beutl.Engine;

public static class SimplePropertyExtensions
{
    public static IExpressionProperty<T> ToAnimatable<T>(this SimpleProperty<T> simpleProperty)
    {
        var animatableProperty = Property.CreateAnimatable(
            simpleProperty.DefaultValue);

        if (simpleProperty.IsValueSet)
        {
            animatableProperty.Value = simpleProperty.Value;
        }

        return animatableProperty;
    }
}
