using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

// Values that loading dropped, leaving no fallback that saving writes, such as a nested fallback that a type's own
// Deserialize throws away. Only an edit that replaces or removes the member that held one, or an object above it, repairs.
internal sealed partial class SceneRecovery
{
    private static readonly ConditionalWeakTable<CoreObject, DroppedValues> s_droppedValues = new();
    private static readonly ConditionalWeakTable<Element, CoreObject[]> s_droppedValueOwners = new();

    // Records, for each fallback that saving the element does not write, the member of the nearest saved object it sat in.
    private static void RecordDroppedValues(
        Element element,
        IReadOnlyList<DeserializationIncidents.DeserializationIncident> incidents)
    {
        if (!incidents.Any(static incident => incident.Fallback is not null)) return;

        DeserializationIncidents.DeserializationIncident[] dropped;
        var savedById = new Dictionary<Guid, CoreObject?>();
        using (var saved = new SerializedObjectCapture())
        {
            CoreSerializer.SerializeToJsonObject(element, CreateRecoveryCheckOptions(element.Uri));
            dropped = incidents
                .Where(incident => incident.Fallback is { } fallback && !saved.Contains(fallback))
                .ToArray();
            foreach (CoreObject obj in saved.Objects)
            {
                savedById[obj.Id] = savedById.ContainsKey(obj.Id) ? null : obj;
            }
        }

        var incidentJson = new HashSet<JsonNode>(
            incidents.Select(static incident => incident.Json).OfType<JsonNode>(),
            ReferenceEqualityComparer.Instance);
        var members = new Dictionary<CoreObject, List<string?>>(ReferenceEqualityComparer.Instance);
        foreach (DeserializationIncidents.DeserializationIncident incident in dropped)
        {
            if (FindDroppedValueOwner(element, incident.Json, savedById, incidentJson) is not { } location) continue;

            (CoreObject owner, string? member) = location;
            if (!members.TryGetValue(owner, out List<string?>? list))
            {
                members[owner] = list = [];
            }

            if (!list.Contains(member)) list.Add(member);
        }

        if (members.Count == 0) return;

        foreach ((CoreObject owner, List<string?> list) in members)
        {
            s_droppedValues.AddOrUpdate(owner, new DroppedValues(element.Uri, list.ToArray()));
        }

        s_droppedValueOwners.AddOrUpdate(element, members.Keys.ToArray());
    }

    // The nearest saved object above json and the member of it json sat in, or null when json sits inside the JSON of
    // another fallback, which then covers it. Past an object that is not saved, the record covers the whole owner.
    private static (CoreObject Owner, string? Member)? FindDroppedValueOwner(
        Element element,
        JsonObject? json,
        Dictionary<Guid, CoreObject?> savedById,
        HashSet<JsonNode> incidentJson)
    {
        bool passedUnsavedObject = false;
        for (JsonNode? child = json, parent = json?.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (incidentJson.Contains(parent)) return null;
            if (parent is not JsonObject obj || !RecoveredElementJsonScanner.TryGetSerializedId(obj, out Guid id)) continue;

            if (savedById.GetValueOrDefault(id) is { } owner)
            {
                return (owner, passedUnsavedObject ? null : child!.GetPropertyName());
            }

            passedUnsavedObject = true;
        }

        return (element, null);
    }

    // Saves what each object of element that held a dropped value holds now, unless that was saved before.
    private static void SaveDroppedValues(Element element)
    {
        if (!s_droppedValueOwners.TryGetValue(element, out CoreObject[]? owners)) return;

        foreach (CoreObject owner in owners)
        {
            if (s_droppedValues.TryGetValue(owner, out DroppedValues? values)) values.EnsureSaved(owner);
        }
    }

    // Whether owner still holds the member of a value that loading dropped as it was saved before any edit.
    internal static bool HoldsUnresolvedDroppedValue(CoreObject owner)
    {
        return s_droppedValues.TryGetValue(owner, out DroppedValues? values) && values.IsUnresolved(owner);
    }

    // Whether loading dropped the value property held, so that an edit replacing it repairs; saves it before that edit.
    internal static bool HeldDroppedValue(IProperty? property)
    {
        if (property?.GetOwnerObject() is not { } owner
            || !s_droppedValues.TryGetValue(owner, out DroppedValues? values)
            || !values.Holds(property.Name))
        {
            return false;
        }

        values.EnsureSaved(owner);
        return true;
    }

    private static CoreSerializerOptions CreateRecoveryCheckOptions(Uri? baseUri)
    {
        return new CoreSerializerOptions
        {
            BaseUri = baseUri,
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
        };
    }

    // The members of an owner whose values loading dropped; a null member stands for the whole owner. They are saved at
    // the first check, after the scene finished loading, and a member is resolved once it saves something else.
    private sealed class DroppedValues(Uri? baseUri, string?[] members)
    {
        private JsonNode?[]? _saved;

        private bool CoversTheOwner => Array.IndexOf(members, null) >= 0;

        public bool Holds(string member) => !CoversTheOwner && Array.IndexOf(members, member) >= 0;

        public void EnsureSaved(CoreObject owner)
        {
            if (!CoversTheOwner) _saved ??= Save(owner);
        }

        public bool IsUnresolved(CoreObject owner)
        {
            if (CoversTheOwner) return true;

            JsonNode?[] current = Save(owner);
            _saved ??= current;
            for (int i = 0; i < current.Length; i++)
            {
                if (JsonNode.DeepEquals(current[i], _saved[i])) return true;
            }

            return false;
        }

        // What each member saves: its value, and its animation and expression for an engine property.
        private JsonNode?[] Save(CoreObject owner)
        {
            JsonObject json = CoreSerializer.SerializeToJsonObject(owner, CreateRecoveryCheckOptions(baseUri));
            return members
                .Select(member => (JsonNode?)new JsonArray(
                    json[member!]?.DeepClone(),
                    (json["Animations"] as JsonObject)?[member!]?.DeepClone(),
                    (json["Expressions"] as JsonObject)?[member!]?.DeepClone()))
                .ToArray();
        }
    }
}
