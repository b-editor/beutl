namespace Beutl.Extensibility;

// Passed to ThemeExtension.OnApplied. Currently exposes only the applied descriptor; the class
// shape (init-only required property) lets future apply-time data be added without breaking
// existing overrides.
public sealed class ThemeApplyContext
{
    public required ThemeDescriptor Descriptor { get; init; }
}
