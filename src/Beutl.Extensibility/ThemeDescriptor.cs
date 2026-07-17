using Avalonia.Styling;

namespace Beutl.Extensibility;

// A custom theme layers a brush-override ResourceDictionary (ResourceUri) on a base ThemeVariant;
// built-ins are registered by the host and extensions register their own via ThemeExtension.
public sealed record ThemeDescriptor(
    string Id,
    string DisplayName,
    ThemeVariant BaseVariant,
    Uri? ResourceUri = null,
    bool IsSystemFollowing = false);
