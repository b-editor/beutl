# Implementation Plan: Proxy Media Workflow

**Branch**: `002-proxy-media` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/002-proxy-media/spec.md`

## Summary

While editing, Beutl must transparently serve preview decode requests from a low-resolution **proxy** of each heavy source clip; on export the renderer must always decode from the **original** source. The implementation routes the *preview decode path* through a new `IProxyResolver` consulted by `DecoderRegistry.OpenMediaFile`, while the *export decode path* (`SceneComposer` and/or the 003 `OutputViewModel` → encoder) explicitly bypasses it. Proxy generation reuses the existing `Beutl.FFmpegWorker` encoder via `FFmpegEncodingControllerProxy` — no new GPL coupling and no new IPC verbs. A new `ProxyStore` in `Beutl.Engine` persists a JSON-indexed cache of proxy files keyed on `(absolute path, file size, mtime)`, enforces a global LRU cap, and exposes a serial `ProxyJobQueue` for background generation. Project-level preview source mode (Proxy / Original) is persisted on `Scene`.

**Post-003 note**: this feature builds on the now-implemented resolution-independent pipeline (`003-resolution-independent-pipeline`). The load-bearing addition over the pre-003 design is a **logical-size decoupling seam** — a proxy is modeled as a lower-density supply with an *unchanged* logical footprint, so `SourceImage`/`SourceVideo` and `Image/VideoSourceRenderNode` pin the clip's bounds to the original `FrameSize` and report `EffectiveScale.At(supplyDensity)` (replacing 003's hard-coded `At(1)`). The `MediaOptions.PreferProxy` toggle and `DecoderRegistry` choke point from R-1 remain; the sizes ride a `ProxyResolution` side-channel. Details: research R-11, spec FR-021/FR-022/FR-023.

## Technical Context

**Language/Version**: C# (`LangVersion: preview`), .NET 10 dual-target (`net10.0` + `net10.0-windows`).

**Primary Dependencies**: `Beutl.Engine` (rendering, media abstractions), `Beutl.ProjectSystem` (project / Scene), `Beutl.Extensions.FFmpeg` (in-MIT IPC client to `Beutl.FFmpegWorker`), `Beutl.FFmpegIpc` (pipes + JSON + shared memory), Avalonia (UI), NUnit + Moq (tests).

**Storage**: On-disk proxy files (MP4/H.264) in a configurable proxy-store root; a single `index.json` per store root tracking entries with `(source path, size, mtime, preset, state, lastUsedUtc, proxyFileName, fileSize)`. Per-user default: `<app cache>/proxies/`. No database.

**Testing**: NUnit + Moq under `tests/Beutl.UnitTests` (store, queue, eviction, resolver, fingerprint) and `tests/Beutl.FFmpegIpc.Tests` (end-to-end proxy generation via IPC, conditionally). Headless UI smoke for the proxy tool tab if feasible; otherwise manual verification per quickstart.

**Target Platform**: Desktop (Avalonia) on Windows / macOS / Linux (matches existing Beutl matrix).

**Project Type**: Desktop application — extends existing `src/` project graph; no new top-level project.

**Performance Goals** (from spec SCs):
- ≥3× faster frame-decode in preview vs. no-proxy (SC-001)
- Project-mode toggle reflected on next frame within 2 s on ≤500-clip projects (SC-003)
- Proxy-backed preview holds project frame rate on a 3-layer 4K timeline that drops frames at original quality (SC-004)
- Eviction and store lookups must not block UI thread (lookup ≤1 ms p95 for hot path; eviction off the UI thread)

**Constraints**:
- **License firewall (NON-NEGOTIABLE)**: no MIT project may take a `ProjectReference` to `Beutl.FFmpegWorker`. Proxy generation MUST go through existing `FFmpegIpc` + `FFmpegEncodingControllerProxy`. The PreToolUse hook enforces this and must remain green.
- Dual-target build must stay green on both `net10.0` and `net10.0-windows`.
- Default proxy-store root must NOT live inside the project directory (project portability).
- LRU eviction must never delete a file that is currently being decoded for preview or whose generation job is in flight (FR-018a safety clause).
- Proxy generation must not block the UI thread; cancellation must leave no "complete-looking" partial file (FR-009, FR-012).

**Scale/Scope**:
- Target a typical 200–500 clip project with up to ~50 heavy (≥4K, ≥60 Mbps) source files.
- Proxy-store default cap: 50 GB global (initial default; user-configurable 5–500 GB).
- Concurrency: 1 active proxy generation job at MVP (serial queue).
- UI surface: one new project-level toggle (preview source mode) + one new tool tab ("Proxies") for queue / store / eviction visibility.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| I. License Firewall | MIT projects must not `ProjectReference` `Beutl.FFmpegWorker`; proxy generation goes through `Beutl.FFmpegIpc` only. | **PASS** — design reuses existing `FFmpegEncodingControllerProxy` (already IPC-only) and `FFmpegReaderProxy`. No new MIT → GPL edges. |
| II. Dual-Target Framework | Must keep `net10.0` and `net10.0-windows` building. | **PASS** — all new code lands in `Beutl.Engine` / `Beutl.ProjectSystem` / `Beutl.Editor*`, which already dual-target. No new TFM. |
| III. Test-First with NUnit | New logic ships with NUnit tests under `tests/`. | **PASS** — see [Phase 1 testing](#phase-1-testing). Tests target `tests/Beutl.UnitTests` (store/queue/resolver/fingerprint/eviction) and `tests/Beutl.FFmpegIpc.Tests` (generation end-to-end). |
| IV. Avalonia + Compiled Bindings | New `UserControl`s declare `x:CompileBindings="True"` + `x:DataType`. | **PASS** — the new Proxies tool tab follows the existing pattern; `beutl-xaml-binder` subagent will spot regressions. |
| V. Style Belongs to the Linter | No stylistic-only edits; rely on `dotnet format`. | **PASS** — no manual style changes planned. |
| VI. Source Generators Are Load-Bearing | Generator changes need build + `tests/SourceGeneratorTest/`. | **PASS** — feature does not change source generators. |

No gate violations to justify. **Complexity Tracking** section is intentionally omitted (no violations).

Post-Phase 1 re-check: PASS — no design decision in `research.md` / `data-model.md` / `contracts/` introduces a new GPL ↔ MIT edge, a new TFM, an untested public method, an `x:CompileBindings="False"` control, a manual style edit, or a source-generator change.

## Project Structure

### Documentation (this feature)

```text
docs/specs/002-proxy-media/
├── plan.md              # this file
├── spec.md              # /speckit-specify + /speckit-clarify output
├── research.md          # Phase 0 (this command)
├── data-model.md        # Phase 1 (this command)
├── quickstart.md        # Phase 1 (this command)
├── contracts/           # Phase 1 (this command)
│   ├── IProxyStore.md
│   ├── IProxyResolver.md
│   ├── IProxyJobQueue.md
│   └── proxy-index.schema.json
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
├── Beutl.Engine/
│   ├── Media/
│   │   ├── Source/                                    # existing — extended (PreferProxy plumbing)
│   │   │   ├── MediaSource.cs                         #   (touched: pass PreferProxy through Resource.Update)
│   │   │   └── VideoSource.cs                         #   (touched: ditto)
│   │   ├── Decoding/
│   │   │   ├── DecoderRegistry.cs                     #   (touched: consult IProxyResolver when MediaOptions.PreferProxy; hand ProxyResolution to the source layer)
│   │   │   └── MediaOptions.cs                        #   (touched: add bool PreferProxy = false — toggle only; sizes ride ProxyResolution)
│   │   └── Proxy/                                     # NEW namespace
│   │       ├── IProxyResolver.cs                      # interface: source → ProxyResolution? (path + original logical size + proxy decoded size)
│   │       ├── IProxyStore.cs                         # interface: lookup, register, evict, list, totals
│   │       ├── ProxyStore.cs                          # default impl, JSON-indexed
│   │       ├── ProxyStoreIndex.cs                     # in-memory index + serialization
│   │       ├── ProxyEntry.cs                          # record: fingerprint + preset + state + lastUsedUtc + file
│   │       ├── ProxyFingerprint.cs                    # struct: AbsolutePath + Size + MtimeUtc
│   │       ├── ProxyPreset.cs                         # enum/record: Half / Quarter (+ Eighth optional)
│   │       ├── ProxyPresetDefinitions.cs              # concrete H.264 params per preset
│   │       ├── ProxyState.cs                          # enum: None / Generating / Ready / Stale / Failed / Partial
│   │       ├── ProxyJob.cs                            # job descriptor + progress + cancel handle
│   │       ├── ProxyJobQueue.cs                       # serial queue (SemaphoreSlim+Channel)
│   │       ├── ProxyGenerationOrchestrator.cs         # decode source → re-encode via FFmpegEncodingControllerProxy
│   │       ├── ProxyEvictionService.cs                # global LRU under configurable cap
│   │       └── PreviewSourceMode.cs                   # enum: PreferProxy / ForceOriginal
│   └── Graphics/                                      # existing — extended (003 logical-size seam)
│       ├── SourceImage.cs                             #   (touched: logical size = OriginalLogicalFrameSize when proxied, else FrameSize)
│       ├── SourceVideo.cs                             #   (touched: ditto)
│       └── Rendering/
│           ├── ImageSourceRenderNode.cs               #   (touched: Bounds from original logical size; EffectiveScale.At(supplyDensity); dest-rect draw)
│           └── VideoSourceRenderNode.cs               #   (touched: ditto — replaces hard-coded EffectiveScale.At(1))
├── Beutl.ProjectSystem/
│   └── ProjectSystem/
│       ├── Scene.cs                                   #   (touched: add PreviewSourceMode property; persist)
│       ├── SceneRenderer.cs                           #   (touched: set MediaOptions.PreferProxy from Scene.PreviewSourceMode)
│       └── SceneComposer.cs                           #   (touched: ALWAYS set MediaOptions.PreferProxy = false)
├── Beutl.Configuration/
│   └── ProxyStoreConfig.cs                            # NEW: store root path, LRU cap, default preset
└── Beutl.Editor*/                                     # UI (new tool tab + toggle)
    ├── ToolTabs/Proxies/
    │   ├── ProxiesToolTab.axaml                       # NEW UserControl
    │   ├── ProxiesToolTab.axaml.cs
    │   └── ProxiesToolTabViewModel.cs
    └── (existing Scene settings UI)                   #   (touched: surface PreviewSourceMode toggle)

tests/
├── Beutl.UnitTests/
│   └── Media/Proxy/
│       ├── ProxyFingerprintTests.cs                   # NEW — staleness detection
│       ├── ProxyStoreTests.cs                         # NEW — index load/save/round-trip, partial-file recovery
│       ├── ProxyResolverTests.cs                      # NEW — preview routing, fallback to original
│       ├── ProxyJobQueueTests.cs                      # NEW — serial dispatch, cancel, individual completion
│       └── ProxyEvictionTests.cs                      # NEW — LRU, safety clause (in-flight skip)
└── Beutl.FFmpegIpc.Tests/
    └── ProxyGenerationE2ETests.cs                     # NEW — end-to-end generate via IPC (gated on FFmpeg availability)
```

**Structure Decision**: extend existing projects rather than introducing a new one. The new namespace `Beutl.Media.Proxy` lives inside `Beutl.Engine` so the proxy store + resolver + queue have direct access to the existing media abstractions (`MediaSource`, `MediaReader`, `DecoderRegistry`, `MediaOptions`). Project-level state goes on `Scene` in `Beutl.ProjectSystem`. The Avalonia UI bits go in the existing `Beutl.Editor*` projects following the existing tool-tab pattern. This keeps the feature inside Beutl's existing project graph and avoids new top-level projects — favoring orthogonality without churning the boundary map.

## Phase 0 → Phase 2 outputs

Phase 0 research and Phase 1 contracts/data-model/quickstart are written to companion files in this directory:

- [research.md](./research.md) — decisions, rationale, alternatives, all spec-level deferrals resolved
- [data-model.md](./data-model.md) — `ProxyEntry`, `ProxyFingerprint`, `ProxyPreset`, `ProxyState`, `ProxyJob`, `PreviewSourceMode`, `ProxyStoreConfig`, persistence model
- [contracts/](./contracts/) — `IProxyStore`, `IProxyResolver`, `IProxyJobQueue`, and the JSON schema for the on-disk proxy index
- [quickstart.md](./quickstart.md) — developer/manual-test walkthrough end-to-end

Phase 2 (`tasks.md`) is **not** produced by `/speckit-plan`. Run `/speckit-tasks` next.

## Phase 1 testing

NUnit + Moq across two test projects:

- **`tests/Beutl.UnitTests/Media/Proxy/`** — fast unit tests for `ProxyFingerprint` (path/size/mtime triple match + mismatch), `ProxyStore` (load/save round-trip, partial-file recognition, index corruption recovery), `ProxyResolver` (returns proxy when ready, returns null for missing/stale/partial — caller falls back), `ProxyJobQueue` (serial ordering, cancel mid-job leaves no "complete" file, individual completion not batch), `ProxyEvictionService` (LRU correctness, in-flight skip, no-evict-while-reading guard).
- **`tests/Beutl.FFmpegIpc.Tests/ProxyGenerationE2ETests.cs`** — drives a real `FFmpegEncodingControllerProxy` end-to-end on a small synthetic source, asserts the produced proxy is decodable and its fingerprint metadata matches the source. Gated on `FFmpeg` availability (skip if `FFmpegInstallService` reports missing) — same pattern as existing FFmpeg-dependent tests.
- **Headless / smoke for the Proxies tool tab** — if `Avalonia.Headless` covers our tab usage, add a smoke test; otherwise document a manual verification step in `quickstart.md`.

All preview-vs-export routing (the heart of the feature) is exercised through `ProxyResolverTests` and integration assertions in `ProxyGenerationE2ETests` (export pass MUST resolve to original even when proxy is fresh).

## Risks & mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **Opening a smaller proxy file shrinks the source's logical footprint under 003** (003 derives logical size from decoded `FrameSize`; render nodes hard-code `EffectiveScale.At(1)`) → the clip moves/resizes on canvas | **High** (the naive file-swap does exactly this) | **High** (visibly broken preview; defeats the feature) | Deliver the logical-size seam (FR-021/FR-022, tasks T062–T065, research R-11): carry `OriginalLogicalFrameSize` + `ProxyDecodedFrameSize` on `ProxyResolution`; pin render-node `Bounds` to the original `FrameSize`; report `EffectiveScale.At(supplyDensity)`; draw into the original-footprint dest rect. On the original path the ratio is `1.0` ⇒ byte-identical to 003. Verified by quickstart step 4a. |
| `SceneRenderer` and the export path share a media-resolution helper, breaking the "preview opts in, export bypasses" assumption | Low (separate files in repo) | High (whole design hinges on this) | Phase 0 includes a verification step that audits **all** `OpenMediaFile` / `MediaOptions` call sites — including the 003 export path (`OutputViewModel` / `FrameProviderImpl`), not just the pre-003 `SceneComposer`. If they share a helper, the helper takes `PreferProxy` as a parameter and both call sites set it explicitly (export always `false`). Confirmed distinct by 003 (export goes through `OutputViewModel`). |
| LRU eviction races with preview decode | Medium | Medium (could yank a file out from under the reader) | `ProxyEvictionService` consults an in-memory "pinned" set of proxy paths that `ProxyResolver` populates while a `MediaReader` is open. Eviction skips pinned entries (FR-018a safety clause). Tested in `ProxyEvictionTests`. |
| Partial proxy file from crash mistakenly served as ready | Medium | High (silent wrong output in preview) | `ProxyStore` writes to `*.proxy.tmp` first, fsyncs, then renames to the canonical name and only then updates `index.json`. Boot-time scan finds dangling `.tmp` files and marks corresponding entries `Partial`. Tested in `ProxyStoreTests`. |
| FFmpeg-not-installed surface | Medium | Medium (proxy generation silently fails) | Reuse existing `FFmpegInstallNotifier`; surface install prompt the first time a generation job is queued without FFmpeg. |
| Index file corruption (concurrent writes, partial fsync, manual edits) | Low | Medium (lost cache, not lost data) | Index is rebuildable by scanning the directory; on parse failure, the store falls back to a directory scan and emits a warning. Source of truth on conflict: filesystem. |

## Open items deferred to `/speckit-tasks`

- Exact UI placement for the per-clip proxy-state indicator (timeline strip vs. project panel vs. tool tab badge). Spec FR-015 already accepts "choice deferred to design"; the Proxies tool tab plus a small icon on the timeline clip is the leading candidate.
- Concrete numbers in `ProxyPresetDefinitions.cs` (bitrate per resolution; `crf` vs `b:v` mode) — captured in `research.md` but final tuning belongs in implementation.
- Whether the third (intermediate) preset ships in MVP — `research.md` records "include if cheap, drop if it slips the schedule".
