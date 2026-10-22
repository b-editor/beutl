using Avalonia.Controls;

namespace Beutl.Extensibility;

// One editor an extension registered; the host chooses among them by Tier, then by value type.
internal sealed class PropertyEditorRegistration
{
    // The value type the editor takes, a class or an interface; null for the other tiers.
    public Type? ValueType { get; init; }

    public PropertyEditorTier Tier { get; init; } = PropertyEditorTier.Type;

    // Picks the properties a match-tier registration takes from those left.
    public Func<IReadOnlyList<IPropertyAdapter>, IReadOnlyList<IPropertyAdapter>>? Match { get; init; }

    // Null when the editor turns the properties down, as a file source editor does a type it cannot create.
    public required Func<IReadOnlyList<IPropertyAdapter>, IPropertyEditorContext?> CreateContext { get; init; }

    public required Func<IPropertyEditorContext, PropertyEditorSurface, Control?> CreateControl { get; init; }

    // Connects a value editor's control to its context, once the host has set the control up.
    public Action<IPropertyEditorContext, Control>? Connect { get; init; }

    // Set for RegisterValue and the built-in editors: the host binds the header, adds the menu and fits the surface.
    public bool IsStandard { get; init; }

    // A list item looks at these first: a built-in editor used only in lists, or one with its own list item control.
    public bool HasListItemControl { get; init; }

    // Set for a built-in editor used only in lists, such as a path segment's.
    public bool IsListItemOnly { get; init; }

    // The surfaces the editor shows on, or null for all: Register's where it has a control, RegisterGroup's in the panel.
    public Func<PropertyEditorSurface, bool>? ShowsOn { get; init; }

    public required PropertyEditorExtension Owner { get; init; }
}

// How specific a registration is; the host asks the most specific first.
internal enum PropertyEditorTier
{
    Match,
    Choices,
    Type,
}
