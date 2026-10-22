using Avalonia.Controls;

namespace Beutl.Extensibility;

// One editor an extension registered; the host chooses among them by Tier, then by value type.
internal sealed class PropertyEditorRegistration
{
    // The value type the editor takes, or null for a group.
    public Type? ValueType { get; init; }

    public PropertyEditorTier Tier { get; init; } = PropertyEditorTier.Type;

    // Picks the properties a match-tier registration takes from those left.
    public Func<IReadOnlyList<IPropertyAdapter>, IReadOnlyList<IPropertyAdapter>>? Match { get; init; }

    // Null when the editor turns the properties down, as a file source editor does a type it cannot create.
    public required Func<IReadOnlyList<IPropertyAdapter>, IPropertyEditorContext?> CreateContext { get; init; }

    public required Func<IPropertyEditorContext, PropertyEditorSurface, Control?> CreateControl { get; init; }

    // A value editor with the standard look: the host adds the menu and binds the header.
    public bool IsValueEditor { get; init; }

    // Set for a built-in editor used only in lists, such as a path segment's.
    public bool IsListItemOnly { get; init; }

    public required PropertyEditorExtension Owner { get; init; }
}

// How specific a registration is; the host asks the most specific first.
internal enum PropertyEditorTier
{
    Match,
    Choices,
    Type,
}
