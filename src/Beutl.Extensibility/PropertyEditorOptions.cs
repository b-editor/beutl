using Avalonia.Controls;

namespace Beutl.Extensibility;

// Narrows a registration to some properties and gives it other controls in lists, nodes and settings pages.
public sealed class PropertyEditorOptions
{
    // Takes only the properties this accepts; without it, every property of the value type.
    public Func<IPropertyAdapter, bool>? Match { get; init; }

    // The control for a list item, when it differs from the panel's.
    public Func<IPropertyEditorContext, Control>? CreateListItemControl { get; init; }

    // The control in a node, when it differs from the panel's.
    public Func<IPropertyEditorContext, Control>? CreateNodeControl { get; init; }

    // The control on a settings page, when it differs from the panel's.
    public Func<IPropertyEditorContext, Control>? CreateSettingsControl { get; init; }

    // Only in the property panel; in nodes, lists and settings pages the next editor, such as the built-in one, takes it.
    internal bool PanelOnly { get; init; }
}
