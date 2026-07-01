# Research: Proxy Media Workflow

**Feature**: 002-proxy-media | **Phase**: 0 | **Date**: 2026-05-20

Phase 0 resolves the open items left after `/speckit-clarify` so that Phase 1 (contracts + data model) and `/speckit-tasks` have concrete inputs. Each section follows: **Decision → Rationale → Alternatives considered**.

---

## R-1: Preview vs. export pipeline separation in `Beutl.Engine`

**Decision**: The spec-level assumption holds — preview and export video decode paths are distinct and meet only at `DecoderRegistry.OpenMediaFile(file, options)`. The route the spec is asking for is implemented by adding a new boolean `PreferProxy` to `MediaOptions`, threading it through `VideoSource.Resource.Update` (the current video site that constructs `MediaOptions`), and consulting a registered `IProxyResolver` inside `DecoderRegistry.OpenMediaFile` only when that flag is true.

- The **preview** `SceneCompositor` seeds the render context's `PreferProxy` from `Scene.PreviewSourceMode`; `VideoSource.Resource.Update` reads it when constructing `new(MediaMode.Video)` — the sole video `MediaOptions` site (`src/Beutl.Engine/Media/Source/VideoSource.cs`). (`SceneRenderer` / `SceneComposer` do **not** construct `MediaOptions`; the compositor's `CompositionContext` is the seam, as a sibling to the existing `DisableResourceShare` init-property — `src/Beutl.Engine/Composition/CompositionContext.cs`, `src/Beutl.ProjectSystem/SceneCompositor.cs`.)
- The **export** `SceneCompositor` (the one inside the `SceneRenderer` that `OutputViewModel` builds with `disableResourceShare: true`) MUST NOT seed `PreferProxy` from `Scene.PreviewSourceMode`, so the context default `false` holds and every export `MediaOptions` is `PreferProxy = false`, **always**, regardless of project setting. This is the safety floor for FR-002 / FR-004.

**Rationale**: `DecoderRegistry.OpenMediaFile` is the only public choke point through which video media open requests pass; intercepting there guarantees consistent behavior without sprinkling proxy-awareness across decoder implementations. `MediaOptions` already exists as the per-open parameter bag, so adding one boolean is the minimum-surface change. Keeping the export context explicitly unable to seed `PreferProxy = true` prevents future regressions: a `git grep PreferProxy` must show both the preview seeding site and the export-safety test seam.

**Alternatives considered**:

- **Wrap the returned `MediaReader` with a "proxy or original" `MediaReader` decorator.** Rejected: pushes proxy logic into the hot decode path on every frame, and the decorator either always opens both readers (wasteful) or proxies dynamically (complex). The "decide once at open time" model is simpler and matches how `FFmpegReaderProxy` is selected today.
- **Add a separate `OpenMediaFileForPreview` method.** Rejected: duplicates the registry surface, and the export call site still has to know to call the non-preview variant — same coupling, more API. Reusing `MediaOptions` is more orthogonal (one method, one bag of options).
- **Make `MediaSource` itself carry a proxy mode.** Rejected: `MediaSource` is shared across preview and export; per-call routing belongs in `MediaOptions`, not on the source object.

**Verification step before merge**: grep for every call to `DecoderRegistry.OpenMediaFile` and `VideoSource.Resource.Update` and audit the surrounding `MediaOptions` construction plus `CompositionContext` seeding; ensure the export pass cannot accidentally inherit a `PreferProxy = true` from preview state.

> **Post-003 amendment**: R-1 was written before `003-resolution-independent-pipeline` landed. The `MediaOptions.PreferProxy` toggle and the `DecoderRegistry.OpenMediaFile` choke point remain correct, but they are no longer sufficient on their own — opening a smaller proxy file would shrink the source's decoded `FrameSize` and therefore its logical footprint under 003. The full integration design (logical-size channel, supply density, `MediaOptions` shape trade-off) is settled in **R-11** below; R-1's "minimum-surface change" framing still describes the toggle, while R-11 adds the size/density side-channel the 003 pipeline requires.

---

## R-11: Integration with the 003 resolution-independent pipeline (logical-size seam + supply density + `MediaOptions` shape)

**Decision**: A proxy is modeled as a **lower-density supply with an unchanged logical footprint**, consumed through the seam 003 deliberately left open (003 FR-023/FR-024, `003/data-model.md` "003 scope note"). Concretely:

1. `ProxyResolution` (returned by `IProxyResolver`) carries `OriginalLogicalFrameSize` (the original source `FrameSize`) **and** `ProxyDecodedFrameSize` (the proxy's decoded `FrameSize`); their linear ratio is the proxy's supply density (for example `0.5` for an exact half-width/half-height proxy, with tolerance for long-edge clamps and integer rounding).
2. `MediaOptions.PreferProxy` stays a bare `bool` — it is only the on/off toggle consulted by `DecoderRegistry.OpenMediaFile`. The size/density data rides the `ProxyResolution` side-channel, not `MediaOptions`.
3. `SourceVideo` and its `.Resource` thread the original logical size through; `VideoSourceRenderNode` pins `Bounds` to the **original** logical size, reports `EffectiveScale.At(supplyDensity)` (replacing the hard-coded `EffectiveScale.At(1)`), and draws the decoded proxy bitmap scaled into the original-footprint destination rect (the 003 FR-024 dest-rect seam). Still images remain out of MVP scope because `ImageSource` bypasses `DecoderRegistry.OpenMediaFile`.
4. On the original path (no proxy, `PreferProxy = false`, or export) the original `FrameSize` **is** the decoded `FrameSize`, the density is `1.0`, and behavior is byte-identical to 003 — the seam is purely additive.
5. Export always decodes the original (FR-002): under 003's working-scale rule `w = min(max(s_out, densest supply), MaxWorkingScale)`, a sub-output proxy at export (`s_out ≥ 1` — Off / 2× / 4× supersampling; export ceiling `+∞`) would be lifted to at least `s_out` and upsampled (soft); routing through the original is the only way to keep export full-fidelity.

**Rationale**: 003's supply-driven model means a source's value to the pipeline is its `EffectiveScale` (supply density), and effects run at `w = min(max(s_out, densest supply), MaxWorkingScale)` (supply-driven on the high side, floored at `s_out`, capped by a per-request working-scale ceiling — `2 × s_out` in preview, `+∞` at export; `RenderNodeContext.ResolveWorkingScale`). Lowering a source's supply density — which is exactly what a reduced-resolution proxy does — lowers `w` (down to the `s_out` floor) and therefore the decode + effect-raster cost of preview. The ceiling is inert for the sub-output proxies this feature produces; the **floor** is what governs them. This is the precise mechanism by which proxies help, and it explains **where** they help: 003's preview render scale already cheapens vector / text / Skia-filter preview, so proxies add value only for **source-heavy** scenes whose supply density 003 otherwise preserves. Modeling the proxy as "same footprint, lower density" (rather than "a smaller file swapped in") is what makes it compose correctly with 003's mixed-scale compositing without moving content.

**Alternatives considered**:

- **Bare `bool PreferProxy` + native 1:1 blit (the pre-003 design in R-1).** Rejected as *incomplete*: opening the proxy file shrinks the decoded `FrameSize`, and because 003 derives the logical footprint from the decoded `FrameSize`, the clip would render at the proxy's smaller size / shifted position. The toggle is retained; the size side-channel (point 1–3) is added.
- **A decode-target-size / decode-scale hint on `MediaOptions` (the seam 003 FR-025 explicitly reserves).** Considered: it would carry the scale on `MediaOptions` instead of a side-channel. Rejected for 002 because (a) a proxy needs the **original logical size**, not merely a scale factor — and the original size is known only to the resolver/generation metadata, not to the generic decoder open path; (b) the file-swap decision is per-open and already centralized in `IProxyResolver.Resolve`, so carrying the result (path + sizes) on `ProxyResolution` is more direct than encoding it as a decode-scale hint and re-deriving the footprint. This keeps `MediaOptions` minimal and additive, consistent with 003 FR-025's "MUST NOT add the decode-scale hint now, MUST NOT foreclose it" — a future feature may still add the hint if a non-file-based reduced decode appears.
- **Derive the logical footprint from the proxy and rescale the drawable.** Rejected: it would change the document's logical coordinates and break layout/hit-testing (003 FR-027); the whole point of 003 US3 is that swapping backing resolution must not move content.

**Verification step before merge**: render a clip with proxy on and proxy off and assert the on-canvas footprint (bounds + hit region) is identical and only the op's `EffectiveScale` differs; render at export and assert the original is used (no proxy upsample). Covered by T062–T065 and quickstart step 4a.

---

## R-2: Proxy generation orchestration (no new IPC verbs, no reverse Engine references)

**Decision**: `Beutl.Engine.Media.Proxy` exposes an `IProxyGenerator` abstraction, and `ProxyJobQueue` depends only on that abstraction. The concrete FFmpeg-backed implementation lives in `Beutl.Extensions.FFmpeg.Proxy` (for example `FFmpegProxyGenerator`) and drives proxy generation by:

1. Opening the source via `DecoderRegistry.OpenMediaFile(source, options with PreferProxy = false)` to get a `MediaReader`.
2. Wrapping that reader as a frame provider that reads at native rate.
3. Instantiating `FFmpegEncodingControllerProxy` with `FFmpegVideoEncoderSettings` populated from the chosen `ProxyPreset`.
4. Calling `Encode(frameProvider, NullSampleProvider, cancellationToken)` (audio is dropped — see FR-020).
5. On success: atomic-rename the output, then call `IProxyStore.Register(entry)`.
6. On failure or cancel: delete partial files and mark the entry `Failed` / removed.

**Rationale**: The existing `FFmpegEncodingControllerProxy` already runs the GPL encoder over IPC. Reusing it satisfies the License Firewall principle (no new MIT → GPL edges) and means we ship zero new IPC messages. The concrete generator cannot live in `Beutl.Engine`, because `Beutl.FFmpegIpc` and `Beutl.Extensions.FFmpeg` already depend on Engine-side media abstractions; adding an Engine reference back to either project would create an invalid project-reference cycle. Keeping only `IProxyGenerator` in Engine preserves the existing dependency direction while letting the application / extension composition root register the FFmpeg implementation once per app.

**Alternatives considered**:

- **Add a dedicated `GenerateProxyRequest` IPC verb.** Rejected: would introduce a new payload that, by definition, has to round-trip metadata the worker already knows how to derive from `EncodeStartRequest`. Pure cost, no benefit.
- **Use FFmpeg directly in-process for proxy generation.** Rejected: violates Principle I (License Firewall) — would force a `ProjectReference` from MIT to GPL.
- **Spawn a dedicated short-lived `Beutl.FFmpegWorker` process per job.** Implicitly accepted: `FFmpegWorkerProcess.CreateForEncoding()` already does this; we inherit its lifecycle. No new design needed.
- **Put `ProxyGenerationOrchestrator` in `Beutl.Engine` and reference `FFmpegEncodingControllerProxy` directly.** Rejected: it would require `Beutl.Engine` to reference `Beutl.Extensions.FFmpeg` / `Beutl.FFmpegIpc`, reversing the existing dependency direction and creating a cycle.

---

## R-3: Default LRU cap value

**Decision**: Default global cap = **50 GB**. User-configurable range: 5 GB ↔ 500 GB via `ProxyStoreConfig.MaxTotalBytes`. Cap is enforced after each successful generation and on startup.

**Rationale**:
- 50 GB holds roughly 5–10 hours of 1/2-resolution H.264 proxy footage at the bitrates picked in R-5 — enough for typical projects without immediately evicting fresh work.
- Lower bound 5 GB lets users on small SSDs participate.
- Upper bound 500 GB is a sanity ceiling against runaway config; users with larger needs can edit `ProxyStoreConfig.cs` defaults in a follow-up.

**Alternatives considered**:

- **No default cap.** Rejected: disk-full surprises are exactly what the LRU feature is meant to prevent.
- **% of free disk at startup.** Rejected: surprising behavior — proxies that fit yesterday might be evicted today because someone else filled the disk. A fixed configurable cap is predictable.
- **Per-project cap rather than global.** Rejected: clarification Q5 explicitly picked **global LRU**. Per-project would also force the eviction service to enumerate all projects, fighting Beutl's project-portability model.

---

## R-4: Source identity / fingerprint

**Decision**: `ProxyFingerprint = (AbsolutePath, FileSizeBytes, MtimeUtc)`. Two fingerprints are equal iff all three components match exactly. No content hashing in MVP (per clarification Q2).

**Rationale**: Cheap (one `FileInfo` call), accurate enough in the common case, and matches the convention used by other NLEs. The known failure mode (sync tools that bump mtime without changing content) leads to a regenerated proxy — slow but never wrong. The dangerous failure mode (content changes but mtime preserved) is rare in practice and surfaced by the manual "regenerate" affordance.

**Alternatives considered**: covered in clarification Q2; not re-litigated here.

**Implementation note**: `AbsolutePath` is normalized via `Path.GetFullPath` and case-folded (`ToUpperInvariant`) on Windows / macOS — their default volumes are case-insensitive — to avoid spurious mismatches; Linux stays case-sensitive. Accepted MVP nuance: the fold is by string case, not per-volume detection, so on a case-sensitive APFS volume the fold can rarely over-merge distinct paths (see data-model.md path-normalization note). Symlinks are followed at fingerprint time so that a project moving between drives with a stable symlink works; this is documented as a known nuance.

---

## R-5: H.264 preset definitions

**Decision**: Ship **three presets** in MVP: `Half`, `Quarter`, and `Eighth`. All use H.264 in MP4 container, `yuv420p` 8-bit, single-pass `crf`-rate (constant quality), `tune=fastdecode`, `preset=fast`, audio dropped.

| Preset | Scale factor | Long-edge clamp | CRF | Approx. bitrate at 1080p source |
|---|---|---|---|---|
| `Half` | 1/2 | max 1920 long-edge | 25 | ~4–6 Mbps |
| `Quarter` (default) | 1/4 | max 1280 long-edge | 26 | ~1.5–2.5 Mbps |
| `Eighth` | 1/8 | max 854 long-edge | 28 | ~0.6–1 Mbps |

**Rationale**:
- `Quarter` is the default because it gives the biggest editor responsiveness win for the smallest disk cost. `Half` is the "high-fidelity scrub" option for color-attentive workflows; `Eighth` is the "any decode is good enough" option for laptops on battery.
- `crf` (constant-quality) is preferred over `b:v` (constant bitrate) for proxies — quality matters more than predictable file size, and constant-quality scales naturally with source complexity.
- `tune=fastdecode` minimizes CPU during preview playback (the whole point); `preset=fast` keeps encode time reasonable.
- All three keep the `Beutl.FFmpegWorker` x264 path; no codec discovery work in MVP.

**Alternatives considered**:

- **ProRes Proxy / DNxHR LB.** Rejected for MVP: requires verifying encoder availability across platforms in the bundled FFmpeg and would significantly inflate proxy file sizes (intra-only). Excellent decode performance, but the disk-vs-decode tradeoff favors H.264 for MVP.
- **HEVC (x265).** Rejected: slower to encode for marginal preview benefit; harder to play back on some hardware. Save for follow-up.
- **Two presets only (`Half` + `Quarter`).** Rejected for MVP: the coding/test cost of `Eighth` is small because the preset table is a single source-of-truth `ProxyPresetDefinitions.cs`, and the low-density option is useful for constrained laptops / very heavy sources.

**Open implementation tuning**: exact CRF and bitrate ceilings are subject to a one-pass tuning round during implementation. The values above are starting points.

---

## R-6: Proxy store layout and metadata format

**Decision**: One **store root directory** per machine (default `<app cache>/Beutl/proxies/`). Inside the root:

```text
<store-root>/
├── index.json                     # single JSON file: array of ProxyEntry
├── <fingerprint-hash>/
│   ├── half.mp4
│   ├── quarter.mp4
│   └── eighth.mp4
└── <next-fingerprint-hash>/...
```

- `<fingerprint-hash>` = short non-cryptographic hash of `ProxyFingerprint` (e.g., 16-hex of xxhash64 of "path|size|mtime"); collision risk is acceptable because the actual identity check still uses the full triple on load.
- The hash is used **only** for directory naming; it is not the staleness key (R-4 already settled that).
- One proxy file per preset coexists per source — letting users switch presets without forcing a regenerate of every variant on disk.
- `index.json` is the authoritative metadata; on parse failure the store rebuilds it by scanning the directory tree.

**Rationale**: A single JSON file is human-readable, trivially `git`-diffable for debug, and avoids dragging in a database dependency. Grouping per source under a hashed subdirectory keeps the filesystem entries manageable even with thousands of sources. Multi-preset-per-source supports the realistic flow where the user starts on `Quarter` and later switches to `Half` for a precision check.

**Alternatives considered**:

- **SQLite.** Rejected: adds a dependency for what is fundamentally a flat list. Recovery from corruption is harder than "delete and rescan".
- **Per-entry sidecar `.json`.** Rejected: more files to fsync, less atomic for batch updates.
- **Flat directory with hashed filenames, no subdirs.** Rejected: makes "delete all proxies for one source" expensive (you'd need to enumerate the whole flat dir).

**Atomic write protocol**: generation writes to `<dir>/quarter.mp4.tmp`, fsyncs, then renames to `quarter.mp4`. `index.json` is updated last via the same temp-then-rename pattern (`index.json.tmp` → `index.json`). Boot scan looks for orphan `*.tmp` files and removes them.

---

## R-7: Serial job queue implementation

**Decision**: `ProxyJobQueue` is implemented as a single-consumer `Channel<ProxyJob>` (`BoundedChannelOptions { FullMode = Wait, Capacity = 256 }`) drained by one async loop. Each job's `Run` returns a `Task` whose lifecycle is tracked separately so cancel / progress observers don't need access to the channel itself.

**Rationale**: `Channel<T>` is the idiomatic .NET pattern for producer–consumer with structured cancellation; one consumer enforces serial execution out of the box. The capacity bound (256) prevents unbounded queue growth when the user invokes "generate for all clips" on a project with thousands of sources — additional enqueue calls await space rather than allocating without limit.

**Alternatives considered**:

- **`SemaphoreSlim(1, 1)` around a `List<ProxyJob>`.** Rejected: requires hand-rolling FIFO ordering and cancellation propagation; `Channel<T>` already gives both.
- **`TaskScheduler.LimitedConcurrencyLevel`.** Rejected: harder to wire cancellation per job and harder to enumerate the pending queue for UI display.
- **Per-job `Task.Run`.** Rejected: doesn't enforce serial execution; would violate the MVP concurrency=1 clarification answer.

**Design provision for future parallelism**: the queue accepts a `maxConcurrency` parameter (default 1) so a future change can lift the cap without rewriting the queue API.

---

## R-8: Project-level `PreviewSourceMode` storage and propagation

**Decision**: Add `PreviewSourceMode PreviewSourceMode { get; set; }` to `Scene` with default `PreferProxy`. Persisted in the existing `Scene` JSON via the existing serialization pipeline. The preview `SceneCompositor` reads it while seeding `CompositionContext.PreferProxy`, and `VideoSource.Resource.Update` copies that context value into the `MediaOptions` it constructs.

**Rationale**: `Scene` is already the per-project state object and is already serialized. Putting the toggle there is the smallest possible change and matches how `TimelineOptions` is stored. A project-level (not global) toggle matches the user's mental model — different projects can have different needs (color-grading project wants `ForceOriginal`, rough-cut wants `PreferProxy`).

**Alternatives considered**:

- **Global setting in `ProxyStoreConfig`.** Rejected: doesn't match user mental model. A user opening a color project should not have to remember to flip a global switch.
- **Per-clip override.** Considered as enhancement; out of scope for MVP. The current "global fallback to original when proxy is missing" already covers the spot-check case.

---

## R-9: UI surface

**Decision**: Two UI surfaces in MVP:

1. **Project Settings → "Preview source" toggle** (radio: Proxy / Original) — bound to `Scene.PreviewSourceMode`.
2. **New tool tab: "Proxies"** — implemented as a `ToolTabExtension` registered via the existing extensibility surface. Contents: per-project clip list with proxy state badge (None / Generating / Ready / Stale / Failed), action buttons (Generate / Regenerate / Delete) for selection, current/pending job list with progress bars, current store totals (per-project size, global size, % of cap), "Delete all for this project" button.

A small per-clip badge on the timeline strip is captured as a stretch goal and left for `/speckit-tasks` to slot.

**Rationale**: Tool tab is the established Beutl pattern for "auxiliary feature with its own state" (see `beutl-tooltab-extension` skill). Putting visibility behind a tab keeps the timeline UI uncluttered for users who don't use proxies.

**Alternatives considered**:

- **Modal dialog for proxy management.** Rejected: editors hate modal dialogs in a long-running workflow.
- **Status-bar-only UI.** Rejected: not enough surface area for selection, regen, deletion.

---

## R-10: FFmpeg availability gating

**Decision**: FFmpeg availability is owned by the concrete `Beutl.Extensions.FFmpeg` generator, not by `Beutl.Engine.ProxyJobQueue`. If the generator cannot start because FFmpeg libraries are unavailable, it surfaces the existing `FFmpegInstallNotifier` path and returns a dependency-missing failure to the queue. The queue marks the active job `Failed` with reason `FFmpegMissing`, pauses draining, and resumes when the registered generator reports availability again. Other queued jobs remain queued, not lost.

**Rationale**: Reuses the existing install-prompt pattern (`FFmpegInstallDialog`) so the proxy feature behaves consistently with export, which already requires FFmpeg.

**Alternatives considered**:

- **Silently fail.** Rejected: violates the spec's "no surprises" tone; user won't know why proxies never appear.
- **Block enqueue.** Rejected: would force users to install FFmpeg before they can even click "Generate", which is a worse onboarding moment than a clear "install needed" dialog.

---

## Summary of resolved unknowns

| Unknown | Resolution |
|---|---|
| Preview vs. export pipeline separation | R-1: distinct, gated by `MediaOptions.PreferProxy` |
| Proxy generation IPC | R-2: `IProxyGenerator` abstraction in Engine, FFmpeg concrete implementation in `Beutl.Extensions.FFmpeg` reuses existing `FFmpegEncodingControllerProxy` |
| Default LRU cap | R-3: 50 GB default, 5–500 GB configurable |
| Source fingerprint | R-4: `(path, size, mtime)` triple |
| Preset shape | R-5: 3 H.264 presets (Half / Quarter default / Eighth) |
| On-disk layout | R-6: store root with `index.json` + per-source subdirs |
| Queue implementation | R-7: `Channel<ProxyJob>` with bounded capacity, single consumer |
| Project toggle storage | R-8: `Scene.PreviewSourceMode` |
| UI surface | R-9: project settings toggle + new Proxies tool tab |
| FFmpeg-missing UX | R-10: reuse existing install prompt; jobs stay queued |
| 003 integration (logical-size seam, supply density, `MediaOptions` shape) | R-11: proxy = lower-density supply, same logical footprint; sizes on `ProxyResolution`, `PreferProxy` stays a bool toggle |
