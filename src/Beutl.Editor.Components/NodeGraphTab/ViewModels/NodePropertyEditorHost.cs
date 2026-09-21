using System.Collections;
using Avalonia.Controls;
using Beutl.Engine;
using Beutl.NodeGraph;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

// A separate host follows each editor context, even when several contexts edit the same object.
internal sealed class NodePropertyEditorHost(
    GraphNodeViewModel node,
    INodeMember? root,
    IPropertyAdapter property,
    NodePropertyEditorHost? parent = null) : IPropertyEditorControlHost
{
    public IPropertyEditorControlHost CreateChildHost(IPropertyAdapter childProperty)
        // Composite editors can forward the same adapter to a helper (e.g. a gradient-stop list).
        => ReferenceEquals(property, childProperty) ? this : new NodePropertyEditorHost(node, root, childProperty, this);

    public Control WrapEditor(IPropertyEditorContext context, Control editor)
        => node.WrapEditor(context, editor, root, GetPath());

    private IReadOnlyList<string>? GetPath()
    {
        if (root == null) return null;
        if (parent == null) return [];
        if (parent.GetPath() is not { } parentPath) return null;

        object? parentValue = parent.GetValue();
        if (property.GetEngineProperty() is { } childProperty
            && parentValue is EngineObject owner && owner.Properties.Contains(childProperty))
            return [.. parentPath, "p:" + childProperty.Name];

        // List item contexts survive moves and replacements. Resolve the current persistent ID
        // when wrapping their children, rather than capturing an index or the previous item's ID.
        if (parentValue is IList && property.GetEngineProperty() == null && property.GetValue() is EngineObject item)
            return [.. parentPath, "i:" + item.Id];

        return null;
    }

    private object? GetValue() => property.GetValue();
}
