namespace Beutl;

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class NumberStepAttribute(double largeChange = 10, double smallChange = 1) : Attribute
{
    public double LargeChange { get; } = largeChange;

    public double SmallChange { get; } = smallChange;
}
