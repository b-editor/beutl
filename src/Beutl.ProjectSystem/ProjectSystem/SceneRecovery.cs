using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.ProjectSystem;

// Owns recovery metadata and reconciles restored elements before the scene becomes editable.
internal sealed class SceneRecovery(Scene scene)
{
    private readonly Scene _scene = scene;
    private Uri? Uri => _scene.Uri;
    private Guid Id => _scene.Id;
    private Elements Children => _scene.Children;
    private IEnumerable<TimelineLayer> Layers => _scene.Layers;
    private IEnumerable<SceneMarker> Markers => _scene.Markers;

    private const int MaxRecoveredIdCollisionAttempts = 1024;
    private const string RecoveredDescendantIdsKey = "RecoveredDescendantIds";
    private const string RecoveredDescendantIdentitiesKey = "RecoveredDescendantIdentities";
    private const string RecoveredElementIdsKey = "RecoveredElementIds";
    private static readonly Guid s_recoveredElementNamespace = new("dfad2f76-1d04-5593-ae3b-f371fb1f42ee");
    private static readonly Regex s_idPattern = new(
        "\"Id\"\\s*:\\s*\"(?<id>[0-9a-fA-F-]{36})\"",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_typePattern = new(
        "\"\\$type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);
    private static readonly Regex s_legacyTypePattern = new(
        "\"@type\"\\s*:\\s*(?<type>\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.CultureInvariant);
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
        if (context.GetValue<JsonNode>(RecoveredElementIdsKey) is JsonObject recoveredElementIds)
        {
            foreach ((string path, JsonNode? idNode) in recoveredElementIds)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredElementIds[NormalizeRelativePath(path)] = id;
                }
            }
        }

        if (context.GetValue<JsonNode>(RecoveredDescendantIdsKey) is JsonObject recoveredDescendantIds)
        {
            foreach ((string key, JsonNode? idNode) in recoveredDescendantIds)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredDescendantIds[NormalizeRelativePath(key)] = id;
                }
            }
        }

        if (context.GetValue<JsonNode>(RecoveredDescendantIdentitiesKey) is JsonObject recoveredDescendantIdentities)
        {
            foreach ((string key, JsonNode? idNode) in recoveredDescendantIdentities)
            {
                if (idNode is JsonValue idValue
                    && idValue.TryGetValue(out string? idText)
                    && Guid.TryParse(idText, out Guid id))
                {
                    _recoveredDescendantIdentities[key] = id;
                }
            }
        }

    }

    public void WriteMetadata(ICoreSerializationContext context)
    {
        RecoveredSerializationState recoveredState = BuildRecoveredSerializationState();
        if (recoveredState.ElementIds.Count > 0)
        {
            var recoveredElementIds = new JsonObject();
            foreach ((string path, Guid id) in recoveredState.ElementIds.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                recoveredElementIds[path] = id.ToString();
            }

            context.SetValue(RecoveredElementIdsKey, recoveredElementIds);
        }

        if (recoveredState.DescendantIds.Count > 0)
        {
            var recoveredDescendantIds = new JsonObject();
            foreach ((string key, Guid id) in recoveredState.DescendantIds.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                recoveredDescendantIds[key] = id.ToString();
            }

            context.SetValue(RecoveredDescendantIdsKey, recoveredDescendantIds);
        }

        if (recoveredState.DescendantIdentities.Count > 0)
        {
            var recoveredDescendantIdentities = new JsonObject();
            foreach ((string key, Guid id) in recoveredState.DescendantIdentities.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                recoveredDescendantIdentities[key] = id.ToString();
            }

            context.SetValue(RecoveredDescendantIdentitiesKey, recoveredDescendantIdentities);
        }

    }

    private void ReassignDuplicateRecoveredIds()
    {
        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        var recoveredChildren = Children
            .Where(static child => child.SuppressedStorageSource is not null)
            .Select(child => (
                Child: child,
                RelativePath: NormalizeRelativePath(
                    Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath))))
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var claimedIds = new HashSet<Guid> { Guid.Empty, Id };
        claimedIds.UnionWith(EnumerateSceneOwnedObjects().Select(static obj => obj.Id));

        var seenDescendants = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        var persistedDescendantIds = new Dictionary<string, Guid>(
            _recoveredDescendantIds,
            StringComparer.Ordinal);
        var persistedDescendantIdentities = new Dictionary<string, Guid>(
            _recoveredDescendantIdentities,
            StringComparer.Ordinal);
        var healthyChildren = Children
            .Where(static child => child.SuppressedStorageSource is null)
            .Select(child => (
                Child: child,
                RelativePath: NormalizeRelativePath(
                    Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath))))
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();

        void ClaimHealthyDescendants(Element child)
        {
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                seenDescendants.Add(descendant);
                claimedIds.Add(descendant.Id);
            }
        }

        void ClaimPreviouslyRecoveredHealthyDescendants(Element child, string relativePath)
        {
            var occurrences = new Dictionary<Guid, int>();
            var legacyIndices = new Dictionary<CoreObject, int>(ReferenceEqualityComparer.Instance);
            int legacyIndex = 0;
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                legacyIndices.TryAdd(descendant, legacyIndex++);
            }

            foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                     EnumerateSerializedGraphDescendantPaths(child))
            {
                if (!seenDescendants.Add(descendant))
                {
                    continue;
                }

                string identityKey = CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
                Guid originalId = descendant.Id;
                int occurrence = occurrences.GetValueOrDefault(originalId);
                occurrences[originalId] = occurrence + 1;
                string remapKey = CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
                bool hasPersistedId = persistedDescendantIds.TryGetValue(remapKey, out Guid persistedId);
                bool hasPersistedIdentity = persistedDescendantIdentities.TryGetValue(
                    identityKey,
                    out Guid persistedIdentityId);
                bool ambiguousPositionalIdentity = false;
                if (!hasPersistedIdentity && graphPath.Positional != graphPath.Stable)
                {
                    hasPersistedIdentity = TryGetRecoveredDescendantPositionalIdentity(
                        persistedDescendantIdentities,
                        relativePath,
                        graphPath.Positional,
                        out persistedIdentityId,
                        out ambiguousPositionalIdentity);
                }

                if (!hasPersistedIdentity
                    && !ambiguousPositionalIdentity
                    && legacyIndices.TryGetValue(descendant, out int persistedIndex))
                {
                    string legacyIdentityKey = CreateLegacyRecoveredDescendantIdentityKey(
                        relativePath,
                        persistedIndex);
                    hasPersistedIdentity = persistedDescendantIdentities.TryGetValue(
                        legacyIdentityKey,
                        out persistedIdentityId);
                }

                bool hasPreviousAssignedId = hasPersistedId || hasPersistedIdentity;
                Guid previousAssignedId = hasPersistedId ? persistedId : persistedIdentityId;

                if (claimedIds.Add(originalId))
                {
                    if (hasPreviousAssignedId)
                    {
                        _pendingRecoveredDescendantIdMigrations.TryAdd(previousAssignedId, originalId);
                    }

                    continue;
                }

                Guid assignedId = hasPreviousAssignedId && claimedIds.Add(previousAssignedId)
                    ? previousAssignedId
                    : ClaimRecoveredDescendantId(relativePath, remapKey, claimedIds);
                descendant.Id = assignedId;
                _pendingRecoveredDescendantIdMigrations.TryAdd(
                    hasPreviousAssignedId ? previousAssignedId : assignedId,
                    assignedId);
            }
        }

        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => !_recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            claimedIds.Add(child.Id);
            ClaimHealthyDescendants(child);
        }

        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => _recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            if (!claimedIds.Add(child.Id))
            {
                Guid placeholderId = _recoveredElementIds[relativePath];
                child.Id = claimedIds.Add(placeholderId)
                    ? placeholderId
                    : ClaimRecoveredElementId(relativePath, claimedIds);
            }

            ClaimPreviouslyRecoveredHealthyDescendants(child, relativePath);
        }

        foreach ((Element child, string relativePath) in healthyChildren)
        {
            if (_recoveredElementIds.Remove(relativePath, out Guid placeholderId))
            {
                _pendingRecoveredElementIdMigrations.TryAdd(placeholderId, child.Id);
            }
        }

        var recoveredPaths = recoveredChildren
            .Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string path in _recoveredElementIds.Keys.Where(path => !recoveredPaths.Contains(path)).ToArray())
        {
            _recoveredElementIds.Remove(path);
        }

        var persistedChildren = new HashSet<Element>();
        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            if (_recoveredElementIds.TryGetValue(relativePath, out Guid persistedId))
            {
                // A persisted remap that a healthy element now owns is stale; drop it so the
                // derivation loop assigns a fresh deterministic identity instead of a duplicate.
                if (claimedIds.Add(persistedId))
                {
                    child.Id = persistedId;
                    persistedChildren.Add(child);
                }
                else
                {
                    _recoveredElementIds.Remove(relativePath);
                }
            }
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            if (persistedChildren.Contains(child))
            {
                continue;
            }

            if (claimedIds.Add(child.Id))
            {
                continue;
            }

            child.Id = ClaimRecoveredElementId(relativePath, claimedIds);
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            _recoveredElementIds[relativePath] = child.Id;
        }

        _recoveredDescendantIds.Clear();
        _recoveredDescendantRemaps.Clear();
        var pendingDescendantRemaps
            = new Dictionary<CoreObject, (string RemapKey, Guid OriginalId, int Occurrence)>(
                ReferenceEqualityComparer.Instance);
        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            var occurrences = new Dictionary<Guid, int>();
            int idlessOccurrence = 0;
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                if (!seenDescendants.Add(descendant))
                {
                    continue;
                }

                bool idless = _idlessRecoveredDescendants.TryGetValue(descendant, out _);
                Guid originalId = idless ? Guid.Empty : descendant.Id;
                int occurrence = idless
                    ? idlessOccurrence++
                    : occurrences.GetValueOrDefault(originalId);
                if (!idless)
                {
                    occurrences[originalId] = occurrence + 1;
                }

                string remapKey = CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
                if (persistedDescendantIds.TryGetValue(remapKey, out Guid persistedId))
                {
                    if (claimedIds.Add(persistedId))
                    {
                        descendant.Id = persistedId;
                        RecordRecoveredDescendantRemap(
                            descendant,
                            remapKey,
                            originalId,
                            persistedId,
                            occurrence);
                        continue;
                    }

                    pendingDescendantRemaps[descendant] = (remapKey, originalId, occurrence);
                }
                else if (!claimedIds.Add(originalId))
                {
                    pendingDescendantRemaps[descendant] = (remapKey, originalId, occurrence);
                }
            }
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
            {
                if (!pendingDescendantRemaps.Remove(
                        descendant,
                        out (string RemapKey, Guid OriginalId, int Occurrence) remap))
                {
                    continue;
                }

                Guid candidate = ClaimRecoveredDescendantId(relativePath, remap.RemapKey, claimedIds);
                descendant.Id = candidate;
                RecordRecoveredDescendantRemap(
                    descendant,
                    remap.RemapKey,
                    remap.OriginalId,
                    candidate,
                    remap.Occurrence);

            }
        }

        // A global OriginalId -> AssignedId migration would redirect every reference that still
        // targets a surviving object. Only migrate when the original ID was abandoned entirely;
        // otherwise the references keep pointing at the object that retained it.
        var retainedIds = new HashSet<Guid> { Guid.Empty, Id };
        foreach (CoreObject graphObject in EnumerateSerializedGraphObjects(Children).OfType<CoreObject>())
        {
            retainedIds.Add(graphObject.Id);
        }

        retainedIds.UnionWith(EnumerateSceneOwnedObjects().Select(static obj => obj.Id));

        foreach (Guid originalId in _pendingRecoveredElementIdMigrations.Keys.ToArray())
        {
            if (_pendingRecoveredElementIdMigrations[originalId] != originalId
                && retainedIds.Contains(originalId))
            {
                _pendingRecoveredElementIdMigrations.Remove(originalId);
            }
        }

        foreach (Guid originalId in _pendingRecoveredDescendantIdMigrations.Keys.ToArray())
        {
            if (_pendingRecoveredDescendantIdMigrations[originalId] != originalId
                && retainedIds.Contains(originalId))
            {
                _pendingRecoveredDescendantIdMigrations.Remove(originalId);
            }
        }
    }

    private static Guid ClaimRecoveredElementId(string relativePath, ISet<Guid> claimedIds)
    {
        for (int attempt = 0; attempt < MaxRecoveredIdCollisionAttempts; attempt++)
        {
            string candidateName = attempt == 0
                ? relativePath
                : $"{relativePath}#{attempt}";
            Guid candidate = CreateVersion5Guid(s_recoveredElementNamespace, candidateName);
            if (claimedIds.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a unique recovered element Id for '{relativePath}'.");
    }

    private IEnumerable<CoreObject> EnumerateSceneOwnedObjects()
    {
        // Scene serializes these collections explicitly, independent of property metadata.
        foreach (CoreObject sceneObject in Layers.Cast<CoreObject>().Concat(Markers))
        {
            foreach (CoreObject obj in EnumerateSceneOwnedGraphObjects(sceneObject))
                yield return obj;
        }

        foreach (CoreProperty property in PropertyRegistry.GetRegistered(_scene.GetType()))
        {
            if (property == Scene.ChildrenProperty || property == Scene.LayersProperty || property == Scene.MarkersProperty
                || !property.GetMetadata<CorePropertyMetadata>(_scene.GetType()).ShouldSerialize
                || _scene.GetValue(property) is not { } value)
            {
                continue;
            }

            foreach (CoreObject obj in EnumerateSceneOwnedGraphObjects(value))
                yield return obj;
        }
    }

    private IEnumerable<CoreObject> EnumerateSceneOwnedGraphObjects(object value)
    {
        using var capture = new SerializedObjectCapture();
        CoreSerializer.SerializeToJsonNode(value, new CoreSerializerOptions
        {
            BaseUri = Uri,
            Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
        });
        return EnumerateSerializedGraphObjects(value).OfType<CoreObject>()
            .Concat(capture.Objects).Distinct<CoreObject>(ReferenceEqualityComparer.Instance).ToArray();
    }

    private static Guid ClaimRecoveredDescendantId(
        string relativePath,
        string remapKey,
        ISet<Guid> claimedIds)
    {
        for (int attempt = 0; attempt < MaxRecoveredIdCollisionAttempts; attempt++)
        {
            string candidateName = attempt == 0
                ? remapKey
                : $"{remapKey}#{attempt}";
            Guid candidate = CreateVersion5Guid(s_recoveredElementNamespace, candidateName);
            if (claimedIds.Add(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not assign a unique recovered descendant Id for '{relativePath}'.");
    }

    private void RecordRecoveredDescendantRemap(
        CoreObject descendant,
        string remapKey,
        Guid originalId,
        Guid assignedId,
        int occurrence)
    {
        _recoveredDescendantIds[remapKey] = assignedId;
        _recoveredDescendantRemaps.Remove(descendant);
        _recoveredDescendantRemaps.Add(
            descendant,
            new RecoveredDescendantRemap(originalId, assignedId, occurrence));
        if (originalId != Guid.Empty)
        {
            _pendingRecoveredDescendantIdMigrations.TryAdd(originalId, assignedId);
        }

        if (descendant is IFallback fallback)
        {
            EnsureFallbackProjection(fallback);
        }
    }

    public Element RestoreElement(Uri uri)
    {
        using DeserializationIncidents.Capture incidentCapture = DeserializationIncidents.BeginCapture();
        using var storageCapture = new ReferencedStorageCapture();
        try
        {
            Element element = CoreSerializer.RestoreFromUri<Element>(uri);
            IFallback[] fallbacks = EnumerateSerializedGraphFallbacks(element).ToArray();
            int incidentCount = incidentCapture.Count;

            if (fallbacks.Length > 0 || incidentCount > 0)
            {
                var traversedFallbacks = new HashSet<IFallback>(
                    fallbacks,
                    ReferenceEqualityComparer.Instance);
                DeserializationIncidents.DeserializationIncident[] untraversedIncidents
                    = incidentCapture.Incidents
                        .Where(incident => incident.Fallback is null
                                           || !traversedFallbacks.Contains(incident.Fallback))
                        .ToArray();
                JsonObject[] untraversedFallbacks = untraversedIncidents
                    .Where(static incident => incident.Fallback?.Json != null)
                    .Select(static incident => incident.Fallback!.Json!.DeepClone().AsObject())
                    .ToArray();
                SuppressedRecoveryIncident[] recoveryIncidents = untraversedIncidents
                    .Select(CreateSuppressedRecoveryIncident)
                    .ToArray();
                foreach (IFallback fallback in fallbacks)
                {
                    if (fallback is CoreObject fallbackObject)
                    {
                        if (TryGetSerializedId(fallback.Json, out Guid serializedId))
                        {
                            fallbackObject.Id = serializedId;
                        }
                        else
                        {
                            _idlessRecoveredDescendants.GetValue(
                                fallbackObject,
                                static _ => new IdlessRecoveredDescendant());
                        }
                    }

                    EnsureFallbackProjection(fallback);
                }

                MarkRecoveredElement(
                    element,
                    File.ReadAllBytes(uri.LocalPath),
                    uri,
                    recoveryIncidents.Length > 0,
                    untraversedFallbacks,
                    recoveryIncidents,
                    storageCapture.Sources);
            }

            return element;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex)
                                   && !ExceptionHelpers.ContainsNonRecoverableFileSystemFailure(ex))
        {
            // Raw bytes, not text: the sidecar must survive rehoming byte-identically even when it
            // holds a BOM, another encoding, or undecodable bytes. The lossy decode is only scanned
            // for top-level recovery metadata.
            byte[] rawBytes = File.ReadAllBytes(uri.LocalPath);
            string rawText = DecodeRecoveryMetadata(rawBytes);
            byte[] metadataBytes = Encoding.UTF8.GetBytes(rawText);
            JsonObject? root = TryParseTopLevelObject(rawText);
            var element = new Element
            {
                Id = ResolveRecoveredElementId(metadataBytes, rawText, root, uri),
                Name = Path.GetFileNameWithoutExtension(uri.LocalPath),
                Uri = uri,
                IsEnabled = false,
            };
            string? topLevelTypeName = TryGetTopLevelTypeName(metadataBytes, rawText, root);
            FallbackReason fallbackReason = topLevelTypeName is not null
                                            && TypeFormat.ToType(topLevelTypeName) is null
                ? FallbackReason.TypeNotFound
                : FallbackReason.DeserializationFailed;
            var fallback = new FallbackEngineObject
            {
                Name = "Unreadable element data",
                Reason = fallbackReason,
                ErrorMessage = fallbackReason == FallbackReason.DeserializationFailed
                    ? $"{ex.GetType().Name}: {ex.Message}"
                    : null,
            };
            fallback.Json = CreateFallbackProjection(fallback, topLevelTypeName);
            element.AddObject(fallback);
            _idlessRecoveredDescendants.GetValue(
                fallback,
                static _ => new IdlessRecoveredDescendant());
            MarkRecoveredElement(element, rawBytes, uri, referencedSources: storageCapture.Sources);
            return element;
        }
    }

    private static bool TryGetSerializedId(JsonObject? json, out Guid id)
    {
        id = Guid.Empty;
        return json is not null
               && json.TryGetPropertyValue(nameof(CoreObject.Id), out JsonNode? idNode)
               && idNode is JsonValue idValue
               && idValue.TryGetValue(out string? idText)
               && Guid.TryParse(idText, out id)
               && id != Guid.Empty;
    }

    private sealed class IdlessRecoveredDescendant;

    private void MarkRecoveredElement(
        Element element,
        byte[] rawBytes,
        Uri uri,
        bool hasNonFallbackIncidents = false,
        JsonObject[]? untraversedFallbacks = null,
        SuppressedRecoveryIncident[]? recoveryIncidents = null,
        IReadOnlyList<Uri>? referencedSources = null)
    {
        string sourceRootPath = Path.GetDirectoryName(Uri?.LocalPath ?? uri.LocalPath)
                                ?? throw new JsonException("Recovered element has no source directory.");
        element.SuppressedStorageSource = new SuppressedStorageSource(
            rawBytes,
            uri,
            hasNonFallbackIncidents,
            untraversedFallbacks,
            CollectReferencedStorageSources(element, uri, sourceRootPath, referencedSources ?? []),
            sourceRootPath,
            recoveryIncidents);
    }

    private static SuppressedRecoveryIncident CreateSuppressedRecoveryIncident(
        DeserializationIncidents.DeserializationIncident incident)
    {
        if (incident.Fallback is { } fallback)
        {
            fallback.TryGetTypeName(out string? typeName);
            return new SuppressedRecoveryIncident(
                fallback.Reason.ToString(),
                typeName,
                fallback.ErrorMessage);
        }

        return new SuppressedRecoveryIncident(
            incident.Reason?.ToString() ?? nameof(FallbackReason.DeserializationFailed),
            incident.TypeName,
            incident.Message);
    }

    private static SuppressedReferencedStorageSource[]? CollectReferencedStorageSources(
        Element element,
        Uri elementUri,
        string sourceRootPath,
        IReadOnlyList<Uri> referencedSources)
    {
        string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRootPath));
        string resolvedSourceRoot = Path.TrimEndingDirectorySeparator(
            PathBoundary.ResolveDeepestExistingTarget(sourceRoot));
        string elementPath = Path.GetFullPath(elementUri.LocalPath);
        string resolvedElementPath = PathBoundary.ResolveDeepestExistingTarget(elementPath);
        if (!PathBoundary.IsPathInsideRoot(resolvedSourceRoot, resolvedElementPath))
        {
            return null;
        }

        string elementDirectory = Path.GetDirectoryName(elementPath)
                                  ?? throw new JsonException("Recovered element has no source directory.");
        // Save As must retain every serialized path, including distinct symlink aliases.
        // Do not fold case: differently cased sidecars may be distinct files.
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SuppressedReferencedStorageSource>();
        foreach (string sourcePath in EnumerateSerializedGraphObjects(element)
            .OfType<CoreObject>()
            .Select(static coreObject => coreObject.Uri)
            .Concat(referencedSources)
            .Where(static uri => uri is { IsFile: true })
            .Select(static uri => Path.GetFullPath(uri!.LocalPath)))
        {
            string resolvedSourcePath = PathBoundary.ResolveDeepestExistingTarget(sourcePath);
            if (string.Equals(
                    resolvedSourcePath,
                    resolvedElementPath,
                    StringComparison.Ordinal)
                || !PathBoundary.IsPathInsideRoot(resolvedSourceRoot, resolvedSourcePath)
                || !File.Exists(sourcePath)
                || !seenPaths.Add(sourcePath))
            {
                continue;
            }

            result.Add(new SuppressedReferencedStorageSource(
                File.ReadAllBytes(sourcePath),
                Path.GetRelativePath(sourceRoot, sourcePath),
                Path.GetRelativePath(elementDirectory, sourcePath)));
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    internal static SuppressedStorageSource? TryResumeElementPersistence(Element element)
    {
        if (element.SuppressedStorageSource is not { } source
            || EnumerateSerializedGraphFallbacks(element).Any()
            || HasUnresolvedSerializedRecoveryBlocker(element, source)
            || EnumerateSerializedGraphObjects(element).OfType<KeyFrame>().Any(static keyFrame => keyFrame.HasLossyEasing))
        {
            return null;
        }

        element.SuppressedStorageSource = null;
        return source;
    }

    private static bool HasUnresolvedSerializedRecoveryBlocker(
        Element element,
        SuppressedStorageSource source)
    {
        using var capture = new LossyEasingSerializationCapture();
        using var objects = new SerializedObjectCapture();
        CoreSerializer.SerializeToJsonObject(
            element,
            new CoreSerializerOptions
            {
                BaseUri = element.Uri,
                Mode = CoreSerializationMode.ReadWrite | CoreSerializationMode.EmbedReferencedObjects,
            });
        if (capture.HasLossyEasing || objects.HasFallback) return true;
        if (source.UntraversedFallbacks is not { Length: > 0 } snapshots) return false;
        // Keep the original representation for fallback snapshot matching; embedding adds URI metadata.
        JsonObject current = CoreSerializer.SerializeToJsonObject(
            element, new CoreSerializerOptions { BaseUri = element.Uri });
        return snapshots.Any(snapshot => ContainsEquivalentJsonNode(current, snapshot));
    }

    private static bool ContainsEquivalentJsonNode(JsonNode? current, JsonNode snapshot)
    {
        if (JsonNode.DeepEquals(current, snapshot))
        {
            return true;
        }

        return current switch
        {
            JsonObject obj => obj.Any(item =>
                item.Value != null && ContainsEquivalentJsonNode(item.Value, snapshot)),
            JsonArray array => array.Any(item =>
                item != null && ContainsEquivalentJsonNode(item, snapshot)),
            _ => false,
        };
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

        var rewriteState = new RecoveredReferenceRewriteState(referenceTargets);
        foreach (CoreObject coreObject in EnumerateSerializedGraphObjects(_scene).OfType<CoreObject>())
        {
            foreach (CoreProperty property in PropertyRegistry.GetRegistered(coreObject.GetType()))
            {
                if (!property.GetMetadata<CorePropertyMetadata>(coreObject.GetType()).ShouldSerialize)
                {
                    continue;
                }

                object? currentValue = coreObject.GetValue(property);
                object? migratedValue = MigrateRecoveredReferenceValue(currentValue, rewriteState);
                if (HasReferenceRewrite(currentValue, migratedValue)
                    && property is not IStaticProperty { CanWrite: false })
                {
                    coreObject.ReplaceValue(property, migratedValue);
                }
            }

            if (coreObject is not EngineObject engineObject)
            {
                continue;
            }

            foreach (IProperty property in engineObject.Properties)
            {
                object? currentValue = property.CurrentValue;
                object? migratedValue = MigrateRecoveredReferenceValue(currentValue, rewriteState);
                if (HasReferenceRewrite(currentValue, migratedValue))
                {
                    property.ReplaceCurrentValue(migratedValue);
                }

                if (property.Expression is IReferenceRewritable)
                {
                    property.Expression = (IExpression?)MigrateRecoveredReferenceValue(property.Expression, rewriteState);
                }
                else if (property.Expression is IReferenceExpression referenceExpression
                    && TryGetMigratedId(referenceExpression.ObjectId, out Guid migratedExpressionId)
                    && referenceExpression.Rebind(migratedExpressionId) is { } reboundExpression)
                {
                    property.Expression = (IExpression)reboundExpression;
                }

                if (property.Animation is IKeyFrameAnimation animation)
                {
                    foreach (IKeyFrame keyFrame in animation.KeyFrames)
                    {
                        object? keyFrameValue = keyFrame.Value;
                        object? migratedKeyFrameValue = MigrateRecoveredReferenceValue(
                            keyFrameValue,
                            rewriteState);
                        if (HasReferenceRewrite(keyFrameValue, migratedKeyFrameValue))
                        {
                            keyFrame.ReplaceValue(migratedKeyFrameValue);
                        }
                    }
                }
            }
        }
    }

    private bool TryGetMigratedId(Guid originalId, out Guid migratedId)
    {
        return _pendingRecoveredElementIdMigrations.TryGetValue(originalId, out migratedId)
               || _pendingRecoveredDescendantIdMigrations.TryGetValue(originalId, out migratedId);
    }

    private static object ResolveMigratedReference(
        IReference reference,
        Guid migratedId,
        RecoveredReferenceRewriteState state)
    {
        if (state.ReferenceTargets.TryGetValue(migratedId, out CoreObject? target)
            && reference.ObjectType.IsInstanceOfType(target))
        {
            return reference.Resolved(target);
        }

        return reference;
    }

    private object? MigrateRecoveredReferenceValue(
        object? value,
        RecoveredReferenceRewriteState state)
    {
        if (value is IReference reference)
        {
            object rewritten = TryGetMigratedId(reference.Id, out Guid migratedId)
                ? ResolveMigratedReference(reference, migratedId, state)
                : value;
            if (HasReferenceRewrite(value, rewritten))
            {
                state.RecordRewrite();
            }

            return rewritten;
        }

        if (value is IOptional { HasValue: true } optional)
        {
            object? item = optional.ToObject().Value;
            object? migratedItem = MigrateRecoveredReferenceValue(item, state);
            if (HasReferenceRewrite(item, migratedItem))
            {
                try
                {
                    ConstructorInfo? constructor = value.GetType().GetConstructor([optional.GetValueType()]);
                    return constructor?.Invoke([migratedItem]) ?? value;
                }
                catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex)
                                          && ex is TargetInvocationException
                                                   or ArgumentException
                                                   or MemberAccessException)
                {
                    return value;
                }
            }

            return value;
        }

        if (value is null or string)
        {
            return value;
        }

        bool trackReference = !value.GetType().IsValueType;
        if (trackReference)
        {
            if (state.Memo.TryGetValue(value, out RecoveredReferenceRewriteEntry? cached))
            {
                if (state.ActiveRewritables.TryPeek(out RecoveredReferenceRewriteEntry? parent))
                {
                    parent.Dependencies.Add(cached);
                }

                return cached.ShouldUseTargetDuringTraversal()
                    ? cached.Target
                    : cached.Source;
            }

            if (!state.Active.Add(value))
            {
                return value;
            }
        }

        try
        {
            object? rewrittenValue = value;
            if (value is IReferenceRewritable rewritable)
            {
                IReferenceRewritable target = rewritable.CreateReferenceRewriteTarget();
                if (target is not null && target.GetType() == value.GetType())
                {
                    var entry = new RecoveredReferenceRewriteEntry(value, target);
                    if (state.ActiveRewritables.TryPeek(out RecoveredReferenceRewriteEntry? parent))
                    {
                        parent.Dependencies.Add(entry);
                    }

                    state.Memo[value] = entry;
                    state.ActiveRewritables.Push(entry);
                    try
                    {
                        target.RewriteReferences(new RecoveredReferenceRewriteContext(this, state));
                    }
                    finally
                    {
                        state.ActiveRewritables.Pop();
                    }

                    entry.Complete = true;
                    if (entry.ShouldUseTargetDuringTraversal())
                    {
                        rewrittenValue = target;
                    }
                }
            }
            else if (value is IDictionary dictionary)
            {
                int rewriteCount = state.RewriteCount;
                var entries = new List<DictionaryEntry>();
                bool changed = false;
                foreach (object key in dictionary.Keys.Cast<object>().ToArray())
                {
                    object? item = dictionary[key];
                    object? migratedItem = MigrateRecoveredReferenceValue(item, state);
                    entries.Add(new DictionaryEntry(key, migratedItem));
                    changed |= HasReferenceRewrite(item, migratedItem);
                }

                if (changed)
                {
                    if (!dictionary.IsReadOnly)
                    {
                        foreach (DictionaryEntry entry in entries) dictionary[entry.Key] = entry.Value;
                    }
                    else if (RecoveredCollectionFactory.RebuildDictionary(dictionary, entries.ToArray()) is { } rebuilt)
                        rewrittenValue = rebuilt;
                    else
                        state.RewriteCount = rewriteCount;
                }
            }
            else if (value is IList list)
            {
                int rewriteCount = state.RewriteCount;
                object?[] rewrittenItems = new object?[list.Count];
                bool hasRewrittenItem = false;
                for (int i = 0; i < list.Count; i++)
                {
                    object? item = list[i];
                    object? migratedItem = MigrateRecoveredReferenceValue(item, state);
                    rewrittenItems[i] = migratedItem;
                    hasRewrittenItem |= HasReferenceRewrite(item, migratedItem);
                }

                if (hasRewrittenItem)
                {
                    if (!list.IsReadOnly)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            list[i] = rewrittenItems[i];
                        }
                    }
                    else if ((RecoveredCollectionFactory.RebuildEnumerable(list, rewrittenItems)
                              ?? TryRebuildReadOnlyList(list, rewrittenItems)) is { } rebuilt)
                    {
                        rewrittenValue = rebuilt;
                    }
                    else
                    {
                        state.RewriteCount = rewriteCount;
                    }
                }
            }

            else if (value is IEnumerable enumerable)
            {
                int rewriteCount = state.RewriteCount;
                object?[] items = enumerable.Cast<object?>().ToArray();
                object?[] rewrittenItems = items.Select(item => MigrateRecoveredReferenceValue(item, state)).ToArray();
                if (items.Where((item, index) => HasReferenceRewrite(item, rewrittenItems[index])).Any())
                {
                    if (RecoveredCollectionFactory.RebuildEnumerable(enumerable, rewrittenItems) is { } rebuilt)
                        rewrittenValue = rebuilt;
                    else
                        state.RewriteCount = rewriteCount;
                }
            }

            if (trackReference)
            {
                if (!state.Memo.ContainsKey(value))
                {
                    state.Memo[value] = new RecoveredReferenceRewriteEntry(value, rewrittenValue)
                    {
                        Complete = true,
                        DirectChanged = HasReferenceRewrite(value, rewrittenValue),
                    };
                }
            }

            return rewrittenValue;
        }
        finally
        {
            if (trackReference)
            {
                state.Active.Remove(value);
            }
        }
    }

    private static object? TryRebuildReadOnlyList(IList source, object?[] items)
    {
        Type sourceType = source.GetType();
        Type? elementType = sourceType.GetInterfaces()
            .Where(static type => type.IsGenericType
                                  && type.GetGenericTypeDefinition() == typeof(IList<>))
            .Select(static type => type.GetGenericArguments()[0])
            .FirstOrDefault();
        if (elementType is null)
        {
            return null;
        }

        Array array = Array.CreateInstance(elementType, items.Length);
        try
        {
            for (int i = 0; i < items.Length; i++)
            {
                array.SetValue(items[i], i);
            }

            foreach (ConstructorInfo constructor in sourceType.GetConstructors())
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(array))
                {
                    return constructor.Invoke([array]);
                }
            }
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex)
                                  && ex is ArgumentException
                                       or TargetInvocationException
                                       or MemberAccessException)
        {
        }

        return null;
    }

    private static bool HasReferenceRewrite(object? current, object? rewritten)
    {
        if (ReferenceEquals(current, rewritten))
        {
            return false;
        }

        if (current is null || rewritten is null)
        {
            return true;
        }

        return current.GetType().IsValueType
            ? !Equals(current, rewritten)
            : true;
    }

    private sealed class RecoveredReferenceRewriteContext(
        SceneRecovery scene,
        RecoveredReferenceRewriteState state) : IReferenceRewriteContext
    {
        public T Rewrite<T>(T value)
        {
            object? rewritten = scene.MigrateRecoveredReferenceValue(value, state);
            return rewritten is T typed ? typed : value;
        }
    }

    private sealed class RecoveredReferenceRewriteState(
        IReadOnlyDictionary<Guid, CoreObject> referenceTargets)
    {
        public IReadOnlyDictionary<Guid, CoreObject> ReferenceTargets { get; } = referenceTargets;

        public HashSet<object> Active { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<object, RecoveredReferenceRewriteEntry> Memo { get; }
            = new(ReferenceEqualityComparer.Instance);

        public Stack<RecoveredReferenceRewriteEntry> ActiveRewritables { get; } = new();

        public int RewriteCount { get; set; }

        public void RecordRewrite()
        {
            RewriteCount++;
            if (ActiveRewritables.TryPeek(out RecoveredReferenceRewriteEntry? entry))
            {
                entry.DirectChanged = true;
            }
        }
    }

    private sealed class RecoveredReferenceRewriteEntry(object source, object? target)
    {
        public object Source { get; } = source;

        public object? Target { get; } = target;

        public HashSet<RecoveredReferenceRewriteEntry> Dependencies { get; }
            = new(ReferenceEqualityComparer.Instance);

        public bool DirectChanged { get; set; }

        public bool Complete { get; set; }

        public bool ShouldUseTargetDuringTraversal()
        {
            return !Complete
                   || Dependencies.Any(static dependency => !dependency.Complete)
                   || IsChanged(new HashSet<RecoveredReferenceRewriteEntry>(ReferenceEqualityComparer.Instance));
        }

        private bool IsChanged(ISet<RecoveredReferenceRewriteEntry> visited)
        {
            if (DirectChanged)
            {
                return true;
            }

            return visited.Add(this)
                   && Dependencies.Any(dependency => dependency.IsChanged(visited));
        }
    }

    private static IEnumerable<object> EnumerateSerializedGraphObjects(object root)
        => SerializedGraphTraversal.Enumerate(root);

    private static IEnumerable<(CoreObject Object, SerializedGraphPath Path)> EnumerateSerializedGraphDescendantPaths(
        Element element)
    {
        var objects = new List<(CoreObject Object, SerializedGraphPath Path)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectSerializedGraphObjectPaths(element, new SerializedGraphPath("$", "$"), visited, objects);
        return objects.Where(item => !ReferenceEquals(item.Object, element));
    }

    private static void CollectSerializedGraphObjectPaths(
        object? value,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        if (value is null or string
            || (!value.GetType().IsValueType && !visited.Add(value)))
        {
            return;
        }

        if (value is IOptional optional)
        {
            if (optional.HasValue)
            {
                CollectSerializedGraphObjectPaths(optional.ToObject().Value, path, visited, objects);
            }

            return;
        }

        if (value is CoreObject coreObject)
        {
            objects.Add((coreObject, path));
        }

        if (value is Element element)
        {
            CollectSerializedGraphPathItems(
                element.Objects,
                AppendSerializedGraphPath(path, "property", nameof(Element.Objects)),
                visited,
                objects);
        }

        if (value is EngineObject engineObject)
        {
            foreach (IProperty property in engineObject.Properties)
            {
                SerializedGraphPath propertyPath = AppendSerializedGraphPath(path, "property", property.Name);
                CollectSerializedGraphObjectPaths(property.CurrentValue, propertyPath, visited, objects);
                if (property.Animation is IKeyFrameAnimation animation)
                {
                    SerializedGraphPath keyFramesPath = AppendSerializedGraphPath(
                        path,
                        "animation",
                        property.Name);
                    var occurrences = new Dictionary<Guid, int>();
                    int index = 0;
                    foreach (IKeyFrame keyFrame in animation.KeyFrames)
                    {
                        SerializedGraphPath keyFramePath = CreateSerializedGraphCollectionItemPath(
                            keyFramesPath,
                            keyFrame,
                            index++,
                            occurrences);
                        CollectSerializedGraphObjectPaths(keyFrame, keyFramePath, visited, objects);
                        CollectSerializedGraphObjectPaths(
                            keyFrame.Value,
                            AppendSerializedGraphPath(keyFramePath, "property", nameof(IKeyFrame.Value)),
                            visited,
                            objects);
                    }
                }
            }
        }

        if (value is IHierarchical hierarchical)
        {
            CollectSerializedGraphPathItems(
                hierarchical.HierarchicalChildren,
                AppendSerializedGraphPath(path, "collection", "HierarchicalChildren"),
                visited,
                objects);
        }

        if (value is CoreObject registeredObject)
        {
            foreach (CoreProperty property in PropertyRegistry.GetRegistered(registeredObject.GetType()))
            {
                if (!property.GetMetadata<CorePropertyMetadata>(registeredObject.GetType()).ShouldSerialize)
                {
                    continue;
                }

                CollectSerializedGraphObjectPaths(
                    registeredObject.GetValue(property),
                    AppendSerializedGraphPath(path, "property", property.Name),
                    visited,
                    objects);
            }
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                CollectSerializedGraphObjectPaths(
                    entry.Value,
                    AppendSerializedGraphPath(path, "key", entry.Key?.ToString() ?? "null"),
                    visited,
                    objects);
            }
        }
        else if (value is IEnumerable enumerable)
        {
            CollectSerializedGraphPathItems(enumerable, path, visited, objects);
        }
    }

    private static void CollectSerializedGraphPathItems(
        IEnumerable items,
        SerializedGraphPath path,
        ISet<object> visited,
        ICollection<(CoreObject Object, SerializedGraphPath Path)> objects)
    {
        var occurrences = new Dictionary<Guid, int>();
        int index = 0;
        foreach (object? item in items)
        {
            SerializedGraphPath itemPath = CreateSerializedGraphCollectionItemPath(
                path,
                item,
                index++,
                occurrences);
            CollectSerializedGraphObjectPaths(item, itemPath, visited, objects);
        }
    }

    private static SerializedGraphPath CreateSerializedGraphCollectionItemPath(
        SerializedGraphPath path,
        object? item,
        int index,
        IDictionary<Guid, int> occurrences)
    {
        string indexText = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string positional = AppendSerializedGraphPath(path.Positional, "index", indexText);
        if (item is CoreObject { Id: var id } && id != Guid.Empty)
        {
            int occurrence = occurrences.TryGetValue(id, out int value) ? value : 0;
            occurrences[id] = occurrence + 1;
            string stable = AppendSerializedGraphPath(path.Stable, "id", $"{id:D}#{occurrence}");
            return new SerializedGraphPath(stable, positional);
        }

        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, "index", indexText),
            positional);
    }

    private static SerializedGraphPath AppendSerializedGraphPath(
        SerializedGraphPath path,
        string kind,
        string value)
    {
        return new SerializedGraphPath(
            AppendSerializedGraphPath(path.Stable, kind, value),
            AppendSerializedGraphPath(path.Positional, kind, value));
    }

    private static bool TryGetRecoveredDescendantPositionalIdentity(
        IReadOnlyDictionary<string, Guid> identities,
        string relativePath,
        string positionalPath,
        out Guid identityId,
        out bool ambiguous)
    {
        string keyPrefix = $"{relativePath}!path:";
        string normalizedPath = NormalizeSerializedGraphPositionalPath(positionalPath);
        var candidates = new HashSet<Guid>();
        foreach ((string key, Guid id) in identities)
        {
            if (key.StartsWith(keyPrefix, StringComparison.Ordinal)
                && NormalizeSerializedGraphPositionalPath(key[keyPrefix.Length..]) == normalizedPath)
            {
                candidates.Add(id);
            }
        }

        ambiguous = candidates.Count > 1;
        if (candidates.Count == 1)
        {
            identityId = candidates.Single();
            return true;
        }

        identityId = Guid.Empty;
        return false;
    }

    private static string NormalizeSerializedGraphPositionalPath(string path)
    {
        string[] segments = path.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].StartsWith("index:", StringComparison.Ordinal))
            {
                segments[i] = "index:*";
            }
        }

        return string.Join('/', segments);
    }

    private static string AppendSerializedGraphPath(string path, string kind, string value)
    {
        string escaped = value.Replace("~", "~0").Replace("/", "~1");
        return $"{path}/{kind}:{escaped}";
    }

    private readonly record struct SerializedGraphPath(string Stable, string Positional);

    private static void EnsureFallbackProjection(IFallback fallback)
    {
        if (fallback is not CoreObject coreObject)
        {
            return;
        }

        JsonObject json = fallback.Json ?? new JsonObject();
        if (!json.ContainsKey("$type") && !json.ContainsKey("@type"))
        {
            json.WriteDiscriminator(coreObject.GetType());
        }

        json[nameof(CoreObject.Id)] = coreObject.Id.ToString();
        fallback.Json = json;
    }

    private static JsonObject CreateFallbackProjection(FallbackEngineObject fallback, string? typeName = null)
    {
        var json = new JsonObject
        {
            [nameof(CoreObject.Id)] = fallback.Id.ToString(),
            [nameof(CoreObject.Name)] = fallback.Name,
        };
        if (typeName is not null)
        {
            json["$type"] = typeName;
        }
        else
        {
            json.WriteDiscriminator(typeof(FallbackEngineObject));
        }

        return json;
    }

    private static JsonObject? TryParseTopLevelObject(string rawText)
    {
        try
        {
            return JsonNode.Parse(rawText) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DecodeRecoveryMetadata(byte[] rawBytes)
    {
        ReadOnlySpan<byte> bytes = rawBytes;
        if (bytes.Length >= 4
            && bytes[0] == 0xff && bytes[1] == 0xfe
            && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return Encoding.UTF32.GetString(bytes[4..]);
        }

        if (bytes.Length >= 4
            && bytes[0] == 0x00 && bytes[1] == 0x00
            && bytes[2] == 0xfe && bytes[3] == 0xff)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? TryGetTopLevelTypeName(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root)
    {
        if (root?.TryGetDiscriminator(out string? parsedTypeName) == true)
        {
            return parsedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "$type", out string? scannedTypeName))
        {
            return scannedTypeName;
        }

        if (TryGetTopLevelStringProperty(rawBytes, "@type", out string? scannedLegacyTypeName))
        {
            return scannedLegacyTypeName;
        }

        Match? match = FindTopLevelMatch(rawText, s_typePattern.Matches(rawText))
                       ?? FindTopLevelMatch(rawText, s_legacyTypePattern.Matches(rawText));
        if (match is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string>(match.Groups["type"].Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Guid ResolveRecoveredElementId(
        ReadOnlySpan<byte> rawBytes,
        string rawText,
        JsonObject? root,
        Uri uri)
    {
        if (TryGetSerializedId(root, out Guid parsedId))
        {
            return parsedId;
        }

        if (TryGetTopLevelStringProperty(rawBytes, nameof(CoreObject.Id), out string? scannedId)
            && Guid.TryParse(scannedId, out Guid scannedGuid)
            && scannedGuid != Guid.Empty)
        {
            return scannedGuid;
        }

        // Only a top-level Id may name the element: a nested object's or quoted Id would collide
        // with live objects, so anything else falls through to the deterministic filename Guid.
        MatchCollection matches = s_idPattern.Matches(rawText);
        Match? topLevelMatch = FindTopLevelMatch(rawText, matches);
        if (topLevelMatch != null
            && Guid.TryParse(topLevelMatch.Groups["id"].Value, out Guid topLevelId)
            && topLevelId != Guid.Empty)
        {
            return topLevelId;
        }

        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(sceneDirectory, uri.LocalPath));
        return CreateVersion5Guid(s_recoveredElementNamespace, relativePath);
    }

    private static bool TryGetTopLevelStringProperty(
        ReadOnlySpan<byte> rawBytes,
        string propertyName,
        out string? value)
    {
        value = null;
        if (rawBytes.Length >= 3
            && rawBytes[0] == 0xef
            && rawBytes[1] == 0xbb
            && rawBytes[2] == 0xbf)
        {
            rawBytes = rawBytes[3..];
        }

        var reader = new Utf8JsonReader(rawBytes, isFinalBlock: false, state: default);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            int propertyDepth = reader.CurrentDepth + 1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == propertyDepth
                    && reader.ValueTextEquals(propertyName))
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        value = reader.GetString();
                        return value is not null;
                    }

                    return false;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

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
            string relativePath = NormalizeRelativePath(
                Path.GetRelativePath(sceneDirectory, child.Uri!.LocalPath));
            elementIds[relativePath] = child.Id;
            foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                     EnumerateSerializedGraphDescendantPaths(child))
            {
                if (descendant is IFallback)
                {
                    string identityKey = CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
                    descendantIdentities[identityKey] = descendant.Id;
                    if (graphPath.Positional != graphPath.Stable)
                    {
                        string positionalIdentityKey = CreateRecoveredDescendantIdentityKey(
                            relativePath,
                            graphPath.Positional);
                        descendantIdentities[positionalIdentityKey] = descendant.Id;
                    }
                }

                if (_recoveredDescendantRemaps.TryGetValue(descendant, out RecoveredDescendantRemap? remap)
                    && descendant.Id == remap.AssignedId)
                {
                    string remapKey = CreateRecoveredDescendantKey(
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

    private static string CreateRecoveredDescendantKey(string relativePath, Guid originalId, int occurrence)
    {
        return $"{relativePath}!{originalId:D}#{occurrence}";
    }

    private static string CreateRecoveredDescendantIdentityKey(string relativePath, string graphPath)
    {
        return $"{relativePath}!path:{graphPath}";
    }

    private static string CreateLegacyRecoveredDescendantIdentityKey(string relativePath, int index)
    {
        return $"{relativePath}!@{index}";
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static Match? FindTopLevelMatch(string rawText, MatchCollection matches)
    {
        int rootStart = 0;
        while (rootStart < rawText.Length
               && (char.IsWhiteSpace(rawText[rootStart]) || rawText[rootStart] == '\uFEFF'))
        {
            rootStart++;
        }

        if (rootStart >= rawText.Length || rawText[rootStart] != '{')
        {
            return null;
        }

        int matchIndex = 0;
        int objectDepth = 0;
        int arrayDepth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = rootStart; i < rawText.Length && matchIndex < matches.Count; i++)
        {
            Match match = matches[matchIndex];
            if (i == match.Index)
            {
                if (!inString && objectDepth == 1 && arrayDepth == 0)
                {
                    return match;
                }

                matchIndex++;
            }

            char current = rawText[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }
            }
            else if (current == '"')
            {
                inString = true;
            }
            else if (current == '{')
            {
                objectDepth++;
            }
            else if (current == '}' && objectDepth > 0)
            {
                objectDepth--;
                if (objectDepth == 0)
                {
                    return null;
                }
            }
            else if (current == '[')
            {
                arrayDepth++;
            }
            else if (current == ']' && arrayDepth > 0)
            {
                arrayDepth--;
            }
        }

        return null;
    }

    private static Guid CreateVersion5Guid(Guid namespaceId, string name)
    {
        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] source = new byte[namespaceBytes.Length + nameBytes.Length];
        namespaceBytes.CopyTo(source, 0);
        nameBytes.CopyTo(source, namespaceBytes.Length);

        byte[] hash = SHA1.HashData(source);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        Array.Resize(ref hash, 16);
        SwapGuidByteOrder(hash);
        return new Guid(hash);
    }

    private static void SwapGuidByteOrder(Span<byte> bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }


}
