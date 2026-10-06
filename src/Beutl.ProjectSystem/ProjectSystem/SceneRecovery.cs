using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

// Owns recovery metadata and reconciles restored elements before the scene becomes editable.
internal sealed partial class SceneRecovery(Scene scene)
{
    private readonly Scene _scene = scene;
    private Uri? Uri => _scene.Uri;
    private Guid Id => _scene.Id;
    private Elements Children => _scene.Children;
    private IEnumerable<TimelineLayer> Layers => _scene.Layers;
    private IEnumerable<SceneMarker> Markers => _scene.Markers;

    internal const string RecoveredDescendantIdsKey = "RecoveredDescendantIds";
    internal const string RecoveredDescendantIdentitiesKey = "RecoveredDescendantIdentities";
    internal const string RecoveredElementIdsKey = "RecoveredElementIds";
    private readonly Dictionary<string, Guid> _recoveredDescendantIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _recoveredDescendantIdentities = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<CoreObject, RecoveredDescendantRemap> _recoveredDescendantRemaps = new();
    private readonly Dictionary<string, Guid> _recoveredElementIds = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Guid> _pendingRecoveredElementIdMigrations = [];
    private readonly Dictionary<Guid, Guid> _pendingRecoveredDescendantIdMigrations = [];
    private readonly ConditionalWeakTable<CoreObject, IdlessRecoveredDescendant> _idlessRecoveredDescendants = new();

    public Guid MapElementId(Guid id) => _pendingRecoveredElementIdMigrations.GetValueOrDefault(id, id);

    public void Reconcile()
    {
        ReassignDuplicateRecoveredIds();
        MigrateRecoveredElementReferences();
    }

    public void ReadMetadata(ICoreSerializationContext context)
    {
        _pendingRecoveredElementIdMigrations.Clear();
        _pendingRecoveredDescendantIdMigrations.Clear();
        _idlessRecoveredDescendants.Clear();
        _recoveredDescendantIds.Clear();
        _recoveredDescendantIdentities.Clear();
        _recoveredDescendantRemaps.Clear();
        _recoveredElementIds.Clear();
        ReadGuidMap(context, RecoveredElementIdsKey, _recoveredElementIds, normalizeKeys: true);
        ReadGuidMap(context, RecoveredDescendantIdsKey, _recoveredDescendantIds, normalizeKeys: true);
        ReadGuidMap(context, RecoveredDescendantIdentitiesKey, _recoveredDescendantIdentities, normalizeKeys: false);
    }

    public void WriteMetadata(ICoreSerializationContext context)
    {
        RecoveredSerializationState recoveredState = BuildRecoveredSerializationState();
        WriteGuidMap(context, RecoveredElementIdsKey, recoveredState.ElementIds);
        WriteGuidMap(context, RecoveredDescendantIdsKey, recoveredState.DescendantIds);
        WriteGuidMap(context, RecoveredDescendantIdentitiesKey, recoveredState.DescendantIdentities);
    }

    private static void ReadGuidMap(
        ICoreSerializationContext context,
        string key,
        Dictionary<string, Guid> target,
        bool normalizeKeys)
    {
        if (context.GetValue<JsonNode>(key) is JsonObject map)
        {
            foreach ((string entryKey, JsonNode? idNode) in map)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    target[normalizeKeys ? RecoveredIdentity.NormalizeRelativePath(entryKey) : entryKey] = id;
                }
            }
        }
    }

    private static void WriteGuidMap(
        ICoreSerializationContext context,
        string key,
        IReadOnlyDictionary<string, Guid> map)
    {
        if (map.Count > 0)
        {
            var json = new JsonObject();
            foreach ((string entryKey, Guid id) in map.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                json[entryKey] = id.ToString();
            }

            context.SetValue(key, json);
        }
    }

    private static string GetSceneRelativePath(string sceneDirectory, Uri uri)
    {
        return RecoveredIdentity.NormalizeRelativePath(Path.GetRelativePath(sceneDirectory, uri.LocalPath));
    }

    private static IEnumerable<IFallback> EnumerateSerializedGraphFallbacks(Element element)
    {
        return EnumerateSerializedGraphObjects(element).OfType<IFallback>();
    }

    private static IEnumerable<CoreObject> EnumerateSerializedGraphDescendants(Element element)
    {
        return EnumerateSerializedGraphObjects(element)
            .OfType<CoreObject>()
            .Where(value => !ReferenceEquals(value, element));
    }

    private void MigrateRecoveredElementReferences()
    {
        if (_pendingRecoveredElementIdMigrations.Count == 0
            && _pendingRecoveredDescendantIdMigrations.Count == 0)
        {
            return;
        }

        var referenceTargets = new Dictionary<Guid, CoreObject>();
        foreach (CoreObject candidate in EnumerateSerializedGraphObjects(Children).OfType<CoreObject>())
        {
            referenceTargets.TryAdd(candidate.Id, candidate);
        }

        var rewriter = new RecoveredReferenceRewriter(
            _pendingRecoveredElementIdMigrations,
            _pendingRecoveredDescendantIdMigrations,
            referenceTargets);
        foreach (CoreObject coreObject in EnumerateSerializedGraphObjects(_scene).OfType<CoreObject>())
        {
            MigrateRegisteredPropertyReferences(coreObject, rewriter);
            if (coreObject is EngineObject engineObject)
            {
                MigrateEnginePropertyReferences(engineObject, rewriter);
            }
        }
    }

    private static void MigrateRegisteredPropertyReferences(
        CoreObject coreObject,
        RecoveredReferenceRewriter rewriter)
    {
        foreach (CoreProperty property in PropertyRegistry.GetRegistered(coreObject.GetType()))
        {
            if (!property.GetMetadata<CorePropertyMetadata>(coreObject.GetType()).ShouldSerialize)
            {
                continue;
            }

            object? currentValue = coreObject.GetValue(property);
            object? migratedValue = rewriter.Rewrite(currentValue);
            if (RecoveredReferenceRewriter.HasReferenceRewrite(currentValue, migratedValue)
                && property is not IStaticProperty { CanWrite: false })
            {
                coreObject.ReplaceValue(property, migratedValue);
            }
        }
    }

    private static void MigrateEnginePropertyReferences(
        EngineObject engineObject,
        RecoveredReferenceRewriter rewriter)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            object? currentValue = property.CurrentValue;
            object? migratedValue = rewriter.Rewrite(currentValue);
            if (RecoveredReferenceRewriter.HasReferenceRewrite(currentValue, migratedValue))
            {
                property.ReplaceCurrentValue(migratedValue);
            }

            if (property.Expression is IReferenceRewritable)
            {
                property.Expression = (IExpression?)rewriter.Rewrite(property.Expression);
            }
            else if (property.Expression is IReferenceExpression referenceExpression
                && rewriter.TryGetMigratedId(referenceExpression.ObjectId, out Guid migratedExpressionId)
                && referenceExpression.Rebind(migratedExpressionId) is { } reboundExpression)
            {
                property.Expression = (IExpression)reboundExpression;
            }

            if (property.Animation is IKeyFrameAnimation animation)
            {
                MigrateKeyFrameReferences(animation, rewriter);
            }
        }
    }

    private static void MigrateKeyFrameReferences(
        IKeyFrameAnimation animation,
        RecoveredReferenceRewriter rewriter)
    {
        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            object? keyFrameValue = keyFrame.Value;
            object? migratedKeyFrameValue = rewriter.Rewrite(keyFrameValue);
            if (RecoveredReferenceRewriter.HasReferenceRewrite(keyFrameValue, migratedKeyFrameValue))
            {
                keyFrame.ReplaceValue(migratedKeyFrameValue);
            }
        }
    }

    private static IEnumerable<object> EnumerateSerializedGraphObjects(object root)
        => SerializedGraphTraversal.Enumerate(root);

    private RecoveredSerializationState BuildRecoveredSerializationState()
    {
        if (Uri is null)
        {
            return new RecoveredSerializationState(
                new Dictionary<string, Guid>(_recoveredElementIds, StringComparer.Ordinal),
                new Dictionary<string, Guid>(_recoveredDescendantIds, StringComparer.Ordinal),
                new Dictionary<string, Guid>(_recoveredDescendantIdentities, StringComparer.Ordinal));
        }

        var recoveredChildren = Children.Where(
                static child => child.SuppressedStorageSource is not null)
            .ToArray();
        if (recoveredChildren.Length == 0)
        {
            return RecoveredSerializationState.Empty;
        }

        string sceneDirectory = Path.GetDirectoryName(Uri.LocalPath)!;
        var elementIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var descendantIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var descendantIdentities = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (Element child in recoveredChildren)
        {
            string relativePath = GetSceneRelativePath(sceneDirectory, child.Uri!);
            elementIds[relativePath] = child.Id;
            foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                     SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths(child))
            {
                if (descendant is IFallback)
                {
                    string identityKey = RecoveredIdentity.CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
                    descendantIdentities[identityKey] = descendant.Id;
                    if (graphPath.Positional != graphPath.Stable)
                    {
                        string positionalIdentityKey = RecoveredIdentity.CreateRecoveredDescendantIdentityKey(
                            relativePath,
                            graphPath.Positional);
                        descendantIdentities[positionalIdentityKey] = descendant.Id;
                    }
                }

                if (_recoveredDescendantRemaps.TryGetValue(descendant, out RecoveredDescendantRemap? remap)
                    && descendant.Id == remap.AssignedId)
                {
                    string remapKey = RecoveredIdentity.CreateRecoveredDescendantKey(
                        relativePath,
                        remap.OriginalId,
                        remap.Occurrence);
                    descendantIds[remapKey] = remap.AssignedId;
                }
            }
        }

        return new RecoveredSerializationState(elementIds, descendantIds, descendantIdentities);
    }

    private sealed record RecoveredSerializationState(
        IReadOnlyDictionary<string, Guid> ElementIds,
        IReadOnlyDictionary<string, Guid> DescendantIds,
        IReadOnlyDictionary<string, Guid> DescendantIdentities)
    {
        public static RecoveredSerializationState Empty { get; } = new(
            new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>());
    }

    private sealed record RecoveredDescendantRemap(
        Guid OriginalId,
        Guid AssignedId,
        int Occurrence);
}
