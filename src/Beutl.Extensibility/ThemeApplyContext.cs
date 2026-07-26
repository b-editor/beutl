using Avalonia.Media;

namespace Beutl.Extensibility;

// Passed to ThemeExtension.OnApplied and OnAccentChanged. The class shape (init-only properties)
// lets future data be added without breaking existing overrides.
public sealed class ThemeApplyContext
{
    public required ThemeDescriptor Descriptor { get; init; }

    /// <summary>
    /// The accent the host resolved for this theme — the user's custom accent when one is configured,
    /// otherwise <see cref="ThemeDescriptor.AccentColor"/>. Null means the OS accent, which the host
    /// does not resolve itself: read <c>SystemAccentColor</c> from the app resources for the value
    /// FluentAvalonia settled on.
    /// </summary>
    public Color? Accent { get; init; }
}
