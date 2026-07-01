# Contract: `IProxyStore`

**Feature**: 002-proxy-media | **Type**: internal extensibility surface (within `Beutl.Engine.Media.Proxy`)

The single source of truth for the on-disk proxy cache and its metadata. Owns `index.json`, per-source subdirectories, and the atomic write protocol. Consumed by `IProxyResolver`, `IProxyGenerator` implementations, and `ProxyEvictionService`.

## C# shape

```csharp
public interface IProxyStore
{
    string StoreRootPath { get; }

    /// <summary>Lookup an entry by source identity + preset. Returns null if not present.</summary>
    ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset);

    /// <summary>All entries (consumed by tool-tab UI; do not hot-path).</summary>
    IReadOnlyList<ProxyEntry> Enumerate();

    /// <summary>Register a freshly generated proxy. Updates index.json atomically.</summary>
    /// <remarks>Caller must have already moved the file from .tmp to its final name.</remarks>
    void Register(ProxyEntry entry);

    /// <summary>
    /// Mark an entry as stale or partial without deleting its file.
    /// Returns true if the state changed.
    /// </summary>
    bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null);

    /// <summary>Delete a proxy file and remove its index entry. Returns true if removed.</summary>
    bool Delete(ProxyFingerprint source, ProxyPreset preset);

    /// <summary>Touch LastUsedUtc; called by IProxyResolver every time a proxy is handed out.</summary>
    void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc);

    /// <summary>Total bytes consumed across all entries (excludes Generating / Partial).</summary>
    long GetTotalBytes();

    /// <summary>Project-scoped totals, optionally filtered to a set of source paths.</summary>
    long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths);

    /// <summary>
    /// Atomic flush of the in-memory index to disk. Called from a background thread,
    /// never on the UI thread. Idempotent.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Boot-time scan: validate every entry against disk, drop missing files,
    /// adopt orphan meta.json sidecars, delete orphan .tmp files.
    /// </summary>
    Task ReconcileAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raised after Register / Delete / TryTransition. Lets the resolver and the UI
    /// invalidate cached views.
    /// </summary>
    event EventHandler<ProxyStoreChangedEventArgs> Changed;
}

public sealed class ProxyStoreChangedEventArgs : EventArgs
{
    public required ProxyFingerprint Source { get; init; }
    public required ProxyPreset Preset { get; init; }
    public required ProxyStoreChangeKind Kind { get; init; }
}

public enum ProxyStoreChangeKind { Registered, StateChanged, Deleted, Touched }
```

## Behavior contract

1. **Identity**: composite key `(ProxyFingerprint, ProxyPreset)`. Two entries with the same source but different presets coexist.
2. **Atomic writes**: `Register` only ever observes finalized files. The generator implementation is responsible for `*.tmp → final` rename before calling `Register`. `index.json` writes use the same temp-then-rename pattern.
3. **Size metadata**: `Ready` / `Stale` entries registered with the store must include `OriginalLogicalFrameSize` and `ProxyDecodedFrameSize`; the resolver uses those values to preserve logical footprint and compute supply density without reopening the original source.
4. **Thread safety**: all members are safe to call from multiple threads. `Enumerate` returns a snapshot.
5. **No silent fallback**: `TryGet` returns the recorded state; deciding whether to use it is the caller's job (the resolver checks state, the generator checks for stale, etc.).
6. **`Touch` is hot-path-friendly**: it must complete in <100 µs without I/O. Implementations debounce disk persistence.
7. **`ReconcileAsync` is best-effort**: failures log a warning but never throw to the caller. The store remains usable even if reconciliation finds inconsistencies (preview falls back to original for any source whose proxy was dropped).
8. **No exceptions for "not found"**: `TryGet` / `TryTransition` / `Delete` return `null` / `false`, not exceptions.

## Test obligations (NUnit)

- `Register` then `TryGet` returns the same entry; survives a `FlushAsync` + fresh store instance.
- `Delete` removes both the index entry and the proxy file on disk.
- `Touch` updates `LastUsedUtc` and persists eventually (deterministic via a controllable clock).
- `ReconcileAsync` on a store containing one orphan `.tmp` and one missing-file entry produces the expected diff and leaves the store internally consistent.
- Concurrent writes from two threads against `Register` for distinct keys both succeed; against the same key, one wins, the other is observable via `Changed`.
- `index.json` corruption: a malformed file triggers a directory rescan and a fresh `index.json`; existing on-disk proxy files survive when their `meta.json` sidecars are intact.
