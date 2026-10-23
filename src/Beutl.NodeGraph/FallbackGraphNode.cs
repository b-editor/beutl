using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public sealed partial class FallbackGraphNode : GraphNode, IFallback
{
    private bool _metadataRestored;

    // A node read from JSON has its metadata already.
    internal void MarkMetadataRead() => _metadataRestored = true;

    // The Ids of the ports in the JSON this node stands in for, which its connections name.
    internal IEnumerable<Guid> SavedPortIds()
    {
        foreach (string member in (string[])[nameof(Items), nameof(NestedInputPorts)])
        {
            if (Json?[member] is not JsonArray ports) continue;
            foreach (JsonNode? port in ports)
            {
                if (port?[nameof(Id)] is JsonValue id && Guid.TryParse(id.ToString(), out Guid value)) yield return value;
            }
        }
    }

    internal void RestoreMetadata(JsonObject? json)
    {
        if (_metadataRestored || json == null) return;
        _metadataRestored = true;
        if (json[nameof(Id)] is JsonValue id && Guid.TryParse(id.ToString(), out Guid value)) Id.Value = value;
        if (json[nameof(Name)] is JsonValue name && name.TryGetValue<string>(out var text)) Name.Value = text;
        if (json[nameof(IsExpanded)] is JsonValue expanded && expanded.TryGetValue<bool>(out var state)) IsExpanded.Value = state;
        if (json[nameof(Position)] is JsonValue position && position.TryGetValue<string>(out var coordinates))
        {
            if (TryParsePosition(coordinates, out var parsed)) Position.Value = parsed;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        // The failure path only assigns Json. Restore display metadata when the graph owns it,
        // once, so later hierarchy attachments do not undo scene recovery's ID reconciliation.
        base.OnPropertyChanged(args);
        if (args.PropertyName == nameof(HierarchicalParent) && HierarchicalParent != null) RestoreMetadata(Json);
    }
}
