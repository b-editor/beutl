using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

public sealed partial class FallbackGraphNode : GraphNode, IFallback
{
    private bool _metadataRestored;

    // A node read from JSON has its metadata already.
    internal void MarkMetadataRead() => _metadataRestored = true;

    // Only direct members with a matching connection reference establish ownership of a saved endpoint.
    internal IEnumerable<SavedPort> FindSavedPorts(Guid portId, Guid connectionId, bool input)
    {
        foreach (string member in (string[])[nameof(Items), nameof(NestedInputPorts)])
        {
            if (!input && member == nameof(NestedInputPorts)) continue;
            if (Json?[member] is not JsonArray ports) continue;
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] is not JsonObject port || !MatchesId(port[nameof(Id)], portId)) continue;
                if (input && MatchesId(port[nameof(IInputPort.Connection)], connectionId))
                {
                    yield return new SavedPort(this, member, i, IsList: false);
                }
                else if (port[nameof(IListPort.Connections)] is JsonArray references
                         && references.Any(reference => MatchesId(reference, connectionId)))
                {
                    yield return new SavedPort(this, member, i, IsList: true);
                }
            }
        }
    }

    private static bool MatchesId(JsonNode? node, Guid id)
        => node is JsonValue value
           && (value.TryGetValue(out Guid savedId)
               || value.TryGetValue<string>(out var text) && Guid.TryParse(text, out savedId))
           && savedId == id;

    internal sealed record SavedPort(FallbackGraphNode Node, string Member, int Index, bool IsList)
    {
        internal void RemoveConnection(JsonObject serializedNode, Guid connectionId)
        {
            if (serializedNode[Member] is not JsonArray ports || Index >= ports.Count
                || ports[Index] is not JsonObject port) return;

            if (IsList && port[nameof(IListPort.Connections)] is JsonArray references)
            {
                for (int i = references.Count - 1; i >= 0; i--)
                {
                    if (MatchesId(references[i], connectionId)) references.RemoveAt(i);
                }
            }
            else if (!IsList && MatchesId(port[nameof(IInputPort.Connection)], connectionId))
            {
                port[nameof(IInputPort.Connection)] = Guid.Empty;
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
        if (json[nameof(EditorSize)] is JsonObject size)
        {
            static double? Dimension(JsonNode? node)
                => node is JsonValue value && value.TryGetValue<double>(out double dimension) ? dimension : null;
            EditorSize.Value = new(Dimension(size[nameof(NodeEditorSize.Width)]),
                Dimension(size[nameof(NodeEditorSize.CodeEditorHeight)]));
        }
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
