using Avalonia.Media;
using Avalonia.Styling;

namespace Beutl.Extensibility;

// A custom theme layers a brush-override ResourceDictionary (ResourceUri) on a base ThemeVariant;
// built-ins are registered by the host and extensions register their own via ThemeExtension.
// AccentColor seeds FluentAvalonia's SystemAccentColor shades unless the user configured an accent.
// Null preserves the OS accent.
public sealed record ThemeDescriptor(
    string Id,
    string DisplayName,
    ThemeVariant BaseVariant,
    Uri? ResourceUri = null,
    bool IsSystemFollowing = false,
    Color? AccentColor = null);
