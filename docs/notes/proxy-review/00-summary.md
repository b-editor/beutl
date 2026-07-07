# Proxy media branch review — summary

Branch `yuto-trd/proxy` vs `main` (62 commits; ~7,000 lines of production code across 80 files, plus 35 test files). This is the full proxy-media workflow (PR #2040 scope). Reviewed by five focused passes; per-area detail is in the sibling files:

| # | Area | Report |
|---|------|--------|
| 01 | Proxy engine core (concurrency / data integrity) | [`01-engine-core.md`](01-engine-core.md) |
| 02 | FFmpeg generation / IPC / encoding | [`02-ffmpeg-ipc.md`](02-ffmpeg-ipc.md) |
| 03 | Editor / UI layer (ViewModel + XAML) | [`03-ui-editor.md`](03-ui-editor.md) |
| 04 | Rendering integration / ProjectSystem / Configuration | [`04-rendering-integration.md`](04-rendering-integration.md) |
| 05 | Public API / design review | [`05-design-review.md`](05-design-review.md) |

## Verdict

**No Blockers, no High-severity findings.** The four standard axes (GPL/MIT boundary, XAML compiled bindings, NUnit conventions, SourceGenerator impact) are clean in every area, and NUnit coverage for the new logic is thorough. The public surface is well-designed: real extensibility seams (`IProxyGenerator`/`IProxyResolver`/`IProxyStore`) are interfaces, breaking changes carry correct `refactor!:`/`feat!:` + `BREAKING CHANGE:` footers, and no `[Obsolete]` shims / "v2" types / compat overloads were introduced.

All findings are Medium (9) and Low (12). They cluster into three themes.

## Severity roll-up

| Area | Blocker | High | Medium | Low | Nit |
|------|:---:|:---:|:---:|:---:|:---:|
| 01 Engine core | 0 | 0 | 3 | 4 | — |
| 02 FFmpeg / IPC | 0 | 0 | 1 | 4 | — |
| 03 UI / Editor | 0 | 0 | 2 | 2 | — |
| 04 Rendering integration | 0 | 0 | 1 | 1 | — |
| 05 Design | 0 | 0 | 2 | 1 | 0 |
| **Total** | **0** | **0** | **9** | **12** | **0** |

## Cross-cutting themes

### Theme A — Blocking I/O on the preview / UI hot path (the dominant theme; 5 of 9 Mediums)

Several places do filesystem work (`File.Exists`, `FileInfo.Length`, `ProxyFingerprint.FromFile` → `ResolveLinkTarget` syscall) either while holding the in-process store lock or directly on the UI thread, per event, during bursts like "Generate All". These are responsiveness regressions, not correctness bugs, but they compound with clip count.

- **01 / `ProxyStore.ReconcileAsync` (ProxyStore.cs:216-299)** — stats every tracked entry under `_lock`; stalls the first preview at startup. Fix: snapshot under lock, stat outside, re-take lock only to apply mutations (mirrors the orphan-scan treatment already applied).
- **01 / `ProxyStore.FlushCore` (ProxyStore.cs:506-516,726-747)** — acquires the cross-process `index.lock` (up to ~2 s spin) while holding `_lock`, stalling `TryGet`/`Touch` under multi-instance contention. Fix: acquire the file lock before `_lock`, or lower the retry ceiling so the existing `DegradePersistence` path engages sooner.
- **03 / `ProxiesTabViewModel.Refresh()` (ProxiesTabViewModel.cs:427,679-707)** — full project-graph walk + clip-list rebuild per store/job event; ~2N passes during an N-clip Generate All. Fix: coalesce like `EditViewModel.FlushPendingProxyInvalidations` already does.
- **03 / `ElementViewModel.ElementUsesChangedSource` (ElementViewModel.cs:1063-1080)** — the badge relevance gate that exists to *avoid* work itself re-stats sources on the UI thread per event. Fix: cache resolved comparable keys (invalidated by the existing `ThumbnailsInvalidated` signal) and match in-memory.

`EditViewModel` already gets this right (coalesced burst), so it's the model to copy.

### Theme B — Error-path robustness (the findings most worth fixing before merge)

- **04 / `DecoderRegistry.OpenMediaFile` (DecoderRegistry.cs:53-56)** — *the top correctness pick.* Proxy `Resolve`/`Pin`/`new Uri` run **outside** the try/catch that guards the proxy-decoder-open loop, so a resolver/store exception in PreferProxy mode propagates and **skips the original-decode fallback** — breaking a clip that would open fine with proxy off. Violates the "proxy is best-effort, must never break original playback" contract. Fix: wrap the whole `if (options.PreferProxy && …)` block so proxy failures degrade to original decode.
- **02 / `FFmpegProxyGenerator` publish rollback (FFmpegProxyGenerator.cs:136-149,359-376)** — the restore helpers (`RestoreMetadata`, `RestoreFinalPath`) are not exception-guarded, so a rollback I/O error masks the primary exception (including `OperationCanceledException`) and can leave the previous proxy only as an orphaned `.bak` with the final deleted — defeating the atomicity the surrounding code builds. Fix: best-effort each restore (file first, metadata second), never let a rollback fault replace the primary cause.
- **01 / `ReconcileAsync` swallows `OperationCanceledException` (ProxyStore.cs:317-320)** — a cancelled reconcile completes as `RanToCompletion`; callers can't tell cancelled from finished. Fix: let OCE propagate, keep the swallow for genuine I/O faults only.

### Theme C — Design refinements (05; the F1 item needs a user decision)

- **05 F1 (Medium) — `CompositionContext.ForceOriginalSource` + `PreferProxy` are two derived bools for one tri-state**, with a hand-maintained invariant `ForceOriginalSource => !PreferProxy`. The ultimate consumer (`VideoSource`) reads only `PreferProxy`; `ForceOriginalSource` is threaded through ProjectSystem + NodeGraph purely to be re-projected. Collapsing to a single signal (a `PreviewSourceMode` on the context, or one bool) removes the invariant instead of documenting it — **but it ripples across ProjectSystem + NodeGraph replay paths, so it's a non-trivial diff that should be a user call, not silently settled.**
- **05 F2 (Medium) — `IProxyEvictionPolicy` over-promises**: its name implies a pluggable eviction strategy but it exposes only `long MaxTotalBytes` (a cap readout); the actual policy is baked into `ProxyEvictionService`. Either rename to what it is (`IProxyStoreCapInfo`/status) and document the closed strategy, or widen it into a real selection seam.
- **05 F3 (Low) — `ProxyStoreConfig.DefaultPreset`** is an unvalidated `int` that defers an out-of-range failure to `ProxyPresetDefinitions.Get` (throws). Clamp on get/set like `MaxTotalBytes` does.

## Recommended action order

1. **04 DecoderRegistry try/catch** — small, high-value correctness fix; clear contract violation.
2. **02 publish-rollback best-effort guards** — data-integrity on the exact error path that matters.
3. **01 OCE propagation** in `ReconcileAsync` — cheap correctness fix.
4. **Theme A hot-path coalescing/snapshotting** (01 + 03) — responsiveness under Generate All; adopt the `EditViewModel` pattern uniformly.
5. **05 F1** — surface the CompositionContext single-signal trade-off to the user and decide; it's the one item that shouldn't be resolved unilaterally.
6. Remaining Lows (01 timestamp/pin-recheck, 02 color-space/level/static-event, 03 missing `ProxiesTabViewModelTests` + settings-page localized preset names, 05 F2/F3) — fold in opportunistically.

Notably clean and explicitly verified: the Scene → global `PreviewSourceMode` move has **no serialization back-compat concern** (the field never shipped on `Scene`), export/save-frame correctly **force original decode** through nested scenes, cache invalidation on mode switch is tested, and the GPL/MIT boundary holds everywhere.

## Resolution (applied on this branch)

All correctness, robustness, hot-path, and safe-Low findings were fixed with tests; the two public-API design calls (05 F1, F2) are left for a human decision because they ripple across projects. One finding (03 Low, "missing `ProxiesTabViewModelTests`") was a **false positive** — the 990-line suite exists.

| Finding | Fix | Test |
|---|---|---|
| 04 Medium — `DecoderRegistry` proxy resolve/pin outside try/catch | Wrapped the whole proxy block so any proxy-side fault degrades to original decode | `DecoderRegistryProxyRoutingTests.OpenMediaFile_When{Resolver,Pin}Throws_FallsBackToOriginal` |
| 02 Medium — publish rollback masks primary exception / orphans proxy | Restore helpers made best-effort (swallow + log); file restored before metadata | `FFmpegProxyGeneratorPublishTests.PublishAsync_WhenRollbackMetadataRestoreFails_*` |
| 01 Low — `ReconcileAsync` swallows `OperationCanceledException` | Rethrow OCE on cancellation, swallow only genuine I/O faults | `ProxyStoreTests.ReconcileAsync_WhenCancelled_ThrowsOperationCanceled` |
| 01 Medium — `ReconcileAsync` stats every entry under `_lock` | Snapshot under lock, stat outside, re-acquire and re-validate each key before mutating | existing `ProxyStoreTests` reconcile suite (behavior preserved) |
| 01 Medium — `FlushCore` lock-acquire spin up to ~2 s under `_lock` | Lowered `DefaultLockAcquireMaxAttempts` 200 → 50 (~0.5 s), degraded path replays | existing `ProxyStoreTests` |
| 01 Low — `TryTransition` resets `GeneratedAtUtc` on every transition | Refresh only on transition into `Ready` | `ProxyStoreTests.TryTransition_{ToNonReadyState_Preserves,IntoReadyState_Refreshes}GeneratedAtUtc` |
| 01 Low — eviction pin checked only at collection time | Re-check `IsPinned` immediately before each delete | `ProxyEvictionTests.Sweep_ReChecksPinTakenAfterCandidateCollection` |
| 03 Medium — `ProxiesTabViewModel.Refresh` un-coalesced per event | `ScheduleRefresh` collapses a burst into one rebuild per UI tick (light `RefreshJobs` stays immediate) | existing `ProxiesTabViewModelTests` (green) |
| 03 Medium — `ElementViewModel` badge gate re-stats on UI thread | Gate matches against cached fingerprints (`ElementUsesChangedSourceCached`) — no per-event `ResolveLinkTarget` | existing `ElementViewModelProxyStateTests` (green) |
| 03 Low — settings-page preset dropdown shows raw enum names | New `ProxyPresetNameConverter` renders localized names in the combo `ItemTemplate` | shell build (HeadlessUITests scope) |
| 05 F3 Low — `ProxyStoreConfig.DefaultPreset` unvalidated int | Clamp to `[MinPreset, MaxPreset]` on get/set like `MaxTotalBytes` | `ProxyStoreConfigTests.DefaultPreset_ClampsToKnownPresetRange` |
| 04 Low — `DecoderRegistry.ProxyResolver` mutable static | Documented single-assignment (startup-set) expectation | — |
| 05 F1 Medium — `CompositionContext` dual-bool redundancy | **Removed `CompositionContext.ForceOriginalSource`**; `PreferProxy` is now the single decode-selection signal. Producer (`SceneCompositor`) seeds it from the top-level force-original input + `PreviewSourceMode`; consumers use `!PreferProxy`; NodeGraph capture/restore drops the redundant field. `SceneCompositor.ForceOriginalSource` stays as the export input. | existing `SceneCompositorTests` / `GraphSnapshotTests` / `NodeGraphFilterEffectRenderNodeTests` (updated, green) |
| 05 F2 Medium — `IProxyEvictionPolicy` over-promises | **Renamed to `IProxyStoreCapInfo`** with a doc note that the eviction strategy is a closed MVP decision (FR-017 rationale); field `_storeCapInfo` | existing `ProxiesTabViewModelTests` (updated, green) |

Both design decisions were confirmed with the user (2026-07-07): F1 → collapse to a single signal; F2 → rename to a cap-readout and document the closed strategy.

**Chosen shapes:** F1 kept a single `bool PreferProxy` (rather than moving the `PreviewSourceMode` enum onto the context) — the enum stays where it is produced and the context carries only the derived bit consumers need. F2 took the rename-and-document option (not the injectable-strategy widening), matching the MVP scope.

Status: all projects build; affected unit tests green (242 passed / 0 failed / 1 pre-existing skip). Not committed.
