namespace Beutl.Validation;

public readonly struct ValidationContext
{
    public ValidationContext(object? target, CoreProperty? property)
    {
        Target = target;
        Property = property;
    }

    public object? Target { get; }

    public CoreProperty? Property { get; }
}
