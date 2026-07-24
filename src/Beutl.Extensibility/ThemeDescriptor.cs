using Avalonia.Media;
using Avalonia.Styling;

namespace Beutl.Extensibility;

// A custom theme layers a brush-override ResourceDictionary (ResourceUri) on a base ThemeVariant;
// built-ins are registered by the host and extensions register their own via ThemeExtension.
// AccentColor is the theme's design accent: while the theme is active and the user has not
// configured a custom accent, the host seeds FluentAvalonia's SystemAccentColor shades from it,
// so accent-derived brushes in ResourceUri can reference SystemAccentColor* and still land on the
// theme's designed values. Null keeps the OS accent.
public sealed record ThemeDescriptor(
    string Id,
    string DisplayName,
    ThemeVariant BaseVariant,
    Uri? ResourceUri = null,
    bool IsSystemFollowing = false,
    Color? AccentColor = null);
