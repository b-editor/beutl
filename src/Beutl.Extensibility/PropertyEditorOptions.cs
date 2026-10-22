using Avalonia.Controls;

namespace Beutl.Extensibility;

// Narrows a registration to some properties and gives it controls for lists, nodes and settings pages.
public sealed class PropertyEditorOptions
{
    // Takes only the properties this accepts; without it, every property whose value type is exactly the registered one.
    public Func<IPropertyAdapter, bool>? Match { get; init; }

    // The control for a list item; a Register editor shows in lists only with it, a RegisterValue one uses the panel's.
    public Func<IPropertyEditorContext, Control>? CreateListItemControl { get; init; }

    // The control in a node; a Register editor shows in nodes only with it, a RegisterValue one uses the panel's.
    public Func<IPropertyEditorContext, Control>? CreateNodeControl { get; init; }

    // The control on a settings page; a Register editor shows there only with it, a RegisterValue one uses the panel's.
    public Func<IPropertyEditorContext, Control>? CreateSettingsControl { get; init; }
}
