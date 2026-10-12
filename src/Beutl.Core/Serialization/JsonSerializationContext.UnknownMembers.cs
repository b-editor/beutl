using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

// Unknown members: the members of an object's JSON that the object does not read. Reading keeps them on
// the object and writing writes them back, so data that a newer version of Beutl or of an extension wrote
// survives an older one.
public partial class JsonSerializationContext
{
    private static readonly ConditionalWeakTable<Type, string[]> s_droppedMembers = new();

    // The member keys the object read through this context. Reading the JSON object itself reads them all.
    private HashSet<string>? _readKeys;
    private bool _readsAllKeys;

    // Whether reading keeps unknown members and writing writes them back. Unless the options say otherwise,
    // a context does what the read or write around it does.
    internal bool KeepsUnknownMembers
        => options?.KeepUnknownMembers ?? (Parent as JsonSerializationContext)?.KeepsUnknownMembers ?? true;

    private void MarkRead(string name)
    {
        (_readKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
    }

    /// <summary>
    /// Keeps on <paramref name="obj"/> the members of this context's JSON that it did not read while it was
    /// deserialized from it.
    /// </summary>
    /// <remarks>
    /// A populated object merges them into what it kept before, so a populate that leaves a member out
    /// keeps it, as it keeps the value of a known property it leaves out. A fallback keeps its whole JSON
    /// already, and an object that read the JSON object itself is taken to have read every key.
    /// </remarks>
    internal void KeepUnknownMembers(ICoreSerializable obj)
    {
        if (obj is not CoreObject coreObject || obj is IFallback || _readsAllKeys || !KeepsUnknownMembers) return;

        JsonObject? unknown = null;
        foreach ((string key, JsonNode? node) in _json)
        {
            if (_readKeys?.Contains(key) == true || IsReservedKey(key) || IsDroppedMember(obj.GetType(), key))
                continue;

            (unknown ??= [])[key] = node?.DeepClone();
        }

        if (unknown is null) return;

        coreObject.KeepUnknownMembers(unknown);
        RootFileVersions.RecordKeptUnknownData();
    }

    // Writes the unknown members obj kept back into this context's JSON, after obj wrote its own members. A
    // member obj wrote itself wins.
    internal void WriteUnknownMembers(ICoreSerializable obj)
    {
        if (obj is not CoreObject { UnknownMembers: { } unknown } || !KeepsUnknownMembers) return;

        foreach ((string key, JsonNode? node) in unknown)
        {
            if (!_json.ContainsKey(key))
            {
                _json[key] = node?.DeepClone();
            }
        }
    }

    // Keys that are never members: discriminators and the other metadata keys that start with '$', the legacy
    // "@type", the Uri an embedded reference carries, and the version fields of a root file, which the load
    // gate takes out before an object reads the file (see FileFormat).
    internal static bool IsReservedKey(string key)
    {
        return key.StartsWith('$')
               || key is "@type" or "Uri" or FileFormat.AppVersionKey or FileFormat.MinAppVersionKey;
    }

    private static bool IsDroppedMember(Type type, string key)
    {
        string[] dropped = s_droppedMembers.GetValue(type, static t => t
            .GetCustomAttributes(typeof(DroppedMembersAttribute), inherit: true)
            .Cast<DroppedMembersAttribute>()
            .SelectMany(static attribute => attribute.Names)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        return Array.IndexOf(dropped, key) >= 0;
    }
}
