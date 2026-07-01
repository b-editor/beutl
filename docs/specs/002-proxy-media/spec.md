# Feature Specification: Proxy Media Workflow

**Feature Branch**: `002-proxy-media`

**Created**: 2026-05-20

**Status**: Ready for Implementation

**Input**: User description: "編集中は低解像度プロキシ素材、書き出し時は元素材を使うようにしたい。"

## Clarifications

### Session 2026-05-20

- Q: MVP 出荷時点で何種類の proxy プリセットを用意し、どの程度のスペックを既定にするか → A: 2〜3 プリセット (例: 1/2 / 1/4 解像度・H.264) からユーザが選ぶ
- Q: source clip と proxy の同一性をどう判定するか (staleness 検出のフィンガープリント) → A: `path + size + mtime` のみ (高速。mtime ドリフトによる false stale はユーザが再生成で解決)
- Q: stale 検出時にどう振る舞うか → A: MVP は手動再生成のみ。stale は UI 上で可視化し「再生成」アクションをユーザに任せる。auto-regenerate は follow-up
- Q: 同時に走る proxy 生成ジョブの本数 → A: 直列 (同時 1 本)。MVP は単一 FFmpegWorker プロセスでキュー消化。並列度は follow-up で拡張可能
- Q: proxy キャッシュの自動上限/退避を MVP で入れるか → A: グローバル LRU eviction を入れる (既定上限あり、設定で変更可)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Edit with proxy, export with original (Priority: P1)

When a user is editing a project that references heavy source media (large/high-resolution/high-bitrate video), Beutl should automatically use a low-resolution proxy of each clip for the timeline preview, scrubbing, and live playback — so seek and playback stay smooth. When the user starts an export / render, Beutl must transparently switch back to the original source media so the exported file is at full quality.

**Why this priority**: This is the headline value of the feature. Without it, editing 4K/8K footage on commodity hardware is painful, and proxies are useless unless the export path is guaranteed to use the originals. *(Post-003 scoping: 003's preview render scale already cheapens vector / text / Skia-filter preview; this story's value is concentrated on **source-heavy** clips, where the proxy lowers the supply density that 003 otherwise preserves — see Assumptions.)*

**Independent Test**: Import a high-resolution clip, generate its proxy, scrub the timeline and confirm the preview is served from the proxy (verifiable via debug overlay / log or measurably lower decode cost). Then run an export and confirm the exported file's resolution, bitrate, and pixel-level content match the original source, not the proxy.

**Acceptance Scenarios**:

1. **Given** a clip on the timeline has a fresh proxy available, **When** the user scrubs / plays back in the editor preview, **Then** Beutl decodes from the proxy and the editor remains responsive.
2. **Given** the same clip with a fresh proxy, **When** the user triggers an export of the project, **Then** the exported file is rendered from the original source media, not from the proxy.
3. **Given** a clip on the timeline has no proxy available (or its proxy is stale relative to the source), **When** the user scrubs / plays back, **Then** Beutl falls back to the original source media and the editor still works (just less smoothly) — playback never fails because the proxy is missing.

---

### User Story 2 - Generate, regenerate, and remove proxies (Priority: P2)

The user needs an explicit way to control proxy generation: request a proxy for a clip (or a selection of clips, or every heavy clip in the project), regenerate a proxy when the source changes or settings change, and delete proxies to reclaim disk space. Proxy generation runs in the background and reports progress, so the user can keep editing while proxies build.

**Why this priority**: Proxies must exist before P1 can deliver value. The user also needs to manage disk usage and recover from stale proxies. Without explicit controls, the workflow turns into a black box that silently chews through disk.

**Independent Test**: Select a clip in the project, invoke "Generate proxy", observe a progress indicator, and confirm a proxy file appears in the configured proxy storage location. Then invoke "Delete proxy" and confirm the proxy file is removed and the project still plays back (from the original).

**Acceptance Scenarios**:

1. **Given** a clip with no proxy, **When** the user invokes "Generate proxy" on it, **Then** a background job starts, progress is visible, and on completion the proxy is available for preview use.
2. **Given** a clip whose source file's `(path, size, mtime)` triple has changed since its proxy was generated, **When** the user opens the project, **Then** the proxy is marked stale and surfaced in the UI as a regeneration candidate. The system does NOT auto-queue a regeneration job — the user explicitly triggers it. Preview for that clip falls back to the original meanwhile (per US1 acceptance #3).
3. **Given** a clip with a proxy on disk, **When** the user invokes "Delete proxy", **Then** the proxy file is removed and the clip falls back to its original source for preview.
4. **Given** the user invokes "Generate proxies for all clips" on a project with many heavy clips, **When** the job runs, **Then** jobs are queued and processed, the user can keep editing during processing, and individual proxies become usable as soon as each one finishes (not only at the end of the batch).

---

### User Story 3 - Toggle between proxy and original preview (Priority: P3)

While editing, the user occasionally needs to inspect detail that the low-resolution proxy hides (focus check, fine color work, small text in a graphic). The user wants a project-level toggle that forces the preview path to use the original media instead of the proxy, without having to delete the proxy or change the export.

**Why this priority**: A convenience over the core flow. The fallback in US1 already handles the "no proxy" case correctly; this story is about deliberately previewing at full quality during editing.

**Independent Test**: With proxies generated, flip the preview source toggle to "Original" and confirm the editor preview switches to full-resolution decoding (visibly higher detail; measurable higher decode cost). Flip back to "Proxy" and confirm the proxy is used again. The export path is unaffected in both cases.

**Acceptance Scenarios**:

1. **Given** a project where all clips have proxies, **When** the user sets the preview source to "Original", **Then** subsequent preview decoding uses the original source files, not the proxies.
2. **Given** the preview source is set to "Original", **When** the user triggers an export, **Then** the export still uses the originals (no behavior change for export — the toggle only affects preview).
3. **Given** the preview source is set to "Proxy" and a specific clip has no proxy, **When** the user previews that clip, **Then** Beutl transparently falls back to the original for that clip (per US1 acceptance #3) without disabling the global toggle.

---

### Edge Cases

- **Stale proxy detection**: source file is edited / replaced externally after the proxy was generated. Beutl must detect this (via the source's `(path, size, mtime)` triple — see Clarification Q2 and FR-010; content hashing is out of scope for MVP) and treat the proxy as stale rather than silently serving outdated frames.
- **Proxy file missing on disk**: project was moved to a new machine, or the proxy cache was deleted. Preview must fall back to original; no error dialog on every clip.
- **Original source missing on disk at export time**: even if a proxy exists, an export must not silently substitute the proxy. The export must fail (or surface a "relink source" prompt) so the user does not unknowingly ship a low-resolution result.
- **Project references the same source file from many clips (subclips, trims)**: a single proxy should be reused across all those clip instances, not generated once per timeline placement.
- **Proxy generation is interrupted** (app crash, machine shutdown, user cancels): partial proxy files must not be served as if complete. The next session must recognize the partial state and either resume or restart that proxy.
- **Source media types that do not benefit from MVP proxies**: still images, short audio-only clips, generative / procedural sources. The feature should not waste disk and CPU on paths that either bypass the video decoder seam or provide negligible preview-decode benefit.
- **Disk space exhaustion** during proxy generation: the system first attempts to free space via LRU eviction within the configured cap (per FR-018a). If host-level disk space is still insufficient after eviction, the job must fail cleanly, surface the error, and leave the project usable (fall back to originals for the clips whose proxies did not complete).
- **LRU eviction races with preview decode**: a proxy must not be deleted while it is being read for preview or while its generation job is in flight (per FR-018a safety clause). The eviction algorithm must defer or skip such proxies and pick the next LRU candidate instead.
- **Render preview ("preview render" / "RAM preview" if applicable) vs. final export**: the rule is "preview = proxy when available, export = original always". Any in-editor render that the user intends to ship must follow the export rule, not the preview rule.

## Requirements *(mandatory)*

### Functional Requirements

**Preview vs. export routing (core)**

- **FR-001**: The system MUST, during editor preview / scrubbing / playback, decode from a clip's proxy when a fresh, complete proxy exists for that clip and the project's preview source mode is "Proxy".
- **FR-002**: The system MUST, during any export / final render, decode from the original source media of every clip — never from a proxy — regardless of whether a proxy exists. **Rationale (post-003)**: at export `s_out ≥ 1` (the user-facing supersampling factors are Off (1×) / 2× / 4× — `OutputViewModel` passes `Math.Max(1, SupersampleFactor)` as the render scale), 003's supply-driven working-scale rule `w = min(max(s_out, densest supply), MaxWorkingScale)` — whose export ceiling is `+∞` — would lift a sub-output proxy (e.g. `At(0.5)`) back up to at least `s_out`, i.e. the proxy would be **upsampled** (soft) into the effect pass. Routing export through the original (`At(1.0)` or denser) is therefore not only the FR-004 safety floor but also the only way to keep export full-fidelity under 003's model.
- **FR-003**: The system MUST, when a clip's proxy is missing, incomplete, or stale, fall back transparently to that clip's original source during preview without aborting playback.
- **FR-004**: The system MUST, when a clip's original source is missing at export time, fail the export with an actionable error (e.g., "relink source") and MUST NOT silently substitute the proxy.

**Proxy lifecycle**

- **FR-005**: Users MUST be able to request proxy generation for a specific clip, a selection of clips, or every eligible clip in the current project.
- **FR-006**: Users MUST be able to delete a clip's proxy (single, selection, or all) to reclaim disk space.
- **FR-007**: The system MUST run proxy generation as a background job that does not block editing, and MUST surface per-job and overall queue progress.
- **FR-008**: The system MUST queue multiple proxy generation jobs and process them **serially (one at a time)** at MVP. Each finished proxy MUST become individually usable as soon as it completes (no "all-or-nothing" batch). Parallel job execution is a follow-up enhancement; the queue API SHOULD be designed so that raising concurrency later does not require an architectural rewrite.
- **FR-009**: The system MUST be cancellable mid-job; cancelling MUST NOT leave a partial file that is treated as a complete proxy on the next session.

**Staleness, identity, and sharing**

- **FR-010**: The system MUST detect when a clip's proxy is stale relative to its source. Staleness is determined by comparing the source file's current `(absolute path, file size, mtime)` triple against the values recorded at proxy generation time; any mismatch marks the proxy stale. The system MUST treat stale proxies as missing for preview purposes until regenerated. Content hashing is explicitly out of scope for MVP (cost on 4K/8K sources is prohibitive); if a user's sync tooling causes false-stale detections, the user re-generates the affected proxies.
- **FR-011**: The system MUST identify proxies by source media identity (not by timeline placement), so multiple clips that reference the same source file share a single proxy on disk.
- **FR-012**: The system MUST recognize partially-written proxy files (e.g., from a crash mid-generation) and treat them as missing, not as complete.

**Resolution integration (built on the 003 resolution-independent pipeline)**

- **FR-021**: The system MUST preserve each proxied video clip's **logical footprint** when decoding from a proxy. As shipped by 003, a video's logical size is derived from its decoded `FrameSize` (`SourceVideo` returns `r.Source.FrameSize.ToSize(1)`, and `VideoSourceRenderNode` hard-codes `EffectiveScale.At(1)`). Naively pointing the decoder at a smaller proxy file would therefore shrink the clip's decoded `FrameSize`, shrink its on-canvas bounds, and move its content. Proxy decode MUST instead carry the **original** source `FrameSize` as a stable logical size — independent of the proxy's decoded `FrameSize` — so the clip's bounds, layout, and hit region are identical whether the backing decode is original or proxy (the 003 seam: US3 acceptance #1 / SC-007 / 003 FR-023). This is the "stable intrinsic-logical-size channel" 003 explicitly deferred to the proxy feature. Still images are out of MVP scope and stay on their existing image-source path.
- **FR-022**: Each proxied video-source operation MUST report its supply density as the linear ratio `ProxyDecodedFrameSize / OriginalLogicalFrameSize` (for example, `0.5` for an exact half-width/half-height proxy, with tolerance for long-edge clamps and integer rounding) via the 003 `EffectiveScale.At(scale)` annotation, replacing the current hard-coded `EffectiveScale.At(1)`. The decoded proxy bitmap MUST be drawn into the original-footprint logical destination rect (the 003 FR-024 dest-rect seam), so 003's supply-driven rule reconciles the lower-density proxy exactly once at the relevant buffer boundary (`w = min(max(s_out, densest supply), MaxWorkingScale)`; Mitchell resample when the op is reconciled). A proxy never moves content and never changes logical bounds; it only lowers supply density.
- **FR-023**: Because toggling proxy usage (the preview source mode, or a clip's proxy becoming ready / stale / deleted) changes the affected sources' `EffectiveScale`, the system MUST invalidate the 003 render cache for those sources on such a change (003 FR-020/FR-032) so no stale-density tile is blitted. The toggle takes effect on the next rendered frame (SC-003) and does **not** require a `SceneRenderer` rebuild — the render scale is unchanged; only the per-source supply density changes.

**Visibility and control**

- **FR-013**: The system MUST expose a project-level preview source toggle with at least "Proxy (preferred, fall back to original)" and "Original (force)".
- **FR-014**: The toggle in FR-013 MUST NOT affect export behavior — exports always follow FR-002.
- **FR-015**: The system MUST surface, somewhere visible (per-clip indicator, project panel, or status bar — choice deferred to design), the proxy state of each clip: none / generating / ready / stale / failed.

**Storage and configuration**

- **FR-016**: The system MUST store proxy files in a configurable location, with a sensible per-user default (e.g., under the application's cache directory). The default MUST NOT be inside the project directory unless the user opts in.
- **FR-017**: The system MUST ship with **2–3 fixed proxy quality presets** at MVP, all H.264-based, differing primarily by scale (e.g., 1/2 source resolution and 1/4 source resolution; a third intermediate preset MAY be included). The user MUST be able to choose which preset is used when generating a proxy, and the chosen preset MUST be recorded with each generated proxy so a later regeneration can match (or deliberately replace) it. **Distinct from the 003 preview render scale (post-003 clarification)**: a proxy preset selects the **decode/supply density** of the proxy file (a persisted, per-proxy property, FR-022); the 003 preview render scale (`Full`/`Half`/`Quarter`/`Fit-to-previewer`, per-edit-view session state, 003 FR-035) selects the **rasterization** density of the whole preview. They are different axes that happen to share the `Half`/`Quarter` vocabulary. They interact through 003's supply-driven rule `w = min(max(s_out, densest supply), MaxWorkingScale)` — supply-driven on the high side, floored at `s_out`, and capped by the per-request working-scale ceiling (`2 × s_out` in preview, `+∞` at export). The cap is inert for a sub-output proxy (its density is below the ceiling); the **floor** is what binds it: a proxy denser than the active preview scale still saves **decode** cost but the effect pass runs at the proxy density (not the cheaper preview scale); a proxy at or below the preview scale lowers `w` fully (to the `s_out` floor). The preset values (`Half`, `Quarter`) are therefore chosen to align with the 003 preview-scale vocabulary so the two compose predictably, and the UI SHOULD communicate that a proxy's benefit is greatest when its density is ≤ the active preview scale.
- **FR-018**: Users MUST be able to view total proxy disk usage for the current project and globally, and trigger a "delete all proxies for this project" action.
- **FR-018a**: The system MUST enforce a **global LRU eviction policy** on the proxy store: a configurable maximum total size (with a sensible default — exact number to be set in `/speckit-plan`, candidate range 20–100 GB) above which the least-recently-used proxies are deleted to bring the store back under the cap. "Recently used" is keyed on the most recent of (last preview-decode time, last successful generation time). Eviction MUST be safe — never evict a proxy whose generation job is still running or whose file is currently being read for preview. Manually-pinned proxies are out of scope for MVP; if needed, a follow-up MAY add per-clip "do not evict" pinning.
- **FR-018b**: When eviction triggers, the system MUST surface a non-blocking notification including how many proxies were removed and how much disk was reclaimed, so the user understands why a previously-cached clip now needs regeneration.

**Non-goals (explicit, to bound scope)**

- **FR-019**: The system is NOT required to auto-generate proxies on import for the MVP — explicit user action (per FR-005) is sufficient. Auto-on-import MAY be added as a follow-up setting. Likewise, the system MUST NOT auto-queue regeneration when a proxy is detected stale; stale proxies are surfaced in the UI and the user explicitly triggers regeneration. Auto-regeneration on staleness is a follow-up setting, not an MVP requirement.
- **FR-020**: The system is NOT required to proxy audio-only clips, procedural/generative sources, or still images; these MAY be skipped silently.

### Key Entities

- **Source media reference**: the project's existing handle to an on-disk video source file. Identity key for proxies is the triple `(absolute path, file size, mtime)`. Content hashing is not used at MVP.
- **Proxy**: a derived, lower-cost representation of a single source media reference. Has its own file on disk, a generation-time fingerprint of the source it was derived from, a quality preset, and a state (generating / ready / stale / failed / partial).
- **Proxy job**: a background unit of work that turns one source media reference into one proxy file. Has progress, status, cancel handle, and an error if failed.
- **Preview source mode**: a project-level setting that selects "prefer proxy" vs. "force original" for the preview pipeline. Does not exist for the export pipeline (export is always original).
- **Proxy store**: the on-disk location holding proxy files plus their fingerprint metadata. Configurable location; shared across projects on the same machine when possible. Subject to a configurable global LRU cap (FR-018a) with a per-entry "last used" timestamp. Per-entry metadata at minimum: `(absolute path, size, mtime)` of the source at generation time, chosen preset, last-used timestamp, state (generating / ready / stale / failed / partial).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a project with at least one 4K source clip (≥3840×2160, ≥60 Mbps), scrubbing and playback in the editor preview after proxies are generated is **at least 3× faster in frame-decode time** than scrubbing the same project with proxies disabled, on the same machine.
- **SC-002**: Exporting a project that has proxies generated produces a file whose every frame is bit-identical (or, when re-encoded, visually indistinguishable at the chosen export quality) to the same export run with all proxies deleted — i.e., proxies provably do not contaminate the export.
- **SC-003**: Switching the project-level preview source between "Proxy" and "Original" takes effect on the next preview frame (no project reload required), within **2 seconds** on a project of ≤500 clips. Under 003 this toggle changes the affected sources' `EffectiveScale`, so the render cache MUST be invalidated for those sources (FR-023) and the next frame re-rendered — the toggle does **not** trigger a `SceneRenderer` rebuild (the render scale is unchanged).
- **SC-004**: With proxies generated and preview source set to "Proxy", playback of a heavy timeline (≥3 simultaneous 4K layers) stays at the project frame rate (no dropped frames) on a machine that drops frames at the same point with proxies disabled.
- **SC-005**: A user new to the feature can, within **2 minutes** of opening the project and with no help beyond the in-app UI, **discover how to initiate proxy generation** for a clip and — once generation completes — **confirm via the UI that preview is served from the proxy**. The 2-minute bound scopes only the interaction/onboarding portion (finding the control, starting generation, reading the proxy-state badge); the background encode itself is explicitly excluded because it is an open-ended background job (FR-007) whose wall-clock scales with source size and can exceed 2 minutes for a genuinely heavy clip (SC-001's ≥4K/≥60 Mbps target). For a bounded, falsifiable measurement, use a lightweight reference clip rather than arbitrary heavy footage. *Verification*: quickstart.md §3 (generate a proxy on the lightweight `$SRC_SMALL`) then §4 (confirm preview uses that proxy) — the discoverability path, not the heavy-encode path.
- **SC-006**: When a proxy is missing, stale, or partial, preview falls back to the original with **zero playback errors surfaced to the user** in 100% of cases across the edge-cases listed above.

## Assumptions

- **This feature builds on the now-implemented resolution-independent pipeline** (spec `003-resolution-independent-pipeline`, status: implemented). 003 introduced a supply-driven scale model: every render operation reports an `EffectiveScale` (supply density), and every effect/buffer boundary runs at a working scale `w = min(max(s_out, densest concrete supply), MaxWorkingScale)` — supply-driven on the high side, floored at `s_out`, capped by a per-request working-scale ceiling (`2 × s_out` in preview, `+∞` at export); the ceiling is inert for the sub-output proxies this feature produces, so the floor rule is what governs them. 003 also split a media source's **logical footprint** from its **decoded pixel size** as a deliberate seam (003 FR-023/FR-024), and explicitly deferred the "stable intrinsic-logical-size channel" to the proxy feature (see `003/data-model.md` "003 scope note"). Proxy media consumes exactly that seam: a proxy is a **lower-density supply** — a smaller decoded bitmap with the *same* logical footprint — which lowers the working scale, and therefore the decode + effect-raster cost, of preview. 003 already made **vector / text / Skia-filter** preview cheaper through its preview render scale (`Full`/`Half`/`Quarter`); proxies add value precisely where 003 deliberately does **not** reduce cost — **source-heavy** scenes (a high-resolution video/image feeding effects), whose supply density 003 preserves end-to-end.
- The user-facing flow uses **explicit, user-initiated proxy generation** for the MVP. Auto-generate-on-import is a follow-up setting, not a launch requirement (see FR-019). Reason: it bounds scope, sidesteps "Beutl ate my disk on import" complaints, and matches the common pattern in comparable editors (DaVinci Resolve, Premiere Pro) where auto-on-import is opt-in.
- Proxy storage **defaults to a per-user application cache directory** (not inside the project folder). This keeps projects portable and avoids accidentally committing large proxy files to project-folder-aware tooling. Users can override per FR-016.
- Proxy identity is keyed on **source content fingerprint** (path + size + mtime, or a content hash if cheap), not on the project-level clip object. Two timeline clips referencing the same file share one proxy on disk.
- Proxy generation **uses the existing media pipeline** with Engine-side media abstractions plus the existing FFmpeg extension / IPC encoder for decode + encode. No new IPC protocol is assumed at the spec level; planning keeps `Beutl.Engine` free of any reverse reference to `Beutl.Extensions.FFmpeg` or `Beutl.FFmpegIpc`.
- The **export path already routes through a distinct render code path** from the preview path. The feature relies on this separation: preview opts into proxy, export does not. If the assumption is wrong (preview and export share a single media-resolution layer), the planning phase must call that out as a structural change.
- "Editing" includes all in-editor preview surfaces (timeline scrub, playback, thumbnails); "export" includes the final-render path and any "preview render" that the user explicitly treats as deliverable output. In-editor preview never feeds an export.
- Proxies are **derived data**, not project assets — losing the proxy cache must not corrupt or alter a project, only slow editing until proxies are regenerated.
- The feature targets **video** as the primary beneficiary; **still images are out of scope for MVP proxying**. Still images decode once via `Bitmap.FromStream` (a single load, not a per-frame decode) and load through `Media.Source.ImageSource`, which **bypasses `DecoderRegistry.OpenMediaFile`** entirely — so neither the preview-routing seam (FR-001) nor the supply-density seam (FR-022) covers them, and a proxy would yield negligible preview benefit. (A follow-up MAY add still-image proxying by teaching `ImageSource` to consult `IProxyResolver`, but that is a separate code path, not this MVP.) Audio is out of scope for proxy generation in the MVP.
