# Proxy engine core review

Scope: `src/Beutl.Engine/Media/Proxy/*` on `yuto-trd/proxy` vs `main`. Focus axes: locking/concurrency, persisted-index integrity, job-queue correctness, resource lifetime, exception safety. Standard four axes (GPL/MIT, XAML, NUnit, SourceGenerators) are all clear (see the `## Clear` section).

## Severity summary

| Severity | Count |
|---|---|
| Blocker | 0 |
| High | 0 |
| Medium | 3 |
| Low | 4 |

Total findings: 7.

---

## [Medium] ProxyStore.cs:216-299 — ReconcileAsync stats every tracked entry (File.Exists / FileInfo.Length / FromFile+symlink-resolve) while holding the in-process `_lock`

Failure scenario: `ReconcileAsync` runs at startup. Inside `lock (_lock)` (lines 233-299) it iterates every entry in `_entries` and per entry does `File.Exists(path)` (253), `HasValidReadyFile` → `new FileInfo(absolutePath).Length` (777-796), and for every `Ready` entry whose source still exists, `ProxyFingerprint.FromFile(entry.Source.SourcePath)` (268), which opens a `FileInfo`, calls `ResolveLinkTarget(returnFinalTarget: true)` and stats the file. For a store with hundreds of entries on a slow or network volume, this is a multi-hundred-millisecond-to-seconds filesystem scan performed entirely under `_lock`. Every concurrent `TryGet` / `Touch` / `Enumerate` — which the code itself documents as the "preview hot path" (`ProxyResolver.Resolve` calls `_store.TryGet` per preset plus `Touch`) — blocks for the whole scan. Startup reconcile therefore stalls the first preview.

The author already moved the *orphan-file* scan outside the lock (comment at 301-305) but left the per-entry existence/size/fingerprint scan inside it.

Recommended fix: snapshot `_entries.Values` under `_lock`, release the lock, perform the `File.Exists` / `FileInfo.Length` / `FromFile` checks on the snapshot, then re-take `_lock` only to apply the `missing`/`changed` mutations (re-validating each key still exists and is unchanged, as the code already tolerates staleness elsewhere). This mirrors the treatment already applied to `ReclaimOrphanProxyFiles`.

## [Medium] ProxyStore.cs:506-516, 726-747 — FlushCore acquires the cross-process `index.lock` (spinning up to ~2 s) while holding `_lock`, stalling hot-path readers under multi-instance contention

Failure scenario: two Beutl instances share one proxy store root (documented, supported: the code has "shared-store peer" handling in eviction and reconcile). `FlushCore` is always called under `_lock`; the first thing it does is `AcquireIndexLock()` (511), which retries `new FileStream(..., FileShare.None)` up to `_lockAcquireMaxAttempts` (default 200) with `Thread.Sleep(10)` between attempts — up to ~2 s of blocking spin. That spin happens while `_lock` is held, so any concurrent `TryGet` / `Touch` / `Enumerate` on the UI/preview thread is blocked for up to ~2 s whenever the peer instance holds the file lock. A `Register` / `TryTransition` from a completing generation triggers this on the same thread that services previews.

The long comment at 495-505 defends keeping the *disk write* under `_lock` and rates the perf concern LOW, but it addresses the read-merge-write window, not the up-front lock-acquire spin; the 2 s worst case on a hot-path mutation is worse than the comment implies.

Recommended fix: acquire the cross-process `index.lock` *before* taking `_lock` (or take `_lock` only to snapshot the pending mutation set, then do lock-acquire + read-merge-write with `_lock` released and a short re-take to publish the merged `_entries`). At minimum, drop `DefaultLockAcquireMaxAttempts` / the sleep so the degraded path (`DegradePersistence`, which already exists and replays on the next flush) engages far sooner than 2 s.

## [Medium] ProxyJobQueue.cs:174-199 — on `WriteAsync` failure the enqueued item can already be dispatching, so `Remove` + `Dispose` races a running generation

Failure scenario (partially speculative on the exact interleaving): `EnqueueAsync` adds the new `WorkItem` to `_items` / `_itemsByKey` under `_lock` (175-176), *then* releases the lock and calls `_channel.Writer.WriteAsync(item, cancellationToken)` (190). Between those two steps the item is already `Queued` and visible to `TakeNextDispatchable` (419-438). If another job's channel permit is being drained concurrently and this item has the highest priority, `ProcessOneAsync` picks it, `TryStart()`s it (→ `Running`) and enters `generator.GenerateAsync`. If the caller's `cancellationToken` then cancels the `WriteAsync`, the `catch` (194-199) runs `Remove(item); item.Dispose()` — disposing the item's linked CTS and removing it from tracking while the generator is mid-run. Consequences: the running generation is now untracked (`Pending()` / `Cancel` / `CancelAll` can't see it), its terminal `OnJobChanged(Succeeded/Failed)` fires for a job the queue already "removed", and `item.Dispose()` disposes the CTS underneath the still-executing job.

The realistic trigger is the enqueue token cancelling while other jobs are draining; the channel-full path is a second, rarer trigger. The permit/item decoupling is otherwise intentional and sound.

Recommended fix: only expose the item to `TakeNextDispatchable` after the permit is durably queued — e.g. add to `_items` *after* a successful `WriteAsync`, or mark the item non-dispatchable until write completes and flip it under `_lock` in the success path. On the failure path, guard `Remove`/`Dispose` so it is a no-op once the item has left `Queued` (check `TryStart` already fired).

## [Low] ProxyStore.cs:119-125 — TryTransition overwrites GeneratedAtUtc on every transition, not just on (re)generation

`TryTransition` sets `GeneratedAtUtc = DateTime.UtcNow` for *all* legal transitions, so `Ready → Stale`, `Failed → Generating`, `Ready → None`, etc. all reset the "when was this proxy file produced" timestamp even though the underlying file is unchanged (Stale) or absent. Currently benign: a repo grep shows `GeneratedAtUtc` has no consumer outside `ProxyEntry` / `ProxyStore`, so nothing reads the corrupted value today. It becomes a latent data bug the moment any age/cache-invalidation logic keys on it. Recommended fix: set `GeneratedAtUtc` only on the transition into `Ready` (or when a generator registers a fresh file), and leave it untouched otherwise.

## [Low] ProxyEvictionService.cs:126-179 — pin check is evaluated at candidate-collection time, not immediately before each Delete (TOCTOU against decode-lifetime pins)

`Evict` filters out pinned candidates once, while building the `candidates` list (132), then iterates deleting them (169-179). A `Pin` taken by a `ProxyMediaReader` *after* collection but *before* the delete loop reaches that candidate is not observed, so the file can be deleted while a reader is about to open it. The pin exists precisely to guarantee "eviction cannot delete it while a MediaReader is decoding it" (`ProxyResolver.Pin` doc), and this snapshot check weakens that guarantee for late deletes in a large sweep. Impact is limited: `TryDeleteProxyFile` swallows the delete failure (Windows share-violation leaves the entry intact) and on Unix an already-open handle survives unlink. Recommended fix: re-check `_resolver?.IsPinned(absolutePath)` inside the delete loop, immediately before `_store.Delete`.

## [Low] ProxyStore.cs:216-323 — ReconcileAsync's outer `catch { }` swallows OperationCanceledException, so a cancelled reconcile completes as success

`ReconcileAsync` threads `cancellationToken` through many `ThrowIfCancellationRequested` calls, but the method-wide `catch` (317-320) catches everything, including the `OperationCanceledException` those checks throw. The returned `Task` therefore completes in the `RanToCompletion` state on cancellation rather than `Canceled`. A caller that `await`s reconcile expecting an OCE on shutdown will not get one, and cannot distinguish "cancelled" from "finished." Recommended fix: let `OperationCanceledException` propagate (add a `when (!cancellationToken.IsCancellationRequested)` guard or a dedicated `catch (OperationCanceledException) { throw; }` before the swallow), keeping the best-effort swallow for genuine I/O faults only.

## [Low] ProxyStore.cs:205-214 — FlushAsync checks the token only at entry, then runs the (up-to-~2 s) blocking flush ignoring it

`FlushAsync` calls `cancellationToken.ThrowIfCancellationRequested()` once, then does the synchronous `FlushCore()` under `_lock` (which, per the Medium finding above, can block ~2 s on lock-acquire) with no further token observation, and returns `Task.CompletedTask`. Cancellation requested during the flush is ignored. Minor, but the async signature implies cancellability it does not provide. Recommended fix: either honor the token inside the lock-acquire spin, or document that the flush is non-cancellable once started.

---

## Clear

Standard four axes:

- **GPL/MIT boundary** — all reviewed files live in `Beutl.Engine` (MIT). No `ProjectReference` to `Beutl.FFmpegWorker`, no `Beutl.Extensions.FFmpeg` / FFmpeg IPC types, no native-binary embedding. The subtree correctly exposes only Engine abstractions (`IProxyGenerator`, `IProxyGeneratorFactory`), matching `Beutl.Engine/CLAUDE.md` rule 6.
- **XAML compiled bindings** — no XAML / UserControls in scope.
- **NUnit conventions** — comprehensive matching tests exist under `tests/Beutl.UnitTests/Media/Proxy/` (`ProxyStoreTests`, `ProxyJobQueueTests`, `ProxyResolverTests`, `ProxyEvictionTests`, `ProxyFingerprintTests`, `ProxyPathUtilitiesTests`, `ProxyPresetDefinitionsTests`, `ProxyGeneratorRegistryTests`, `ProxyEntryStateTransitionsTests`, `ProxyMediaReaderTests`) plus integration suites. Coverage tracks the new logic.
- **SourceGenerator impact** — no changes under `src/Beutl.Engine.SourceGenerators/`; no generated-symbol surface touched.

Files with no additional concurrency/integrity findings beyond the above:

- `ProxyEntry.cs` — plain record, fine.
- `ProxyState.cs`, `ProxyStateTransitions.cs` — closed state machine; `IsLegal` matrix is coherent.
- `ProxyPreset.cs`, `ProxyPresetDefinitions.cs` — `ConcurrentDictionary`-backed override registry; `Register`/`Unregister` guarded by `Enum.IsDefined`; snapshots are copied (no live-view leak).
- `ProxyGeneratorRegistry.cs` — lock-guarded static registry returning copied snapshots; correct.
- `ProxyGenerationExceptions.cs`, `IProxyEvictionPolicy.cs`, `IProxyStore.cs`, `IProxyResolver.cs`, `IProxyJobQueue.cs` — declarations only.
- `ProxyMediaReader.cs` — disposes `inner` then `pin` in a `try/finally`; pin release is guaranteed even if inner disposal throws. Correct.
- `ProxyPathUtilities.cs` — path containment checks (`IsUnderDirectory`, rooted-path rejection) look robust against traversal; hash-dir shape validation is sound.
- `ProxyFingerprint.cs` — case-folding identity key vs case-preserving `SourcePath` for I/O is handled deliberately and documented.
- `ProxyResolver.cs` — `Unpin` uses the correct lock-free decrement-and-remove-if-zero loop (`TryUpdate` / `KeyValuePair` `TryRemove`); `PinHandle` is idempotent via `Interlocked.Exchange`. No issue found (the `Pin`/`IsPinned` normalization both flow through `Path.GetFullPath` on identically-built store-relative paths, so no key mismatch).
- `ProxyJobQueue.cs` lock ordering — the queue `_lock` and `WorkItem._lock` are never nested in opposite orders (each call takes them sequentially, not nested), so no deadlock; the WriteAsync race above is the only queue finding.
