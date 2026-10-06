using System.Text.Json;

using Microsoft.Extensions.Logging;

using ProxyKey = (Beutl.Media.Proxy.ProxyFingerprint Source, Beutl.Media.Proxy.ProxyPreset Preset);

namespace Beutl.Media.Proxy;

public sealed partial class ProxyStore
{
    public Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteAgedGeneratedTempFiles(cancellationToken);

            List<ProxyEntry> sidecarCandidates = CollectSidecarCandidates(cancellationToken);

            HashSet<ProxyKey> changedKeys = [];
            HashSet<ProxyKey> adoptedKeys;
            List<ProxyEntry> snapshot;
            lock (_lock)
            {
                adoptedKeys = MergeSidecarCandidates(sidecarCandidates);
                changedKeys.UnionWith(adoptedKeys);
                snapshot = [.. _entries.Values];
            }

            // Stat each tracked entry outside _lock: File.Exists / FileInfo.Length / FromFile
            // (symlink resolve) over the whole store would otherwise block the preview hot path
            // (TryGet/Touch/Enumerate) behind startup reconciliation. The mutations below re-acquire
            // _lock and re-validate each key against the current entry before acting.
            ClassifyTrackedEntries(snapshot, cancellationToken, out List<ProxyEntry> missing, out List<ProxyEntry> changed);

            List<ProxyEntry> removedEntries = [];
            List<ProxyEntry> changedEntries = [];
            HashSet<string> trackedProxyPaths = ApplyReconcileDecisions(
                missing,
                changed,
                changedKeys,
                removedEntries,
                changedEntries);

            // Scan and delete orphaned proxy files outside the store lock: this walks the whole store
            // root and stats/deletes files, which would otherwise block UI paths (TryGet/Touch/
            // Enumerate) behind startup reconciliation. A just-generated proxy is younger than the
            // age threshold, so the snapshot going slightly stale cannot reclaim a live file.
            ReclaimRetiredProxyFiles();
            ReclaimOrphanProxyFiles(trackedProxyPaths, cancellationToken);

            RaiseReconcileNotifications(removedEntries, changedEntries, adoptedKeys);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Reconcile is best-effort; serving known-good entries is safer than failing startup.
            s_logger.LogWarning(
                ex,
                "Proxy store reconciliation failed; stale entries and orphan files may remain until the next reconcile.");
        }

        return Task.CompletedTask;
    }

    private void DeleteAgedGeneratedTempFiles(CancellationToken cancellationToken)
    {
        foreach (string tmp in Directory.EnumerateFiles(StoreRootPath, "*", SearchOption.AllDirectories)
                     .Where(path => ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, path)
                         || ProxyPathUtilities.IsGeneratedProxyBackupPath(StoreRootPath, path))
                     .Where(IsOldEnoughToCleanGeneratedTemp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(tmp);
        }
    }

    private void ClassifyTrackedEntries(
        List<ProxyEntry> snapshot,
        CancellationToken cancellationToken,
        out List<ProxyEntry> missing,
        out List<ProxyEntry> changed)
    {
        missing = [];
        changed = [];
        foreach (ProxyEntry entry in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path;
            try
            {
                path = GetAbsolutePath(entry);
            }
            catch
            {
                missing.Add(entry);
                continue;
            }

            if (entry.State == ProxyState.Failed)
                continue;

            if (!File.Exists(path))
            {
                missing.Add(entry);
                continue;
            }

            if (entry.State is ProxyState.Ready or ProxyState.Stale
                && !HasValidReadyFile(entry, path))
            {
                missing.Add(entry);
                continue;
            }

            if (entry.State == ProxyState.Ready
                && File.Exists(entry.Source.SourcePath)
                && ProxyFingerprint.FromFile(entry.Source.SourcePath) != entry.Source)
            {
                changed.Add(entry);
            }
        }
    }

    private HashSet<string> ApplyReconcileDecisions(
        List<ProxyEntry> missing,
        List<ProxyEntry> changed,
        HashSet<ProxyKey> changedKeys,
        List<ProxyEntry> removedEntries,
        List<ProxyEntry> changedEntries)
    {
        lock (_lock)
        {
            HashSet<ProxyKey> removedKeys = [];
            foreach (ProxyEntry entry in missing)
            {
                var key = (entry.Source, entry.Preset);
                // Act only on entries unchanged since the unlocked scan; a concurrent
                // Register/TryTransition/Touch supersedes our now-stale decision.
                if (_entries.TryGetValue(key, out ProxyEntry? current) && current == entry)
                {
                    _entries.Remove(key);
                    removedKeys.Add(key);
                    removedEntries.Add(entry);
                }
            }

            foreach (ProxyEntry entry in changed)
            {
                var key = (entry.Source, entry.Preset);
                if (_entries.TryGetValue(key, out ProxyEntry? current) && current == entry)
                {
                    _entries[key] = entry with
                    {
                        State = ProxyState.Stale,
                        LastUsedUtc = DateTime.UtcNow,
                    };
                    changedKeys.Add(key);
                    changedEntries.Add(entry);
                }
            }

            if (removedKeys.Count > 0 || changedKeys.Count > 0)
            {
                FlushCore(changedKeys, removedKeys);
            }

            return CollectTrackedProxyPaths();
        }
    }

    private void RaiseReconcileNotifications(
        List<ProxyEntry> removedEntries,
        List<ProxyEntry> changedEntries,
        HashSet<ProxyKey> adoptedKeys)
    {
        foreach (ProxyEntry entry in removedEntries)
        {
            OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.Deleted);
        }

        foreach (ProxyEntry entry in changedEntries)
        {
            OnChanged(entry.Source, entry.Preset, ProxyStoreChangeKind.StateChanged);
        }

        // Notify for sidecars adopted after services were exposed, so a tab/preview that already
        // saw the missing entry reloads the recovered proxy. Skip keys the stat pass then removed or
        // marked stale — those already fired their own notification.
        var supersededKeys = new HashSet<ProxyKey>();
        foreach (ProxyEntry entry in removedEntries)
            supersededKeys.Add((entry.Source, entry.Preset));
        foreach (ProxyEntry entry in changedEntries)
            supersededKeys.Add((entry.Source, entry.Preset));

        foreach ((ProxyFingerprint source, ProxyPreset preset) in adoptedKeys)
        {
            if (!supersededKeys.Contains((source, preset)))
                OnChanged(source, preset, ProxyStoreChangeKind.Registered);
        }
    }

    private void LoadIndex()
    {
        if (!File.Exists(_indexPath))
            return;

        try
        {
            string json = File.ReadAllText(_indexPath);
            ProxyStoreIndex? index = JsonSerializer.Deserialize<ProxyStoreIndex>(json, s_jsonOptions);
            if (index?.Version != ProxyStoreIndex.CurrentVersion)
            {
                RebuildFromSidecars();
                return;
            }

            foreach (ProxyEntry entry in index.Entries)
            {
                if (TryValidateEntry(entry))
                    _entries[(entry.Source, entry.Preset)] = entry;
            }
        }
        catch
        {
            // FlushCore refuses to overwrite an unreadable index (it cannot distinguish corruption from
            // a concurrent writer), so the rebuild must discard the corrupt file first or the snapshot
            // never reaches disk and every later flush keeps degrading. Safe here: construction is
            // single-threaded and the store is not yet serving.
            RebuildFromSidecars(discardCorruptIndex: true);
        }
    }

    // Rebuild the in-memory index from meta.json sidecars when index.json is missing/invalid. Guarded
    // so a filesystem fault (permissions, I/O) during recovery starts the store with an empty index
    // rather than failing construction and blocking proxy services from starting.
    private void RebuildFromSidecars(bool discardCorruptIndex = false)
    {
        _entries.Clear();
        if (discardCorruptIndex)
            TryDelete(_indexPath);

        try
        {
            HashSet<ProxyKey> adoptedKeys =
                MergeSidecarCandidates(CollectSidecarCandidates(CancellationToken.None));
            FlushCore(adoptedKeys);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Rebuilding the proxy index from sidecars failed; starting with an empty in-memory index.");
        }
    }

    // The meta.json walk + per-entry validation (filesystem work) runs without _lock so a large or
    // shared store cannot block preview TryGet/Touch behind startup reconciliation; only the _entries
    // merge below needs the lock.
    private List<ProxyEntry> CollectSidecarCandidates(CancellationToken cancellationToken)
    {
        List<ProxyEntry> candidates = [];
        foreach (string metadataPath in Directory.EnumerateFiles(StoreRootPath, "meta.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ProxyEntry entry in ReadMetadataEntries(metadataPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryValidateEntry(entry))
                    continue;

                string proxyPath;
                try
                {
                    proxyPath = GetAbsolutePath(entry);
                }
                catch
                {
                    continue;
                }

                if (File.Exists(proxyPath))
                    candidates.Add(entry);
            }
        }

        return candidates;
    }

    // Must be called with _lock held: merges the lock-free candidate scan into _entries.
    private HashSet<ProxyKey> MergeSidecarCandidates(List<ProxyEntry> candidates)
    {
        HashSet<ProxyKey> adoptedKeys = [];
        foreach (ProxyEntry entry in candidates)
        {
            var key = (entry.Source, entry.Preset);
            if (!_entries.TryGetValue(key, out ProxyEntry? existing)
                || ShouldAdoptSidecar(entry, existing))
            {
                _entries[key] = entry;
                adoptedKeys.Add(key);
            }
        }

        return adoptedKeys;
    }

    private HashSet<string> CollectTrackedProxyPaths()
    {
        HashSet<string> tracked = [];
        foreach (ProxyEntry entry in _entries.Values)
        {
            try
            {
                tracked.Add(ProxyFingerprint.NormalizeAbsolutePath(GetAbsolutePath(entry)));
            }
            catch
            {
            }
        }

        return tracked;
    }

    private void ReclaimOrphanProxyFiles(HashSet<string> tracked, CancellationToken cancellationToken)
    {
        foreach (string file in Directory.EnumerateFiles(StoreRootPath, "*.mp4", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ProxyPathUtilities.IsGeneratedProxyTempPath(StoreRootPath, file))
                continue;

            // Only reclaim files that match the generated proxy naming scheme; a user who points the
            // store root at an existing media folder must not have unrelated *.mp4 files deleted just
            // because they are absent from index.json.
            if (!ProxyPathUtilities.IsGeneratedProxyFinalPath(StoreRootPath, file))
                continue;

            if (tracked.Contains(ProxyFingerprint.NormalizeAbsolutePath(file)))
                continue;

            // A just-generated proxy is moved into place before its index/sidecar entry is
            // written; skipping recent files avoids racing that window. Genuine orphans are
            // reclaimed once they age past the same threshold used for temp cleanup.
            if (!IsOldEnoughToCleanGeneratedTemp(file))
                continue;

            TryReclaimProxyFile(file);
        }
    }

    private static IEnumerable<ProxyEntry> ReadMetadataEntries(string metadataPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(metadataPath);
        }
        catch
        {
            yield break;
        }

        ProxySourceMetadata? metadata = null;
        try
        {
            metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(json, s_jsonOptions);
        }
        catch
        {
        }

        // An effect-item single-ProxyEntry sidecar also deserializes as ProxySourceMetadata (Version and
        // Entries take their defaults), so only treat it as a wrapper when it actually carries
        // entries — otherwise fall through to the effect-item ProxyEntry parse so recovery still adopts it.
        if (metadata is { Entries.Count: > 0 })
        {
            if (metadata.Version == ProxySourceMetadata.CurrentVersion)
            {
                foreach (ProxyEntry entry in metadata.Entries)
                {
                    if (entry.Source == metadata.Source)
                        yield return entry;
                }
            }

            yield break;
        }

        ProxyEntry? effectItemEntry = null;
        try
        {
            effectItemEntry = JsonSerializer.Deserialize<ProxyEntry>(json, s_jsonOptions);
        }
        catch
        {
        }

        if (effectItemEntry != null)
            yield return effectItemEntry;
    }

    private void RemoveMetadataEntry(ProxyEntry removed)
    {
        try
        {
            string metadataPath = Path.Combine(Path.GetDirectoryName(GetAbsolutePath(removed))!, "meta.json");
            if (!File.Exists(metadataPath))
                return;

            string json = File.ReadAllText(metadataPath);
            ProxySourceMetadata? metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(json, s_jsonOptions);
            if (metadata == null)
                return;

            ProxyEntry[] entries =
            [
                .. metadata.Entries.Where(entry => entry.Source != removed.Source || entry.Preset != removed.Preset
                    || entry.ProxyFileRelative != removed.ProxyFileRelative)
            ];

            if (entries.Length == 0)
            {
                File.Delete(metadataPath);
                return;
            }

            metadata = metadata with { Entries = [.. entries] };
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
        }
        catch
        {
        }
    }

    private static bool ShouldAdoptSidecar(ProxyEntry sidecarEntry, ProxyEntry existingEntry)
    {
        return sidecarEntry.State is ProxyState.Ready or ProxyState.Stale
            && existingEntry.State is not (ProxyState.Ready or ProxyState.Stale);
    }

    private static bool IsOldEnoughToCleanGeneratedTemp(string path)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= s_generatedTempCleanupMinAge;
        }
        catch
        {
            return false;
        }
    }
}
