namespace Beutl.Validation;

public readonly struct ValidationContext(object? target, IProperty? property)
{
    public object? Target { get; } = target;

    public IProperty? Property { get; } = property;
}
