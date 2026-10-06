using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Beutl.Media.Proxy;

public sealed partial class ProxyStore
{
    // Intentionally holds _lock for the full read-merge-write. Moving the write outside _lock
    // (to unblock TryGet/Touch/Enumerate during flush) breaks two invariants the single-lock span
    // guarantees: (1) the disk read must happen under the cross-process index.lock so another
    // instance cannot write between our read and our write; (2) the _entries clear+refill from the
    // merged result is only safe with no concurrent in-process mutation — once _lock is released
    // during the write, a concurrent Register/Delete can land between the refill and the write, and
    // the refill re-adds a just-deleted key from disk while Phase 3's HashSet.Remove(key) clears a
    // concurrent degraded flush's pending addition, losing that change. Fixing (2) needs a
    // ref-counted pending structure plus an incremental _entries reconcile, which is high-risk for a
    // LOW-severity finding and not deterministically testable. The Touch debounce (1 s) and
    // DegradePersistence (pending replay on the next uncontended flush) mitigate the perf concern.
    private void FlushCore(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys = null,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys = null)
    {
        Directory.CreateDirectory(StoreRootPath);
        FileStream? indexLock = AcquireIndexLock(out Exception? lockFailure);
        if (indexLock is null)
        {
            DegradePersistence(changedKeys, removedKeys, lockFailure);
            return;
        }

        using (indexLock)
        {
            if (!TryReadIndexEntriesFromDisk(out List<ProxyEntry> diskEntries, out Exception? readFailure))
            {
                // index.json exists but is unreadable/corrupt. Seeding the merge from an empty disk view
                // would drop every in-memory entry not in this flush and write that partial index back,
                // destroying valid state. Degrade instead: keep serving memory and replay on a later
                // flush (startup already rebuilds from sidecars when the index is unreadable). Preserve
                // the real read exception as the logged cause for diagnosis.
                DegradePersistence(changedKeys, removedKeys, readFailure);
                return;
            }

            // Seed from the degraded-and-pending ops, then let this flush's own ops supersede them per key:
            // a fresh registration cancels a stale pending delete (the delete/regenerate race after
            // transient lock contention) and a fresh delete cancels a stale pending persist. Without the
            // supersede, effectiveChanged.ExceptWith below would drop the freshly registered proxy and
            // _entries.Clear() would lose it from memory too. Mirrors the re-Register-supersedes-Delete rule
            // DegradePersistence already applies on the degrade path.
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> effectiveRemoved = [.. _pendingRemoveKeys];
            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> effectiveChanged = [.. _pendingPersistKeys];
            if (changedKeys != null)
            {
                effectiveRemoved.ExceptWith(changedKeys);
                effectiveChanged.UnionWith(changedKeys);
            }

            if (removedKeys != null)
            {
                effectiveChanged.ExceptWith(removedKeys);
                effectiveRemoved.UnionWith(removedKeys);
            }

            effectiveChanged.ExceptWith(effectiveRemoved);

            Dictionary<(ProxyFingerprint Source, ProxyPreset Preset), ProxyEntry> merged = [];
            foreach (ProxyEntry entry in diskEntries)
            {
                var key = GetKey(entry);
                if (effectiveRemoved.Contains(key))
                    continue;

                merged[key] = entry;
            }

            HashSet<(ProxyFingerprint Source, ProxyPreset Preset)> touchedKeys = [.. _touchDirtyKeys];
            foreach (var key in touchedKeys)
            {
                if (effectiveRemoved.Contains(key))
                    continue;

                if (merged.TryGetValue(key, out ProxyEntry? diskEntry)
                    && _entries.TryGetValue(key, out ProxyEntry? localEntry))
                {
                    DateTime lastUsedUtc = diskEntry.LastUsedUtc >= localEntry.LastUsedUtc
                        ? diskEntry.LastUsedUtc
                        : localEntry.LastUsedUtc;
                    merged[key] = diskEntry with { LastUsedUtc = lastUsedUtc };
                }
                else if (!effectiveChanged.Contains(key))
                {
                    // A touched key that is not yet on disk but is pending persistence (e.g. a
                    // registration degraded by transient lock contention) must survive this replay;
                    // the effectiveChanged pass below writes it. Only drop keys with no pending change.
                    _entries.Remove(key);
                }
            }

            foreach (var key in effectiveChanged)
            {
                if (_entries.TryGetValue(key, out ProxyEntry? entry))
                    merged[key] = entry;
            }

            _entries.Clear();
            foreach (ProxyEntry entry in merged.Values)
            {
                _entries[GetKey(entry)] = entry;
            }

            var index = new ProxyStoreIndex { Entries = [.. merged.Values] };
            string json = JsonSerializer.Serialize(index, s_jsonOptions);
            string tmp = Path.Combine(
                StoreRootPath,
                $"{Path.GetFileName(_indexPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, _indexPath, overwrite: true);
                foreach (var key in touchedKeys)
                {
                    _touchDirtyKeys.Remove(key);
                }

                _touchDirty = _touchDirtyKeys.Count > 0;
                _pendingPersistKeys.Clear();
                _pendingRemoveKeys.Clear();
                _persistenceDegraded = false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A durable-write failure (transient I/O or a store root without write permission)
                // degrades like a contended lock instead of throwing out of Register/TryTransition/
                // FlushAsync; the pending sets are left populated (not cleared above) so the change
                // replays on the next flush.
                DegradePersistence(changedKeys, removedKeys, ex);
            }
            finally
            {
                TryDelete(tmp);
            }
        }
    }

    private void DegradePersistence(
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? changedKeys,
        IReadOnlySet<(ProxyFingerprint Source, ProxyPreset Preset)>? removedKeys,
        Exception? cause = null)
    {
        bool firstDegradation = !_persistenceDegraded;
        _persistenceDegraded = true;
        if (removedKeys != null)
        {
            foreach (var key in removedKeys)
            {
                _pendingRemoveKeys.Add(key);
                _pendingPersistKeys.Remove(key);
            }
        }

        if (changedKeys != null)
        {
            foreach (var key in changedKeys)
            {
                // A re-Register after a Delete (both degraded) must supersede the pending removal.
                _pendingRemoveKeys.Remove(key);
                _pendingPersistKeys.Add(key);
            }
        }

        // Log only on the transition into the degraded state: the 1 s touch-flush loop retries a
        // persistent fault every second and a per-attempt warning would flood the log. The flag is
        // reset by the next successful flush, so each degradation episode logs exactly once.
        if (!firstDegradation)
            return;

        if (cause is null)
        {
            s_logger.LogWarning(
                "Proxy index lock at '{LockPath}' is contended; skipping durable persistence and serving in-memory state (read-only degradation).",
                _indexLockPath);
        }
        else
        {
            s_logger.LogWarning(
                cause,
                "Proxy index persistence for '{IndexPath}' failed; skipping durable persistence and serving in-memory state until a later flush succeeds.",
                _indexPath);
        }
    }

    private async Task FlushTouchesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            lock (_lock)
            {
                if (!_touchDirty)
                    return;

                FlushCore();
            }

            ReclaimRetiredProxyFiles();
            Interlocked.Exchange(ref _touchFlushFaulted, 0);
        }
        catch (Exception ex)
        {
            // FlushCore degrades I/O and permission faults internally, so anything landing here is
            // unexpected (bug-class). Log once per failure streak — the finally block reschedules
            // this loop every second and a per-attempt error would flood the log.
            if (Interlocked.Exchange(ref _touchFlushFaulted, 1) == 0)
            {
                s_logger.LogError(
                    ex,
                    "Deferred proxy touch flush failed unexpectedly; LastUsedUtc updates stay pending and will retry every second.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _touchFlushScheduled, 0);
            lock (_lock)
            {
                if (_touchDirty)
                    ScheduleTouchFlush();
            }
        }
    }

    private void ScheduleTouchFlush()
    {
        if (Interlocked.Exchange(ref _touchFlushScheduled, 1) == 0)
            _ = FlushTouchesAsync();
    }

    // Returns false only when index.json exists but cannot be deserialized (corrupt / truncated): the
    // on-disk content is unknown, so FlushCore must not overwrite it and drop valid in-memory entries.
    // A missing file (fresh store) and an old-version index (intentionally discarded and upgraded on
    // load) are both legitimate empty reads, not failures.
    private bool TryReadIndexEntriesFromDisk(out List<ProxyEntry> entries, out Exception? failure)
    {
        entries = [];
        failure = null;
        if (!File.Exists(_indexPath))
            return true;

        ProxyStoreIndex? index;
        try
        {
            index = JsonSerializer.Deserialize<ProxyStoreIndex>(File.ReadAllText(_indexPath), s_jsonOptions);
        }
        catch (Exception ex)
        {
            failure = ex;
            return false;
        }

        if (index?.Version != ProxyStoreIndex.CurrentVersion)
            return true;

        foreach (ProxyEntry entry in index.Entries)
        {
            if (TryValidateEntry(entry))
                entries.Add(entry);
        }

        return true;
    }

    private FileStream? AcquireIndexLock(out Exception? failure)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                failure = null;
                return new FileStream(
                    _indexLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (attempt < _lockAcquireMaxAttempts)
            {
                Thread.Sleep(10);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // UnauthorizedAccessException (store root without write permission) is not
                // transient, so it skips the retry loop and degrades immediately. Only the
                // permission fault is surfaced — an IOException here is ordinary lock
                // contention, which the null-cause degradation message already describes.
                failure = ex is UnauthorizedAccessException ? ex : null;
                return null;
            }
        }
    }
}
