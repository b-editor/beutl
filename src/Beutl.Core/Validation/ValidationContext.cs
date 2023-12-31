namespace Beutl.Validation;

public readonly struct ValidationContext(object? target, CoreProperty? property)
{
    public object? Target { get; } = target;

    public CoreProperty? Property { get; } = property;
}
