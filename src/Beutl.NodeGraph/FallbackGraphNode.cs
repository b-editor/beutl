using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Serialization;

namespace Beutl.NodeGraph;

[SuppressFallbackGeneration]
public sealed partial class FallbackGraphNode : GraphNode, IFallback
{
    private JsonObject? _json;
    private readonly Dictionary<Guid, JsonObject> _savedMembers = new();
    private readonly Dictionary<Guid, NodeMember> _restoredMembers = new();
    private (Guid Id, string Name, (double X, double Y) Position, bool Expanded, bool Enabled) _original;

    public JsonObject? Json
    {
        get => _json;
        set
        {
            _json = value?.DeepClone().AsObject();
            _savedMembers.Clear();
            foreach (JsonObject member in EnumerateSavedMembers(_json))
                if (TryGetId(member[nameof(Id)], out Guid memberId) && memberId != Guid.Empty)
                    _savedMembers.TryAdd(memberId, member);
            // The failure path assigns Json without calling Deserialize. Read only graph metadata;
            // never instantiate the failed plugin or deserialize its arbitrary properties again.
            if (TryGetId(_json?[nameof(Id)], out Guid id)) Id = id;
            if (_json?[nameof(Name)] is JsonValue name && name.TryGetValue<string>(out var text)) Name = text;
            if (_json?[nameof(IsExpanded)] is JsonValue expanded && expanded.TryGetValue<bool>(out var isExpanded))
                IsExpanded = isExpanded;
            if (_json?[nameof(IsEnabled)] is JsonValue enabled && enabled.TryGetValue<bool>(out var isEnabled))
                IsEnabled = isEnabled;
            if (_json?[nameof(Position)] is JsonValue position && position.TryGetValue<string>(out var coordinates))
            {
                string[] parts = coordinates.Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    && double.IsFinite(x) && double.IsFinite(y)) Position = (x, y);
            }
            _original = (Id, Name, Position, IsExpanded, IsEnabled);
        }
    }

    public FallbackReason Reason { get; set; }

    public bool CanSerializeWithoutDataLoss => Json != null;

    public string? ErrorMessage { get; set; }

    public bool TryGetTypeName([NotNullWhen(true)] out string? result)
    {
        result = null;
        return Json?.TryGetDiscriminator(out result) == true;
    }

    public override void Deserialize(ICoreSerializationContext context)
        => Json = (context as IJsonSerializationContext)?.GetJsonObject();

    public override void Serialize(ICoreSerializationContext context)
    {
        if (context is not IJsonSerializationContext jsonContext || Json == null)
        {
            base.Serialize(context);
            return;
        }

        var json = Json.DeepClone().AsObject();
        // Preserve malformed/unknown fields verbatim until the user edits that field.
        if (Id != _original.Id) json[nameof(Id)] = Id;
        if (Name != _original.Name) json[nameof(Name)] = Name;
        if (Position != _original.Position) json[nameof(Position)] = FormattableString.Invariant($"{Position.X},{Position.Y}");
        if (IsExpanded != _original.Expanded) json[nameof(IsExpanded)] = IsExpanded;
        if (IsEnabled != _original.Enabled) json[nameof(IsEnabled)] = IsEnabled;

        foreach (JsonObject member in EnumerateSavedMembers(json))
        {
            if (!TryGetId(member[nameof(Id)], out Guid id)) continue;
            // Recovery may reassign a port's Id after it was materialized. Keep the association
            // with its original payload by object identity, including when the old Id is reused.
            _restoredMembers.TryGetValue(id, out NodeMember? port);
            if (port != null && port.Id != id) member[nameof(Id)] = port.Id;
            if (port is InputPort<object> input && TryGetId(member[nameof(input.Connection)], out Guid connectionId)
                && connectionId != input.Connection.Id)
                member[nameof(input.Connection)] = input.Connection.Id;
            else if (port is IListPort list)
                WriteConnections(member, list.Connections);
            else if (port is IOutputPort output)
                WriteConnections(member, output.Connections);
        }
        if (json[nameof(NestedInputPorts)] is JsonArray nestedPorts)
        {
            foreach (JsonObject member in nestedPorts.OfType<JsonObject>())
                if (TryGetId(member[nameof(INestedInputPort.RootMember)], out Guid rootId)
                    && _restoredMembers.TryGetValue(rootId, out NodeMember? root) && root.Id != rootId)
                    member[nameof(INestedInputPort.RootMember)] = root.Id;
        }
        jsonContext.SetJsonObject(json);
    }

    internal IEnumerable<Guid> GetSavedMemberIds()
        => _savedMembers.Keys.Select(id => _restoredMembers.TryGetValue(id, out NodeMember? port) ? port.Id : id);

    internal void RestoreConnection(Connection connection)
    {
        // These ports are a view of retained endpoints, not new user-created graph content.
        using var suppression = PublishingSuppression.Enter();
        ConnectionStatus status = connection.Status;
        if (GetPort(connection.Input.Id, input: true) is NodeMember input)
        {
            connection.SetValue(Connection.InputProperty, new Reference<NodeMember>(input));
            if (input is InputPort<object> single) single.Connection = connection;
            else RestoreListConnection(((IListPort)input).Connections, connection);
        }
        if (GetPort(connection.Output.Id, input: false) is NodeMember output)
        {
            connection.SetValue(Connection.OutputProperty, new Reference<NodeMember>(output));
            RestoreListConnection(((IOutputPort)output).Connections, connection);
        }
        connection.Status = status;
    }

    private NodeMember? GetPort(Guid id, bool input)
    {
        if (Items.FirstOrDefault(item => item.Id == id) is NodeMember existing)
            return (input ? existing is IInputPort : existing is IOutputPort) ? existing : null;
        // A retained key whose port has moved to a different Id no longer owns that old endpoint.
        if (_restoredMembers.ContainsKey(id) || !_savedMembers.TryGetValue(id, out JsonObject? saved)) return null;

        NodeMember port = input
            ? saved.ContainsKey("Connections") ? new ListInputPort<object>() : new InputPort<object>()
            : new OutputPort<object>();
        // Register the placeholder with the active reference resolver, overriding any partially
        // restored plugin port. Only known, validated fields enter the ordinary deserializer.
        var metadata = new JsonObject { [nameof(Id)] = id };
        if (saved[nameof(Name)] is JsonValue name && name.TryGetValue<string>(out var text)) metadata[nameof(Name)] = text;
        if (saved["Connections"] is JsonArray connections)
            metadata["Connections"] = new JsonArray(connections
                .Where(node => TryGetId(node, out _)).Select(node => node!.DeepClone()).ToArray());
        CoreSerializer.PopulateFromJsonObject(port, metadata);
        _restoredMembers.Add(id, port);
        Items.Add((INodeMember)port);
        return port;
    }

    private static void RestoreListConnection(Beutl.Collections.CoreList<Reference<Connection>> connections, Connection connection)
    {
        int index = connections.Index().FirstOrDefault(item => item.Item.Id == connection.Id, (-1, default)).Index;
        if (index < 0) connections.Add(connection);
        else connections[index] = connection;
    }

    private static void WriteConnections(JsonObject member, IEnumerable<Reference<Connection>> connections)
    {
        Guid[] ids = connections.Select(connection => connection.Id).ToArray();
        if (member["Connections"] is not JsonArray saved) return;
        if (saved.Where(node => TryGetId(node, out _))
            .Select(node => { TryGetId(node, out Guid id); return id; }).SequenceEqual(ids)) return;
        member["Connections"] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id))
            .Concat(saved.Where(node => !TryGetId(node, out _)).Select(node => node?.DeepClone())).ToArray());
    }

    private static IEnumerable<JsonObject> EnumerateSavedMembers(JsonObject? json)
    {
        if (json?[nameof(Items)] is JsonArray items)
            foreach (JsonObject member in items.OfType<JsonObject>()) yield return member;
        if (json?[nameof(NestedInputPorts)] is JsonArray nested)
            foreach (JsonObject member in nested.OfType<JsonObject>()) yield return member;
    }

    private static bool TryGetId(JsonNode? node, out Guid id)
    {
        id = default;
        return node is JsonValue value
            && (value.TryGetValue(out id) || value.TryGetValue<string>(out var text) && Guid.TryParse(text, out id));
    }
}
