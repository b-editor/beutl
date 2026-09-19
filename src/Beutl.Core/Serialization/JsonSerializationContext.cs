using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Beutl.Serialization;

public partial class JsonSerializationContext(
    Type ownerType,
    ICoreSerializationContext? parent = null,
    JsonObject? json = null,
    CoreSerializerOptions? options = null)
    : IJsonSerializationContext
{
    public readonly Dictionary<string, (Type DefinedType, Type ActualType)> _knownTypes = [];
    private List<(object, Guid, Action<ICoreSerializable>)>? _rootResolvers;
    private List<(Guid, Action<ICoreSerializable>)>? _resolvers;
    private Dictionary<Guid, ICoreSerializable>? _objects;
    private readonly JsonObject _json = json ?? [];
    private readonly object _migrationSync = new();
    private ICoreSerializable? _deserializedObject;
    private ICoreSerializable? _serializedObject;
    private string? _requiredMinAppVersionAfterMigration;
    private bool _acceptsPersistedContentMigrationReports;
    private bool _afterDeserializedCompleted;

    public ICoreSerializationContext? Parent { get; } = parent;

    public JsonSerializationContext Root => IsRoot ? this : (Parent as JsonSerializationContext)!.Root;

    public CoreSerializationMode Mode => options?.Mode ?? Parent?.Mode ?? CoreSerializationMode.ReadWrite;

    public Uri? BaseUri => options?.BaseUri ?? Parent?.BaseUri;

    public Type OwnerType { get; } = ownerType;

    public void ReportPersistedContentMigration(string minAppVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minAppVersion);
        if (!_acceptsPersistedContentMigrationReports
            && !Mode.HasFlag(CoreSerializationMode.Read))
        {
            throw new InvalidOperationException(
                "Persisted-content migrations can only be reported while deserializing.");
        }

        ICoreSerializable? deserializedObject = null;
        string? requiredVersion = null;
        lock (_migrationSync)
        {
            _requiredMinAppVersionAfterMigration = Project.GetMaximumMigrationVersion(
                _requiredMinAppVersionAfterMigration,
                minAppVersion);
            if (_afterDeserializedCompleted)
            {
                deserializedObject = _deserializedObject;
                requiredVersion = _requiredMinAppVersionAfterMigration;
            }
        }

        if (deserializedObject is not null && requiredVersion is not null)
        {
            PropagatePersistedContentMigration(deserializedObject, requiredVersion);
        }
    }

    internal void EnablePersistedContentMigrationReporting()
    {
        _acceptsPersistedContentMigrationReports = true;
    }

    /// <summary>
    /// Records which object this context is writing, so a migration requirement carried by a nested
    /// value can be handed to the owners that are persisting it.
    /// </summary>
    internal void BeginSerialization(ICoreSerializable obj)
    {
        _serializedObject = obj;
    }

    /// <summary>
    /// Hands the requirement retained for a value that is being written as part of
    /// <paramref name="owner"/> to every owner up to the root.
    /// </summary>
    /// <remarks>
    /// A value that is not a <see cref="CoreObject"/> keeps its requirement beside itself (see
    /// <see cref="AttachedContentMigrations"/>) because it may be assigned to a different owner
    /// after its own deserialization ended. Persisting it is the point at which that owner becomes
    /// known: each owning <see cref="CoreObject"/> takes the requirement over from here on, and the
    /// project being written picks it up in this same save. A <see cref="CoreObject"/> keeps its own
    /// requirement, but only the hierarchy hands it on, so one reached through an ordinary
    /// serialized property is carried here too.
    /// </remarks>
    internal static void TransferRetainedMigration(
        ICoreSerializable value,
        ICoreSerializationContext? owner)
    {
        if (owner is null)
        {
            return;
        }

        string? requiredVersion = value is CoreObject migrated
            ? migrated.RequiredMinAppVersionAfterMigration
            : AttachedContentMigrations.IsEmpty
                ? null
                : AttachedContentMigrations.Get(value);
        if (requiredVersion is null)
        {
            return;
        }

        // Decline unless every owner up to the root writes real persisted state. Resource
        // inspection walks the live project through a context of its own (see
        // VersionControlSerializationGraph), and its temporary reports must not reach the project.
        for (ICoreSerializationContext? context = owner; context is not null;)
        {
            if (context is not JsonSerializationContext jsonContext)
            {
                return;
            }

            context = jsonContext.Parent;
        }

        for (JsonSerializationContext? context = owner as JsonSerializationContext;
             context is not null;
             context = context.Parent as JsonSerializationContext)
        {
            context.MergeOwnerMigration(requiredVersion);
        }
    }

    private void MergeOwnerMigration(string requiredVersion)
    {
        if (_serializedObject is CoreObject owner)
        {
            owner.MergePersistedContentMigration(requiredVersion);
            if (owner is Project project)
            {
                project.MarkAsMigrated(requiredVersion);
            }
        }
    }

    [MemberNotNullWhen(false, nameof(Parent))]
    public bool IsRoot => Parent == null;

    public JsonObject GetJsonObject()
    {
        return _json;
    }

    public void SetJsonObject(JsonObject obj)
    {
        _json.Clear();
        JsonDeepClone.CopyTo(obj, _json);
    }

    public JsonNode? GetNode(string name)
    {
        return _json[name];
    }

    public void SetNode(string name, Type definedType, Type actualType, JsonNode? node)
    {
        _json[name] = node;
        _knownTypes[name] = (definedType, actualType);
    }

    public void Populate(string name, ICoreSerializable obj)
    {
        if (_json[name] is JsonObject jobj)
        {
            var context = new JsonSerializationContext(
                ownerType: obj.GetType(),
                parent: this,
                json: jobj);
            context.EnablePersistedContentMigrationReporting();

            using (ThreadLocalSerializationContext.Enter(context))
            {
                obj.Deserialize(context);
                context.AfterDeserialized(obj);
            }
        }
    }

    public void AfterDeserialized(ICoreSerializable obj)
    {
        lock (_migrationSync)
        {
            _deserializedObject = obj;
        }

        if (_resolvers?.Count > 0)
        {
            Root._rootResolvers ??= [];
            Root._rootResolvers.AddRange(_resolvers.Select(t => ((object)obj, t.Item1, t.Item2)));
            _resolvers.Clear();
        }

        if (obj is CoreObject coreObject)
        {
            SetObjectAndId(coreObject);

            if (IsRoot)
            {
                // Resolve references
                if (_rootResolvers is not null && _objects is not null)
                {
                    for (int i = _rootResolvers.Count - 1; i >= 0; i--)
                    {
                        var item = _rootResolvers[i];
                        var (self, id, callback) = item;
                        if (_objects.TryGetValue(id, out var resolved))
                        {
                            callback(resolved);
                            _rootResolvers.RemoveAt(i);
                        }
                        else if (coreObject is IHierarchical hierarchical)
                        {
                            var resolver = new ReferenceResolver(hierarchical, id);
                            resolver.Resolve().ContinueWith(t =>
                            {
                                callback(t.Result);
                                _rootResolvers?.Remove(item);
                            });
                        }
                        else
                        {
                            // Error
                        }
                    }
                }
            }
        }

        string? requiredVersion;
        lock (_migrationSync)
        {
            _afterDeserializedCompleted = true;
            requiredVersion = _requiredMinAppVersionAfterMigration;
        }

        if (requiredVersion is not null)
        {
            PropagatePersistedContentMigration(obj, requiredVersion);
        }
    }

    private void PropagatePersistedContentMigration(
        ICoreSerializable obj,
        string requiredVersion)
    {
        if (obj is CoreObject migratedObject)
        {
            migratedObject.MergePersistedContentMigration(requiredVersion);
            if (migratedObject is Project project)
            {
                project.MarkAsMigrated(requiredVersion);
            }
        }
        else
        {
            // Retained even when a parent is present: the value may later be assigned to an owner
            // outside the graph it was deserialized with, and only the value itself travels there.
            AttachedContentMigrations.Merge(obj, requiredVersion);
        }

        Parent?.ReportPersistedContentMigration(requiredVersion);
    }

    private void SetObjectAndId(CoreObject coreObject)
    {
        if (IsRoot)
        {
            _objects ??= new Dictionary<Guid, ICoreSerializable>();
            _objects[coreObject.Id] = coreObject;
        }
        else
        {
            Root.SetObjectAndId(coreObject);
        }
    }

    public void Resolve(Guid id, Action<ICoreSerializable> callback)
    {
        _resolvers ??= [];
        _resolvers.Add((id, callback));
    }

    public bool Contains(string name)
    {
        return _json.ContainsKey(name);
    }
}
