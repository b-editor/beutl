namespace Beutl;

public enum PropertyUnit
{
    Percent,
    Degrees,
    Ratio
}

[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class UnitAttribute(PropertyUnit unit) : Attribute
{
    public PropertyUnit Unit { get; } = unit;
}
