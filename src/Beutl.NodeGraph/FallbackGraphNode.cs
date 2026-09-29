using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public sealed partial class FallbackGraphNode : GraphNode, IFallback
{
    private bool _metadataRestored;

    internal void RestoreMetadata(JsonObject? json)
    {
        if (_metadataRestored || json == null) return;
        _metadataRestored = true;
        if (json[nameof(Id)] is JsonValue id && Guid.TryParse(id.ToString(), out Guid value)) Id = value;
        if (json[nameof(Name)] is JsonValue name && name.TryGetValue<string>(out var text)) Name = text;
        if (json[nameof(IsExpanded)] is JsonValue expanded && expanded.TryGetValue<bool>(out var state)) IsExpanded = state;
        if (json[nameof(Position)] is JsonValue position && position.TryGetValue<string>(out var coordinates))
        {
            string[] parts = coordinates.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                && double.IsFinite(x) && double.IsFinite(y)) Position = (x, y);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        // The failure path only assigns Json. Restore display metadata when the graph owns it,
        // once, so later hierarchy attachments do not undo scene recovery's ID reconciliation.
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs change && change.Property == HierarchicalParentProperty
            && HierarchicalParent != null) RestoreMetadata(Json);
    }
}
