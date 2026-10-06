using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal sealed partial class SceneRecovery
{
    private void ReassignDuplicateRecoveredIds()
    {
        string sceneDirectory = Path.GetDirectoryName(Uri!.LocalPath)!;
        (Element Child, string RelativePath)[] recoveredChildren = GetChildPaths(sceneDirectory, recovered: true);

        var claimedIds = new HashSet<Guid> { Guid.Empty, Id };
        claimedIds.UnionWith(EnumerateSceneOwnedObjects().Select(static obj => obj.Id));

        var claims = new RecoveredIdClaims(
            claimedIds,
            new Dictionary<string, Guid>(_recoveredDescendantIds, StringComparer.Ordinal),
            new Dictionary<string, Guid>(_recoveredDescendantIdentities, StringComparer.Ordinal));
        (Element Child, string RelativePath)[] healthyChildren = GetChildPaths(sceneDirectory, recovered: false);

        ClaimHealthyChildren(healthyChildren, claims);
        MigrateHealthyPlaceholderIds(healthyChildren);
        PruneStaleRecoveredElementIds(recoveredChildren);
        AssignRecoveredElementIds(recoveredChildren, claims.ClaimedIds);
        AssignRecoveredDescendantIds(recoveredChildren, claims);
        DropMigrationsOfRetainedIds();
    }

    private (Element Child, string RelativePath)[] GetChildPaths(string sceneDirectory, bool recovered)
    {
        return Children
            .Where(child => (child.SuppressedStorageSource is not null) == recovered)
            .Select(child => (
                Child: child,
                RelativePath: GetSceneRelativePath(sceneDirectory, child.Uri!)))
            .OrderBy(static item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private void ClaimHealthyChildren(
        (Element Child, string RelativePath)[] healthyChildren,
        RecoveredIdClaims claims)
    {
        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => !_recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            claims.ClaimedIds.Add(child.Id);
            ClaimHealthyDescendants(child, claims);
        }

        foreach ((Element child, string relativePath) in healthyChildren
                     .Where(item => _recoveredElementIds.ContainsKey(item.RelativePath)))
        {
            if (!claims.ClaimedIds.Add(child.Id))
            {
                Guid placeholderId = _recoveredElementIds[relativePath];
                child.Id = claims.ClaimedIds.Add(placeholderId)
                    ? placeholderId
                    : RecoveredIdentity.ClaimRecoveredElementId(relativePath, claims.ClaimedIds);
            }

            ClaimPreviouslyRecoveredHealthyDescendants(child, relativePath, claims);
        }
    }

    private static void ClaimHealthyDescendants(Element child, RecoveredIdClaims claims)
    {
        foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
        {
            claims.SeenDescendants.Add(descendant);
            claims.ClaimedIds.Add(descendant.Id);
        }
    }

    private void ClaimPreviouslyRecoveredHealthyDescendants(
        Element child,
        string relativePath,
        RecoveredIdClaims claims)
    {
        var occurrences = new Dictionary<Guid, int>();
        var legacyIndices = new Dictionary<CoreObject, int>(ReferenceEqualityComparer.Instance);
        int legacyIndex = 0;
        foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
        {
            legacyIndices.TryAdd(descendant, legacyIndex++);
        }

        foreach ((CoreObject descendant, SerializedGraphPath graphPath) in
                 SerializedGraphPaths.EnumerateSerializedGraphDescendantPaths(child))
        {
            if (!claims.SeenDescendants.Add(descendant))
            {
                continue;
            }

            Guid originalId = descendant.Id;
            int occurrence = occurrences.GetValueOrDefault(originalId);
            occurrences[originalId] = occurrence + 1;
            string remapKey = RecoveredIdentity.CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
            bool hasPreviousAssignedId = claims.TryFindPreviouslyAssignedDescendantId(
                descendant,
                graphPath,
                relativePath,
                remapKey,
                legacyIndices,
                out Guid previousAssignedId);

            if (claims.ClaimedIds.Add(originalId))
            {
                if (hasPreviousAssignedId)
                {
                    _pendingRecoveredDescendantIdMigrations.TryAdd(previousAssignedId, originalId);
                }

                continue;
            }

            Guid assignedId = hasPreviousAssignedId && claims.ClaimedIds.Add(previousAssignedId)
                ? previousAssignedId
                : RecoveredIdentity.ClaimRecoveredDescendantId(relativePath, remapKey, claims.ClaimedIds);
            descendant.Id = assignedId;
            _pendingRecoveredDescendantIdMigrations.TryAdd(
                hasPreviousAssignedId ? previousAssignedId : assignedId,
                assignedId);
        }
    }

    private void MigrateHealthyPlaceholderIds((Element Child, string RelativePath)[] healthyChildren)
    {
        foreach ((Element child, string relativePath) in healthyChildren)
        {
            if (_recoveredElementIds.Remove(relativePath, out Guid placeholderId))
            {
                _pendingRecoveredElementIdMigrations.TryAdd(placeholderId, child.Id);
            }
        }
    }

    private void PruneStaleRecoveredElementIds((Element Child, string RelativePath)[] recoveredChildren)
    {
        var recoveredPaths = recoveredChildren
            .Select(static item => item.RelativePath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string path in _recoveredElementIds.Keys.Where(path => !recoveredPaths.Contains(path)).ToArray())
        {
            _recoveredElementIds.Remove(path);
        }
    }

    private void AssignRecoveredElementIds(
        (Element Child, string RelativePath)[] recoveredChildren,
        HashSet<Guid> claimedIds)
    {
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

            child.Id = RecoveredIdentity.ClaimRecoveredElementId(relativePath, claimedIds);
        }

        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            _recoveredElementIds[relativePath] = child.Id;
        }
    }

    private void AssignRecoveredDescendantIds(
        (Element Child, string RelativePath)[] recoveredChildren,
        RecoveredIdClaims claims)
    {
        _recoveredDescendantIds.Clear();
        _recoveredDescendantRemaps.Clear();
        var pendingDescendantRemaps
            = new Dictionary<CoreObject, (string RemapKey, Guid OriginalId, int Occurrence)>(
                ReferenceEqualityComparer.Instance);
        foreach ((Element child, string relativePath) in recoveredChildren)
        {
            ReclaimPersistedDescendantIds(child, relativePath, claims, pendingDescendantRemaps);
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

                Guid candidate = RecoveredIdentity.ClaimRecoveredDescendantId(
                    relativePath,
                    remap.RemapKey,
                    claims.ClaimedIds);
                descendant.Id = candidate;
                RecordRecoveredDescendantRemap(
                    descendant,
                    remap.RemapKey,
                    remap.OriginalId,
                    candidate,
                    remap.Occurrence);

            }
        }
    }

    private void ReclaimPersistedDescendantIds(
        Element child,
        string relativePath,
        RecoveredIdClaims claims,
        Dictionary<CoreObject, (string RemapKey, Guid OriginalId, int Occurrence)> pendingDescendantRemaps)
    {
        var occurrences = new Dictionary<Guid, int>();
        int idlessOccurrence = 0;
        foreach (CoreObject descendant in EnumerateSerializedGraphDescendants(child))
        {
            if (!claims.SeenDescendants.Add(descendant))
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

            string remapKey = RecoveredIdentity.CreateRecoveredDescendantKey(relativePath, originalId, occurrence);
            if (claims.PersistedDescendantIds.TryGetValue(remapKey, out Guid persistedId))
            {
                if (claims.ClaimedIds.Add(persistedId))
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
            else if (!claims.ClaimedIds.Add(originalId))
            {
                pendingDescendantRemaps[descendant] = (remapKey, originalId, occurrence);
            }
        }
    }

    private void DropMigrationsOfRetainedIds()
    {
        // A global OriginalId -> AssignedId migration would redirect every reference that still
        // targets a surviving object. Only migrate when the original ID was abandoned entirely;
        // otherwise the references keep pointing at the object that retained it.
        var retainedIds = new HashSet<Guid> { Guid.Empty, Id };
        foreach (CoreObject graphObject in EnumerateSerializedGraphObjects(Children).OfType<CoreObject>())
        {
            retainedIds.Add(graphObject.Id);
        }

        retainedIds.UnionWith(EnumerateSceneOwnedObjects().Select(static obj => obj.Id));

        RemoveMigrationsOfRetainedIds(_pendingRecoveredElementIdMigrations, retainedIds);
        RemoveMigrationsOfRetainedIds(_pendingRecoveredDescendantIdMigrations, retainedIds);
    }

    private static void RemoveMigrationsOfRetainedIds(Dictionary<Guid, Guid> migrations, HashSet<Guid> retainedIds)
    {
        foreach (Guid originalId in migrations.Keys.ToArray())
        {
            if (migrations[originalId] != originalId
                && retainedIds.Contains(originalId))
            {
                migrations.Remove(originalId);
            }
        }
    }

    // The Ids taken during one reassignment pass, the descendants that already have one, and the remaps the
    // previous pass persisted.
    private sealed class RecoveredIdClaims(
        HashSet<Guid> claimedIds,
        Dictionary<string, Guid> persistedDescendantIds,
        Dictionary<string, Guid> persistedDescendantIdentities)
    {
        public HashSet<Guid> ClaimedIds { get; } = claimedIds;

        public HashSet<CoreObject> SeenDescendants { get; } = new(ReferenceEqualityComparer.Instance);

        public Dictionary<string, Guid> PersistedDescendantIds { get; } = persistedDescendantIds;

        public Dictionary<string, Guid> PersistedDescendantIdentities { get; } = persistedDescendantIdentities;

        public bool TryFindPreviouslyAssignedDescendantId(
            CoreObject descendant,
            SerializedGraphPath graphPath,
            string relativePath,
            string remapKey,
            Dictionary<CoreObject, int> legacyIndices,
            out Guid previousAssignedId)
        {
            string identityKey = RecoveredIdentity.CreateRecoveredDescendantIdentityKey(relativePath, graphPath.Stable);
            bool hasPersistedId = PersistedDescendantIds.TryGetValue(remapKey, out Guid persistedId);
            bool hasPersistedIdentity = PersistedDescendantIdentities.TryGetValue(
                identityKey,
                out Guid persistedIdentityId);
            bool ambiguousPositionalIdentity = false;
            if (!hasPersistedIdentity && graphPath.Positional != graphPath.Stable)
            {
                hasPersistedIdentity = RecoveredIdentity.TryGetRecoveredDescendantPositionalIdentity(
                    PersistedDescendantIdentities,
                    relativePath,
                    graphPath.Positional,
                    out persistedIdentityId,
                    out ambiguousPositionalIdentity);
            }

            if (!hasPersistedIdentity
                && !ambiguousPositionalIdentity
                && legacyIndices.TryGetValue(descendant, out int persistedIndex))
            {
                string legacyIdentityKey = RecoveredIdentity.CreateLegacyRecoveredDescendantIdentityKey(
                    relativePath,
                    persistedIndex);
                hasPersistedIdentity = PersistedDescendantIdentities.TryGetValue(
                    legacyIdentityKey,
                    out persistedIdentityId);
            }

            previousAssignedId = hasPersistedId ? persistedId : persistedIdentityId;
            return hasPersistedId || hasPersistedIdentity;
        }
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
}
