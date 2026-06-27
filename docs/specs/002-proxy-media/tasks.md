---

description: "Implementation tasks for the Proxy Media Workflow feature"
---

# Tasks: Proxy Media Workflow

**Input**: Design documents from `docs/specs/002-proxy-media/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, and the **implemented** sibling spec `docs/specs/003-resolution-independent-pipeline/` (the resolution-independent pipeline this feature builds on — its supply-driven scale model, `EffectiveScale`, and the source logical-size seam are load-bearing here; see spec FR-021/FR-022 and research R-11).

**Tests**: REQUIRED for every implementation task — Beutl constitution principle III ("Test-First with NUnit") mandates NUnit tests for any new logic in `src/`. Each user story phase below includes its tests before the corresponding implementation.

**Organization**: Tasks are grouped by user story. The constitution forbids GPL ↔ MIT coupling, so every task that touches media generation goes through `Beutl.FFmpegIpc` only — never via `ProjectReference` to `Beutl.FFmpegWorker`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1 / US2 / US3); setup, foundational, and polish tasks have no story label
- Include exact file paths in descriptions

## Path Conventions

- New core code: `src/Beutl.Engine/Media/Proxy/` (store/resolver/queue/value types plus `IProxyGenerator` abstraction), `src/Beutl.Configuration/`, `src/Beutl.Editor*/ToolTabs/Proxies/`. New FFmpeg concrete generation code: `src/Beutl.Extensions.FFmpeg/Proxy/` (uses `FFmpegEncodingControllerProxy`; Engine must not reference this project). Edits to existing ProjectSystem files: `Scene.cs` is nested at `src/Beutl.ProjectSystem/ProjectSystem/Scene.cs`, but `SceneRenderer.cs` and `SceneCompositor.cs` are at the **top level** `src/Beutl.ProjectSystem/` (not nested) — verify each path before editing.
- New tests: `tests/Beutl.UnitTests/Media/Proxy/`, `tests/Beutl.UnitTests/Extensions/FFmpeg/`
- See `plan.md` "Project Structure" for the full layout

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Carve out the `Beutl.Media.Proxy` namespace and confirm the workspace is clean.

- [ ] T001 Create folder `src/Beutl.Engine/Media/Proxy/` (empty placeholder file to add it to the project; replace with real types in later phases)
- [ ] T002 Create folder `tests/Beutl.UnitTests/Media/Proxy/` (matching test namespace)
- [ ] T003 Run `/beutl-build` to confirm the new folders compile clean before any code is added
- [ ] T004 [P] Skim `src/Beutl.Engine/Media/Source/VideoSource.cs` and `src/Beutl.Engine/Media/Decoding/DecoderRegistry.cs` / `MediaOptions.cs` end-to-end and note any uses of `MediaOptions` that will need the new `PreferProxy` flag (planning input for T013). **Also (post-003)** skim `src/Beutl.Engine/Composition/CompositionContext.cs`, `src/Beutl.ProjectSystem/SceneCompositor.cs`, `src/Beutl.Engine/Graphics/SourceVideo.cs`, and `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs` — note how logical size is derived from decoded `FrameSize` (`r.Source.FrameSize.ToSize(1)`) and how `EffectiveScale.At(1f)` is hard-coded; these are the video seam sites for the logical-size decoupling (T062–T065). Confirm `src/Beutl.Engine/Graphics/Rendering/EffectiveScale.cs` exists (003) and read its `At`/`Unbounded` API. Still images stay out of MVP scope because `ImageSource` bypasses `DecoderRegistry.OpenMediaFile`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the value types, enums, the `MediaOptions.PreferProxy` plumbing, and the `IProxyStore` + `IProxyResolver` + `IProxyJobQueue` + `IProxyGenerator` skeletons. **No user story can start until this phase is complete.**

**⚠️ CRITICAL**: every task here is groundwork for all three user stories. Do not skip.

### Foundational tests (NUnit) — write first, ensure they fail

- [ ] T005 [P] Author failing NUnit fixture `tests/Beutl.UnitTests/Media/Proxy/ProxyFingerprintTests.cs` covering `(path,size,mtime)` equality, mismatch on each component, and `TryFromFile` non-throwing on missing files (per data-model.md `ProxyFingerprint` invariants)
- [ ] T006 [P] Author failing NUnit fixture `tests/Beutl.UnitTests/Media/Proxy/ProxyEntryStateTransitionsTests.cs` enumerating the legal state transitions in data-model.md (`None → Generating → Ready`, `Ready → Stale`, etc.) and rejecting illegal ones

### Foundational types (no behavior yet — pure data)

- [ ] T007 [P] Implement `src/Beutl.Engine/Media/Proxy/ProxyFingerprint.cs` (readonly record struct, `FromFile` / `TryFromFile`, OS path normalization per research R-4). Symlinks MUST be resolved at fingerprint time (e.g. `File.ResolveLinkTarget(_, returnFinalTarget: true)`) and the resolved real path stored in `AbsolutePath`, so a project moved between drives via a stable symlink keeps a consistent fingerprint
- [ ] T008 [P] Implement `src/Beutl.Engine/Media/Proxy/ProxyPreset.cs` (enum `Half | Quarter | Eighth`)
- [ ] T009 [P] Implement `src/Beutl.Engine/Media/Proxy/ProxyState.cs` (enum `None | Generating | Ready | Stale | Failed | Partial`)
- [ ] T010 [P] Implement `src/Beutl.Engine/Media/Proxy/PreviewSourceMode.cs` (enum `PreferProxy | ForceOriginal`)
- [ ] T011 [P] Implement `src/Beutl.Engine/Media/Proxy/ProxyEntry.cs` (record with composite identity `(Source, Preset)`, invariants from data-model.md)
- [ ] T012 [P] Implement `src/Beutl.Engine/Media/Proxy/ProxyJob.cs` + `ProxyJobProgress` + `ProxyJobStatus` per data-model.md (no queue logic yet). `ProxyJobStatus` MUST include `Skipped` for media types that are ineligible for proxy generation (see FR-020 / T041)

### Foundational plumbing — `MediaOptions.PreferProxy`

- [ ] T013 Modify `src/Beutl.Engine/Media/Decoding/MediaOptions.cs` to add `bool PreferProxy = false` (default) per research R-1 / data-model.md "Touched types"
- [ ] T014 Update every caller of `MediaOptions` outside this feature (codec discovery, etc.) to remain on the default `false` — confirm via grep that the change is source-compatible. If any helper builds `MediaOptions` and is used by both preview and export, refactor it to take `PreferProxy` as an explicit parameter (per plan.md risk table — verification step for R-1)
- [ ] T015 Add NUnit test `tests/Beutl.UnitTests/Media/Decoding/MediaOptionsTests.cs` (or extend existing) confirming the new field defaults to `false` and round-trips through any serialization

### Foundational store + resolver + queue skeletons (interfaces, no implementation yet)

- [ ] T016 [P] Implement `src/Beutl.Engine/Media/Proxy/IProxyStore.cs` matching `contracts/IProxyStore.md` exactly (interface + `ProxyStoreChangedEventArgs` + `ProxyStoreChangeKind`)
- [ ] T017 [P] Implement `src/Beutl.Engine/Media/Proxy/IProxyResolver.cs` matching `contracts/IProxyResolver.md` (`Resolve` + `Pin` + `ProxyResolution`). **`ProxyResolution` carries `OriginalLogicalFrameSize` + `ProxyDecodedFrameSize`** (and derived `SupplyDensity`) — these are REQUIRED for the 003 logical-size seam (T062–T065); a skeleton that omits them will fail the T062 tests.
- [ ] T018 [P] Implement `src/Beutl.Engine/Media/Proxy/IProxyJobQueue.cs` matching `contracts/IProxyJobQueue.md` (`EnqueueAsync` + `Pending` + `Cancel*` + `JobChanged` event + `ProxyJobChangeKind`) and `src/Beutl.Engine/Media/Proxy/IProxyGenerator.cs` (Engine abstraction consumed by the queue; concrete FFmpeg implementation comes later in `Beutl.Extensions.FFmpeg`)

### Foundational config

- [ ] T019 Implement `src/Beutl.Configuration/ProxyStoreConfig.cs` (default `StoreRootPath` = `<app cache>/Beutl/proxies`, default `MaxTotalBytes` = 50 GB, default `DefaultPreset` = `Quarter`, validation clamp `[5 GB, 500 GB]`)
- [ ] T020 NUnit test `tests/Beutl.UnitTests/Configuration/ProxyStoreConfigTests.cs` covering defaults, clamping, and `StoreRootPath` resolution per platform

### Verification step

- [ ] T021 Run `/beutl-build` + targeted `dotnet test --filter "FullyQualifiedName~Media.Proxy"` and confirm: foundational tests (T005, T006) pass against the new types; the `MediaOptions` change has not broken any existing test

**Checkpoint**: enums, records, interfaces, `MediaOptions.PreferProxy`, and config are in place. User stories can now begin in parallel.

---

## Phase 3: User Story 1 — Edit with proxy, export with original (Priority: P1) 🎯 MVP

**Goal**: Preview decode is routed through proxies when available; export decode is unconditionally routed through originals. This is the spec's headline value.

**Independent Test**: with a `ProxyEntry` manually inserted into the store (or a real one if US2 is also done), scrub the timeline and confirm via logging/overlay that the proxy file is opened; trigger an export and confirm the export opens the original. Both verifiable through the existing decoder logs without UI.

### Tests for US1 (NUnit) — write first, ensure they fail

- [ ] T022 [P] [US1] Create `tests/Beutl.UnitTests/Media/Proxy/ProxyResolverTests.cs` covering: (a) `Resolve` returns null when store has no entry, (b) returns a `ProxyResolution` when a `Ready` entry exists, (c) cross-preset fallback (only `Half` available, request `Quarter` → returns `Half`), (d) `Touch` is called exactly once per successful `Resolve`, (e) `Pin` lifecycle reference-counts correctly
- [ ] T023 [P] [US1] Create `tests/Beutl.UnitTests/Media/Proxy/DecoderRegistryProxyRoutingTests.cs` covering: with `MediaOptions.PreferProxy = true` and a `Ready` proxy registered, `OpenMediaFile` opens the proxy path; with `PreferProxy = true` and no proxy, opens the original; with `PreferProxy = false`, opens the original even when a proxy is registered (the export-safety case)
- [ ] T024 [P] [US1] Create `tests/Beutl.UnitTests/ProjectSystem/ExportRenderContextSafetyTests.cs` asserting that the **export** render context never carries `PreferProxy == true` (use a test seam — see T030). **Post-003**: 003 routes export through `OutputViewModel` (builds `SceneRenderer(Model, renderScale, disableResourceShare: true, maxWorkingScale)`; downscales in `FrameProviderImpl.RenderCore`). Audit/grep the sole video `MediaOptions` construction site (`VideoSource.Resource.Update`) and assert the export context keeps `PreferProxy = false`.
- [ ] T025 [P] [US1] Create `tests/Beutl.UnitTests/ProjectSystem/ExportMissingSourceTests.cs` covering FR-004 explicitly: (a) when the original source file is deleted and an export is triggered, the export fails with a clear, surfaced error (asserted via the existing error-surface mechanism — exception type / event / log entry depending on what the export path already uses); (b) the export MUST NOT silently substitute a `Ready` proxy even when one exists in the store. Use the same context test seam as T024 to construct an in-test export run against a missing source

### Implementation for US1

- [ ] T026 [US1] Implement `src/Beutl.Engine/Media/Proxy/ProxyStore.cs` minimally enough to satisfy resolver tests: in-memory map + read/write `index.json` via `System.Text.Json` + atomic temp-then-rename. Defer LRU and reconcile to US2 (T039)
- [ ] T027 [P] [US1] Implement `src/Beutl.Engine/Media/Proxy/ProxyStoreIndex.cs` — serialization shape matches `contracts/proxy-index.schema.json`, including `originalLogicalFrameSize` / `proxyDecodedFrameSize` and the `$defs.proxySourceMetadata` sidecar shape (validate manually by writing a sample `index.json` and `meta.json` and running both through a JSON schema validator one time)
- [ ] T028 [US1] Implement `src/Beutl.Engine/Media/Proxy/ProxyResolver.cs` (concrete `IProxyResolver`): resolution policy from research R-1 (priority: exact-preset Ready → other-preset Ready → null); pin set as a `ConcurrentDictionary<string, int>` reference counter; `Touch` on every successful resolve. Verify tests from T022 pass
- [ ] T029 [US1] Wire `IProxyResolver` into `src/Beutl.Engine/Media/Decoding/DecoderRegistry.cs`: when `MediaOptions.PreferProxy == true`, consult the resolver before falling through to the original-source decoder chain. The resolver is acquired from a service locator (or DI) that defaults to null in test contexts (resolver absence = always original). **The resolved `ProxyResolution` (path + `OriginalLogicalFrameSize` + `ProxyDecodedFrameSize`) MUST be handed to the source layer so T063/T064 can pin the logical footprint and report supply density** — opening the proxy file alone is not enough (it would shrink the decoded `FrameSize` and move the content). Verify T023 passes
- [ ] T030 [US1] On the **export** path, ensure the export render context never carries `PreferProxy = true`. The sole video `MediaOptions` construction site is `VideoSource.Resource.Update` (`new(MediaMode.Video)` at `src/Beutl.Engine/Media/Source/VideoSource.cs`), which reads `context.PreferProxy`; the export `SceneRenderer` is built by `OutputViewModel` (`new SceneRenderer(Model, renderScale, disableResourceShare: true, maxWorkingScale)` — `src/Beutl/ViewModels/Tools/OutputViewModel.cs`) and its `SceneCompositor` MUST NOT seed `PreferProxy` from `Scene.PreviewSourceMode`, so the context default `false` holds on every export open (defense-in-depth for FR-002/FR-004). Add a small `internal` test seam (e.g., a way to inspect the export render context's `PreferProxy`) so T024 and T025 can assert this without reflection. Ensure that an open call against a missing source surfaces the existing decoder-failure path *unchanged* (T025 (a)) — do not introduce a silent fallback
- [ ] T031 [US1] Thread `PreferProxy` through the render **context**, not `SceneRenderer`. `MediaOptions` is constructed inside `VideoSource.Resource.Update` (`new(MediaMode.Video)` at `src/Beutl.Engine/Media/Source/VideoSource.cs`), which already reads `CompositionContext.DisableResourceShare` (`src/Beutl.Engine/Composition/CompositionContext.cs`); add a sibling `PreferProxy` init-property on `CompositionContext` and have `SceneCompositor` (`src/Beutl.ProjectSystem/SceneCompositor.cs`) seed it from `Scene.PreviewSourceMode` into its `CompositorContext` (real wiring lands in US3; for now the preview compositor seeds `true` since the spec default is `PreferProxy`). `VideoSource.Resource.Update` then builds `new(MediaMode.Video) { PreferProxy = context.PreferProxy }`. Do NOT add a renderer rebuild for the proxy toggle — `SceneRenderer`'s ctor is `(Scene scene, float renderScale = 1f, bool disableResourceShare = false, float maxWorkingScale = +∞)` (003 breaking change) and the renderer is rebuilt by-replacement off `(FrameSize, OutputScale)` (003 FR-031); toggling `PreviewSourceMode` changes only per-source supply density (FR-023), so invalidate the affected sources' render-cache entries and re-queue a render, reading `PreferProxy` fresh inside the render work-item (as 003 reads the renderer fresh). Add NUnit coverage in `tests/Beutl.UnitTests/ProjectSystem/SceneRendererPreviewRoutingTests.cs`
- [ ] T032 [US1] Pin lifecycle integration: acquire `IProxyResolver.Pin(resolution)` **immediately after `IProxyResolver.Resolve` returns a non-null `ProxyResolution` and BEFORE the proxy decoder is opened** — do NOT wait until after `OpenMediaFile` returns, or there is a resolve→open→pin window in which `ProxyEvictionService` could delete the proxy file out from under the reader. Tie the pin handle's lifetime to the returned `MediaReader` (release on `MediaReader` dispose). This satisfies the `IProxyResolver` behavior contract (pin before open) and protects against eviction (US2 consumes the pin set). Cover with NUnit in the resolver test fixture (T022)

### Manual smoke

- [ ] T033 [US1] Manually drop a pre-encoded `quarter.mp4` plus `meta.json` under `<store-root>/<hash>/` matching a fingerprint of a real `$SRC_SMALL` clip; open the project; confirm via logs that preview decodes the proxy and export decodes the original (matches quickstart.md steps 4 + 8 partial scope)

### 003 integration — logical-size decoupling seam (the seam 003 deferred)

**Purpose**: under the 003 pipeline, simply opening a smaller proxy file shrinks the source's decoded `FrameSize` and therefore its logical footprint — the clip would move/resize on canvas. These tasks deliver the stable logical-size channel 003 explicitly left to the proxy feature (spec FR-021/FR-022; `003/data-model.md` "003 scope note"). **US1's headline guarantee is not actually correct without them** (preview would show proxies at the wrong size), so they are part of US1, not a follow-up.

- [ ] T062 [P] [US1] Author failing NUnit fixture `tests/Beutl.UnitTests/Graphics/ProxyVideoLogicalSizeTests.cs`: given a video source backed by a half-resolution proxy, the render-node `Bounds` equals the **original** `FrameSize` (not the proxy's), the op reports `EffectiveScale.At(0.5)` for controlled even dimensions (or the computed `SupplyDensity` with tolerance when clamps/rounding apply), and the decoded proxy bitmap is drawn into the original-footprint destination rect. Also assert the original-path case (no proxy): `Bounds` == decoded `FrameSize`, `EffectiveScale.At(1)` — byte-identical to 003. (Build a GPU-free probe node per the NodeGraph FilterEffect GPU-free test pattern, or assert at the render-node/operation level.)
- [ ] T063 [US1] Decouple video logical size from decoded size in `src/Beutl.Engine/Graphics/SourceVideo.cs` (`:139`): thread the resolved `ProxyResolution.OriginalLogicalFrameSize` (from T029) into the source / `.Resource` as the logical size, falling back to `r.Source.FrameSize.ToSize(1)` on the original path (no proxy) so behavior is unchanged there. The proxy's decoded `FrameSize` is used only to compute supply density, never as the logical footprint. Do not touch the still-image source path for the MVP; still images are skipped per FR-020.
- [ ] T064 [US1] Update `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs`: (a) `Bounds` from the **original** logical size (FR-021); (b) `effectiveScale: EffectiveScale.At(resolution.SupplyDensity)` instead of the hard-coded `EffectiveScale.At(1f)` (FR-022); (c) draw the decoded proxy bitmap scaled into the original-footprint dest rect (the 003 FR-024 dest-rect seam — use the dest-rect draw path, not the native 1:1 `DrawBitmap` blit). On the original path all three collapse to today's behavior (`At(1)`, native blit). Verify T062 passes
- [ ] T065 [US1] Cache invalidation on supply-density change (FR-023): when a source's proxy becomes ready / stale / deleted, or `PreviewSourceMode` toggles, invalidate that source's render-cache entries (003 FR-020/FR-032) so no stale-density tile is blitted, and re-queue a render. Add NUnit coverage extending `SceneRendererPreviewRoutingTests` (T031) to assert the affected source re-renders on the next frame without a `SceneRenderer` rebuild

**Checkpoint**: with a pre-seeded proxy on disk, US1 demonstrates the headline guarantee (preview = proxy, export = original) end-to-end — **and the proxy occupies the same on-canvas footprint as the original** (T062–T065). The user can't *create* proxies yet — that's US2.

---

## Phase 4: User Story 2 — Generate / regenerate / remove proxies (Priority: P2)

**Goal**: User-initiated proxy generation with background processing, progress, staleness handling, LRU eviction, and the Proxies tool tab.

**Independent Test**: select a clip in the Proxies tab, click Generate, see progress, see `Ready` state on completion; delete and confirm file removal; touch the source externally and confirm `Stale` on next open; fill the cache and confirm LRU eviction with a non-blocking notification.

### Tests for US2 (NUnit) — write first, ensure they fail

- [ ] T034 [P] [US2] Create `tests/Beutl.UnitTests/Media/Proxy/ProxyStoreTests.cs` covering: register → lookup round-trip, flush + restart preserves entries, `index.json` corruption triggers directory rescan, partial `.tmp` files mark entries `Partial` and are not served as `Ready`, concurrent registers for distinct keys both succeed
- [ ] T035 [P] [US2] Create `tests/Beutl.UnitTests/Media/Proxy/ProxyJobQueueTests.cs` covering: serial dispatch in arrival order, async `EnqueueAsync` back-pressure when the bounded channel is full, dedup of `(source, preset)` enqueues, `Cancel` removes `.tmp` and ends in `Canceled`, `CancelAll` empties the queue, `MaxConcurrency == 1` invariant under stress, `JobChanged` event ordering, generator-missing path, ineligible-media path (`Skipped` terminal state per T041)
- [ ] T036 [P] [US2] Create `tests/Beutl.UnitTests/Media/Proxy/ProxyEvictionTests.cs` covering: LRU correctness, in-flight skip (`Generating` entries never evicted), pinned-skip (resolver pin set is consulted), notification fired on eviction (via the same `INotificationService` chosen in T042), eviction stays under `MaxTotalBytes` after each sweep
- [ ] T037 [P] [US2] Create `tests/Beutl.UnitTests/Extensions/FFmpeg/ProxyGenerationE2ETests.cs` — drives the concrete `FFmpegProxyGenerator` / `FFmpegEncodingControllerProxy` on a small synthetic source, asserts the produced proxy is decodable + fingerprint matches + `meta.json` sidecar exists with original/proxy frame sizes. Gate on FFmpeg availability with the existing worker-startup / install-notifier pattern available through `Beutl.Extensions.FFmpeg`

### Implementation for US2

- [ ] T038 [US2] Extend `src/Beutl.Engine/Media/Proxy/ProxyStore.cs` with `ReconcileAsync` (boot-time scan): validate every entry against disk, drop missing files, adopt orphan `meta.json` sidecars, delete orphan `*.tmp` files. Verify against T034
- [ ] T039 [P] [US2] Implement `src/Beutl.Engine/Media/Proxy/ProxyJobQueue.cs` as a single-consumer `Channel<ProxyJob>` (bounded 256) with `EnqueueAsync` back-pressure and a drain loop bound to `MaxConcurrency = 1` (parametric per research R-7). The queue invokes only the Engine-side `IProxyGenerator` abstraction; it must not reference `Beutl.Extensions.FFmpeg`, `Beutl.FFmpegIpc`, `FFmpegEncodingControllerProxy`, `FFmpegInstallService`, or `FFmpegInstallNotifier`. Verify against T035
- [ ] T040 [US2] Implement `src/Beutl.Engine/Media/Proxy/ProxyPresetDefinitions.cs` — table `ProxyPreset → ProxyEncodeParameters` with starting values from research R-5 (`Half`: 1/2 scale, CRF 25; `Quarter`: 1/4 scale, CRF 26 (default); `Eighth`: 1/8 scale, CRF 28). Long-edge clamps included. `tune=fastdecode preset=fast` baked in. **Post-003**: the preset's scale factor is the proxy's **nominal** supply density, but `ProxyResolution.SupplyDensity` (FR-022) is always computed from the **actual** `ProxyDecodedFrameSize / OriginalLogicalFrameSize` — the long-edge clamps (e.g. `Quarter` caps the long edge at 1280 px) and integer `PixelSize` rounding mean an 8K source's `Quarter` proxy realizes density ≈ `0.167`, not the nominal `0.25`. The preset vocabulary is intentionally aligned with the 003 preview render-scale vocabulary (`Half`/`Quarter`). Document the interaction in a code comment: under 003's `w = min(max(s_out, densest supply), MaxWorkingScale)` (ceiling `2 × s_out` preview / `+∞` export; inert for sub-output proxies), a proxy denser than the active preview scale still saves decode cost but the effect pass runs at the proxy density (not the cheaper preview scale); a proxy at/below the preview scale lowers `w` fully (to the `s_out` floor). (See spec FR-017.)
- [ ] T041 [US2] Implement `src/Beutl.Extensions.FFmpeg/Proxy/FFmpegProxyGenerator.cs` as the concrete `IProxyGenerator`: opens source via `DecoderRegistry.OpenMediaFile(_, PreferProxy=false)`; instantiates `FFmpegEncodingControllerProxy` with the chosen preset; pumps frames through; on success rename `*.tmp` → final, write `meta.json` sidecar, call `IProxyStore.Register`; on failure/cancel delete `*.tmp`. Audio is dropped (FR-020). Ineligible-media gating (per FR-020): if the source is audio-only, procedural / generative (no underlying file URI), or a **still image** (out of MVP scope — still images decode once via `Bitmap.FromStream` and bypass `DecoderRegistry`, so a proxy yields no preview benefit and the routing / supply-density seams do not cover them; see spec Assumptions), the generator MUST NOT start encoding — it terminates the job in `Skipped` state with a human-readable reason, and the queue moves on to the next job
- [ ] T042 [US2] Implement `src/Beutl.Engine/Media/Proxy/ProxyEvictionService.cs` — global LRU sweep under `MaxTotalBytes`; consults `IProxyResolver` pin set; consults `IProxyStore` for `Generating` state; fires the FR-018b notification via the existing Beutl notification service (`INotificationService` in `Beutl.Core` / the equivalent that `Beutl.Editor` already uses for non-blocking toasts — pick the one the rest of the editor uses for "background job finished" messages, to keep the UX consistent). Run on every successful generation and at startup. Verify against T036
- [ ] T043 [US2] Hook FFmpeg availability into `FFmpegProxyGenerator` per research R-10: when a job would start without FFmpeg, surface `FFmpegInstallNotifier.NotifyMissing()` from `Beutl.Extensions.FFmpeg`, return a dependency-missing failure to `ProxyJobQueue`, and let the queue pause draining until the generator reports availability again. Keep all FFmpeg-specific types out of `Beutl.Engine`
- [ ] T044 [US2] Wire DI in the application composition root (`src/Beutl/` — NOT inside the `Beutl.Engine` library, which is a passive dependency): register the concrete `ProxyStore`, `ProxyResolver`, `ProxyJobQueue`, `ProxyEvictionService`, `ProxyStoreConfig`, and `Beutl.Extensions.FFmpeg.Proxy.FFmpegProxyGenerator` (`IProxyGenerator`) against their interfaces. Ensure they are constructed once per app, not per `Scene`

### UI for US2

- [ ] T045 [US2] Create `src/Beutl.Editor*/ToolTabs/Proxies/ProxiesToolTab.axaml` + `.axaml.cs` + `ProxiesToolTabViewModel.cs`. Required:
   - `x:CompileBindings="True"` + `x:DataType="vm:ProxiesToolTabViewModel"` (constitution principle IV)
   - Clip list with state badge per clip (None / Generating / Ready / Stale / Failed / Skipped)
   - Preset selector (defaults to `Quarter`)
   - Buttons: Generate selection / Regenerate / Delete / Delete-all-for-project
   - Pending-jobs list with progress + cancel
   - Store totals (project / global) and cap usage
- [ ] T046 [US2] Register the tool tab via the existing `ToolTabExtension` mechanism (follow `beutl-tooltab-extension` skill guidance). Reference the existing tool tab pattern for placement / lifecycle
- [ ] T047 [US2] (Headless smoke if feasible) Add `tests/Beutl.UnitTests/Editor/ToolTabs/ProxiesToolTabSmokeTests.cs` using `Avalonia.Headless` if the project already wires it; otherwise mark this task as a manual verification step in quickstart.md (FR-015 visibility check)

**Checkpoint**: User can generate, regenerate, and delete proxies through the Proxies tool tab; LRU eviction enforces the cap; FFmpeg-missing flows cleanly to the install prompt. With US1 + US2 done, the headline workflow works end-to-end from a clean install.

---

## Phase 5: User Story 3 — Toggle preview source (Priority: P3)

**Goal**: Project-level toggle to force preview to use originals instead of proxies, without affecting export.

**Independent Test**: with `Ready` proxies, set preview source = Original and confirm preview decodes from originals; export still uses originals (no change); set back to Proxy and confirm proxy is used again. Falls back to original for clips whose proxies are missing even with global toggle = Proxy.

### Tests for US3 (NUnit) — write first, ensure they fail

- [ ] T048 [P] [US3] Create `tests/Beutl.UnitTests/ProjectSystem/SceneSerializationPreviewSourceModeTests.cs` covering: default `PreferProxy` for a freshly-created scene, JSON round-trip preserves the value, projects saved before this feature land deserialized as `PreferProxy` (backward compatibility)
- [ ] T049 [P] [US3] Extend `tests/Beutl.UnitTests/ProjectSystem/SceneRendererPreviewRoutingTests.cs` (created in T031): switching `Scene.PreviewSourceMode` to `ForceOriginal` causes subsequent `MediaOptions.PreferProxy` to be `false`; flipping back restores `true`; export path (T024) is unaffected in both states

### Implementation for US3

- [ ] T050 [US3] Modify `src/Beutl.ProjectSystem/ProjectSystem/Scene.cs` to add `public PreviewSourceMode PreviewSourceMode { get; set; } = PreviewSourceMode.PreferProxy;` with JSON serialization (default omitted for backward compatibility on read)
- [ ] T051 [US3] Update `SceneCompositor` (`src/Beutl.ProjectSystem/SceneCompositor.cs`) to seed the render context's `PreferProxy` from `Scene.PreviewSourceMode` (replacing the constant-`true` seeded in T031) — `VideoSource.Resource.Update` reads it when constructing `MediaOptions`. Verify against T049
- [ ] T052 [US3] Add the toggle UI surface to existing Scene/Project settings panel (radio: Proxy / Original). UserControl must declare `x:CompileBindings="True"` + `x:DataType`. Bind two-way to `Scene.PreviewSourceMode` via the existing settings view model

**Checkpoint**: all three user stories complete. The full Proxy Media Workflow is functional and matches the spec's acceptance scenarios.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: confirm gates, run the quickstart end-to-end, document for downstream consumers.

- [ ] T053 [P] Run `/beutl-format` (apply mode) to make sure the entire diff conforms to `.editorconfig` and `xamlstyler.json`
- [ ] T054 [P] Run `/beutl-build` to confirm full-solution build is green on `net10.0` and `net10.0-windows` (constitution principle II)
- [ ] T055 [P] Run `/beutl-test` filtered to `FullyQualifiedName~Media.Proxy` plus `FullyQualifiedName~ProxyGeneration`; iterate until green; then run the full suite (constitution principle III)
- [ ] T056 [P] Run `/beutl-coverage` and confirm no regression in `Beutl.Engine` / `Beutl.ProjectSystem` coverage (constitution gate 4)
- [ ] T057 Manually walk through `docs/specs/002-proxy-media/quickstart.md` steps 1 → 12; report any deviation as a defect, not as quickstart drift. The "Measurement protocol" section in quickstart.md is the official verification path for SC-001 and SC-004 (no automated benchmark in MVP — manual is the contract)
- [ ] T058 Trigger `@beutl-design-reviewer` against the diff to catch any public-API drift that doesn't match the "adopt better designs eagerly" priority (e.g., overlapping abstractions, compatibility shims)
- [ ] T059 Trigger `@beutl-reviewer` to validate GPL/MIT boundary, XAML compiled-bindings, NUnit conventions, and source-generator impact across the diff
- [ ] T060 Update `docs/ai-workflow/` (if affected) and the per-module anchor in `src/Beutl.Engine/CLAUDE.md` with a short pointer to the new `Beutl.Media.Proxy` namespace
- [ ] T061 Run `/beutl-ai-self-review` to ensure the AI workflow scaffolding (subagents, skills, rules, hooks) still reflects current reality after a non-trivial feature has landed

**Final checkpoint**: Beutl ships a working proxy media workflow with green CI, no GPL ↔ MIT leakage, full NUnit coverage on the new namespace, and a quickstart that walks anyone through verifying the headline guarantee.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies; starts immediately.
- **Foundational (Phase 2)**: depends on Setup. **Blocks all user stories** — interfaces, enums, and `MediaOptions.PreferProxy` plumbing must exist before any story phase begins.
- **US1 (Phase 3)**: depends on Foundational. Independent of US2 and US3 (can be tested with manually-seeded proxies; see T033).
- **US2 (Phase 4)**: depends on Foundational. Independent of US1 in implementation but the demo flow (`/beutl-test`-level confidence) benefits from US1 being merged so end-to-end preview-with-real-proxy works.
- **US3 (Phase 5)**: depends on US1 (T031 / T051) and Foundational (T010 `PreviewSourceMode` enum). Trivially independent in test scope (mock `Scene`).
- **Polish (Phase 6)**: depends on every prior phase.

### Within each user story

- Tests are written first and confirmed failing before implementation (constitution principle III).
- Within US1: `IProxyResolver` impl precedes `DecoderRegistry` wire-up (T029, which hands the resolved `ProxyResolution` to the source layer) which precedes the 003 video logical-size seam (T063 source → T064 render node → T065 cache invalidation) which precedes context/export-path wiring (T030/T031). The seam (T062–T065) is part of US1 — US1's guarantee is incorrect without it.
- Within US2: `ProxyStore.ReconcileAsync` + `ProxyJobQueue` precede the concrete `FFmpegProxyGenerator` which precedes UI.
- Within US3: `Scene.PreviewSourceMode` precedes `SceneCompositor` context seeding which precedes the settings-UI toggle.

### Parallel opportunities

- All Phase 2 type tasks marked `[P]` (T005–T012, T016–T018) can run in parallel — different files, no behavior dependencies.
- T013 / T014 / T015 (MediaOptions change) are serial: T013 → T014 (auditing callers) → T015 (tests).
- Within US1: T022 / T023 / T024 / T025 (failing tests) all parallel; T027 parallel with T026; T028 / T029 / T030 / T031 serial along the routing chain.
- Within US2: T034 / T035 / T036 / T037 (failing tests) all parallel; T038 / T039 / T040 / T041 / T042 mostly parallel (different files, but T041 consumes the T018 `IProxyGenerator` abstraction); T045 (UI) parallel with the engine work once T038–T042 land.
- Polish phase (T053–T056) all parallel; T057 (manual quickstart) and T058 / T059 (reviewer subagents) sequential because they want the final diff.

---

## Parallel Example: User Story 1

```bash
# Failing tests, all parallel (different files):
Task: "Author ProxyResolverTests in tests/Beutl.UnitTests/Media/Proxy/ProxyResolverTests.cs"
Task: "Author DecoderRegistryProxyRoutingTests in tests/Beutl.UnitTests/Media/Proxy/DecoderRegistryProxyRoutingTests.cs"
Task: "Author ExportRenderContextSafetyTests in tests/Beutl.UnitTests/ProjectSystem/ExportRenderContextSafetyTests.cs"
Task: "Author ExportMissingSourceTests in tests/Beutl.UnitTests/ProjectSystem/ExportMissingSourceTests.cs"

# Foundational type implementations (parallel — different files, no behavior deps):
Task: "Implement ProxyFingerprint in src/Beutl.Engine/Media/Proxy/ProxyFingerprint.cs"
Task: "Implement ProxyPreset enum in src/Beutl.Engine/Media/Proxy/ProxyPreset.cs"
Task: "Implement ProxyState enum in src/Beutl.Engine/Media/Proxy/ProxyState.cs"
Task: "Implement PreviewSourceMode enum in src/Beutl.Engine/Media/Proxy/PreviewSourceMode.cs"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup → 4 tasks, ~30 min.
2. Phase 2 Foundational → 17 tasks. Pause and confirm `/beutl-build` green.
3. Phase 3 US1 → 12 tasks. **MVP demo step**: pre-seed a proxy on disk and verify preview=proxy / export=original (T033 + quickstart steps 4, 5, 8).
4. Stop here for an initial PR if scope must be split — the headline guarantee already holds with manually-managed proxies, which unblocks any team experimenting with the workflow.

### Incremental delivery

1. PR 1: Phases 1 + 2 + 3 (Setup + Foundational + US1). Ships the routing + safety guarantee.
2. PR 2: Phase 4 (US2). Ships generation + queue + LRU + UI.
3. PR 3: Phase 5 + Phase 6 (US3 toggle + polish). Ships the convenience toggle + closes out gates.

Each PR independently passes constitution gates 1–6. Each PR's diff stays under ~30 changed files. Each PR can be reverted without breaking the others (US3 falls back to default `PreferProxy`; US2 absent just means no generation UI but routing still works).

### Parallel team strategy

After Foundational (Phase 2) merges:

- Engineer A: US1 routing + tests (T022–T032).
- Engineer B: US2 store + queue + concrete FFmpeg generator (T034–T044).
- Engineer C: US2 UI (T045–T047).
- Engineer D: US3 (T048–T052) — can stage on a branch off US1 once T031 lands.

---

## Notes

- `[P]` = different files, no dependencies on incomplete tasks.
- `[Story]` label maps task → user story for traceability.
- Each user story is independently completable and independently testable (US1 with mocks / pre-seeded proxies).
- Tests must FAIL before their corresponding implementation lands (constitution III).
- The export path (`OutputViewModel` / `FrameProviderImpl` plus the `SceneCompositor` context seam) is the spec's headline safety floor — every change touching it must keep T024 + T025 + T030 green.
- The constitution forbids `ProjectReference` from MIT to `Beutl.FFmpegWorker`; additionally, do not add a reverse `Beutl.Engine` reference to `Beutl.Extensions.FFmpeg` or `Beutl.FFmpegIpc`. Proxy generation reuses `FFmpegEncodingControllerProxy` over the existing `Beutl.FFmpegIpc` channel from the concrete `Beutl.Extensions.FFmpeg` generator.
- Commit after each task or each logical group; prefer `feat:` for new types, `refactor:` for `MediaOptions` plumbing, `test:` for fixtures.
- Preset implementation starts from research R-5 numbers (`Half`, `Quarter`, `Eighth`) and tunes only if the quickstart measurement protocol shows the defaults miss SC-001 / SC-004.
