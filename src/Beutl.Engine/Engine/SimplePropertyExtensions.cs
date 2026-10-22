namespace Beutl.Engine;

public static class SimplePropertyExtensions
{
    public static IProperty<T> ToAnimatable<T>(this SimpleProperty<T> simpleProperty)
    {
        var animatableProperty = Property.CreateAnimatable(
            simpleProperty.DefaultValue);

        if (simpleProperty.HasLocalValue)
        {
            animatableProperty.CurrentValue = simpleProperty.CurrentValue;
        }

        return animatableProperty;
    }
}
