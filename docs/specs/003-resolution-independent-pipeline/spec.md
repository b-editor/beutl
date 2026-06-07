# Feature Specification: Resolution-Independent Rendering Pipeline

**Feature Branch**: `speckit/003-resolution-independent-pipeline`

**Created**: 2026-05-30

**Status**: Draft

**Input**: User description: "将来的にプロキシワークフロー（最適化メディア）を実装したいので、解像度に依存しない描画パイプラインを実装したい。すべてのプロパティを論理サイズにして、RenderNodeOperation にスケールを持たせ、最終的な部分でスケールを調整する。複数のスケールの RenderNodeOperation が混ざるケースやそれぞれの全てのエフェクトについての対応を詳しく調べること。"

> **Supporting research**: the exhaustive technical investigation backing this spec — the pixel-coupling inventory, the per-effect scale matrix (every effect), the mixed-scale compositing analysis, and the risk register — lives in [`notes/rendering-analysis.md`](./notes/rendering-analysis.md). This spec states *what* and *why*; that dossier captures *where in the code* and *how much*.

## Overview

Today Beutl's 2D rendering pipeline has **no concept of render scale**. The invariant `1 logical unit == 1 device pixel` is baked implicitly across the render path (materialized as literal `ToSize(1)`, `(int)bounds.Width`, and `PixelRect.FromRect(bounds)` calls). Every drawable property, transform, effect parameter, brush, pen, and glyph is rendered as if the project canvas were always at its full export resolution.

This feature makes the pipeline **resolution-independent**: drawable and effect properties become **logical** sizes, a single **render scale** factor flows through the render-node tree, every pixel-magnitude parameter is multiplied by that scale at the leaves that touch device pixels, and the final stage normalizes to the actual output resolution. This unlocks rendering the *same project* at different resolutions — a reduced-scale preview for cheap editing, a full-scale export for delivery — and lays the **foundation for a future proxy / optimized-media workflow** without committing to the decoder-level changes in this feature.

## Scope

**In scope (this feature):**

- Logical-coordinate definition and a render-scale that propagates through the whole 2D render-node tree.
- A uniform scale contract for every effect, brush, pen, and text: pixel-magnitude (spatial) parameters are multiplied by the render scale; magnitude-invariant parameters are left unchanged.
- Mixed-scale compositing: subtrees rasterized at different scales composite correctly.
- Scale-aware render cache, backdrop/snapshot, and **every independent nested-raster path**: nested scenes, `DrawableBrush`, the particle renderer (which today hard-codes a fixed buffer size), and audio-visualizer drawables.
- Decoupling a media source's **logical** size from its **decoded pixel** size, and keeping `MediaOptions` additively extensible so proxy decode can be added later **without** changing it now.
- Editor hit-testing / handles remain correct and identical across preview scales.
- A **preview render-scale control** — a fixed enum (Full / Half / Quarter / Fit-to-previewer), held as per-edit-view session state and never persisted.
- **Export supersampling anti-aliasing** (`s > 1`): render above the output resolution and downscale for AA on export.

**Out of scope (deliberately deferred to a follow-up feature):**

- Actual **decoder-level reduced decode** (requesting a smaller frame from the FFmpeg worker over the GPL/MIT IPC boundary) and the proxy-media file lifecycle. This feature builds the render-scale plumbing and the implicit "proxy-via-FrameSize" reconciliation path; it must not foreclose decoder-level proxy, but it does not implement it.
- Anisotropic (non-uniform / anamorphic) render scale — v1 is uniform; storage types are chosen so they can widen later.
- **Deep 3D integration** — making `Scene3DRenderNode` render its internal 3D scene at the 2D render scale *in lockstep*. In v1 the 3D surface is treated as an ordinary mixed-scale op (resampled at the composite boundary, FR-033); lockstep 3D scaling is deferred unless the maintainer pulls it in (Open Question 6).

## Clarifications

### Session 2026-05-30

- Q: Is supersampling (render scale `s > 1`) a delivered 003 feature, or only "must not break"? → A: Deliver **export supersampling anti-aliasing** in 003 — render at `s > 1` and downscale to the output resolution for export (FR-034, SC-009).
- Q: How is the preview render scale exposed (value domain)? → A: A **fixed enum** — Full (1.0), Half (0.5), Quarter (0.25), plus **Fit-to-previewer** (derived, clamped ≤ 1.0) — reusing the existing `FrameCacheConfigScale` vocabulary; the fixed options bound the test surface, while Fit-to-previewer may be fractional and must still render correctly (FR-035).
- Q: Where is the preview render scale persisted? → A: **Per-edit-view (tab) session state only**; never persisted to the project file or app settings (FR-035), preserving SC-002.
- Q: Acceptance metric for reduced-scale "exact" effects? → A: **Exact byte-equality gate at scale 1.0** (SC-001) plus an **SSIM threshold** (number pinned in `/speckit-plan`) for reduced-scale "exact" effects (SC-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Faster preview that stays faithful at export (Priority: P1)

A video editor working on a heavy scene switches the preview to a reduced render scale (e.g. 0.5×). The editor canvas renders noticeably faster because the whole frame — shapes, text, gradients, and effects — is rasterized at half resolution. When the editor exports at full scale, the delivered frames are **identical to what Beutl produces today**; the reduced-scale preview changed only the preview, never the document or the export.

**Why this priority**: This is the core deliverable and the immediate user-visible payoff. It is also the regression anchor for the whole feature — "export at scale 1.0 is unchanged" guards every other change.

**Independent Test**: Render a representative vector + Skia-filter scene at scale 0.5×, upscale the result, and compare to the scale-1.0 render within tolerance. Separately, render at scale 1.0 and assert byte-identical (within encoder tolerance) output versus the pre-feature renderer. Measure render-stage time reduction at reduced scale.

**Acceptance Scenarios**:

1. **Given** a scene rendered at full scale today, **When** the same scene is exported with render scale 1.0 after this feature, **Then** the output is byte-identical (within encoder tolerance).
2. **Given** a vector + Skia-filter scene, **When** previewed at scale 0.5×, **Then** the render stage completes faster and the upscaled preview matches the full-scale render within the defined perceptual tolerance.
3. **Given** any reduced preview scale, **When** the user inspects the saved project file, **Then** all stored property values are unchanged (preview scale is a render-request property, never persisted into the document).

---

### User Story 2 - Resolution-independent properties with correct per-effect and mixed-scale behavior (Priority: P1)

Every drawable and effect property is a **logical** size. A blur of "10" looks like the same blur whether the frame is rendered at full scale or half scale — the engine multiplies the blur's pixel-magnitude parameters by the render scale. When a full-resolution shape is composited over a half-resolution nested scene (or, later, a proxy video), the result composites correctly at the higher scale rather than dragging the sharp content down.

**Why this priority**: Without a correct, uniform per-effect contract and a defined mixed-scale rule, reduced-scale rendering produces visibly wrong output (clipped blurs, detached shadows, mis-sized mosaics, soft sharp content). The maintainer explicitly asked that *every* effect and the mixed-scale case be handled.

**Independent Test**: For each built-in effect, render at scale 1.0 and at scale 0.5× (upscaled) and compare within tolerance ("exact" effects) or assert documented best-effort behavior ("resolution-sensitive" effects). For mixed-scale, build a tree with ops of differing effective scale and assert the composite matches a full-scale reference within tolerance, with no seams or registration drift beyond the rounding tolerance.

**Acceptance Scenarios**:

1. **Given** an effect with a spatial-length parameter (blur sigma, shadow offset, dilate radius, mosaic tile size, color-shift offset, stroke thickness), **When** rendered at scale s, **Then** that parameter's effect is scaled by s and magnitude-invariant parameters (color, angle, percentage, ratio, relative coordinate, blend mode, count) are unchanged.
2. **Given** ops with different effective scales in one container, **When** composited, **Then** compositing happens in logical space at the **maximum concrete child scale** (lossless ops regenerate at the target; the output-scale cap applies only at the final root normalization or an explicit `ClampToOutput`), with off-target bitmap ops resampled (Mitchell) exactly once at the blit boundary.
3. **Given** an inherently resolution-sensitive effect (e.g. PixelSort, contour-based stroke/flat-shadow/parts-split, AutoClip, mosaic, custom SKSL/GLSL shader), **When** previewed at reduced scale, **Then** its pixel-magnitude parameters are still multiplied by the scale and the reduced-scale result is accepted as a best-effort approximation, while at export scale 1.0 it is full-fidelity.

---

### User Story 3 - Proxy-ready foundation (Priority: P2)

The pipeline cleanly separates three things that are conflated today: a source's **logical** size, its **decoded pixel** size, and the **render scale**. A media source reports the same logical size (and therefore the same layout, bounds, and hit region) regardless of whether it is backed by full-resolution or (in a future feature) reduced-resolution media. The media-open path stays additively extensible so a decode-scale hint can later be threaded to the decoder without reshaping the API.

**Why this priority**: This is the stated motivation ("将来的にプロキシワークフロー"). Getting the seams right now is what makes the future proxy feature a localized addition rather than another pipeline rewrite.

**Independent Test**: At a fixed logical size, back a source with two different pixel-resolution bitmaps of the same content; assert the drawable's logical bounds and hit region are identical and only the op's `EffectiveScale` differs (the seam). Assert `MediaOptions` (and the decode path) compiles and behaves identically with no decode-scale hint supplied (default = native), proving the extension point is additive. (Deriving a *stable* logical size from a source independent of its decoded resolution is deferred with proxy decode.)

**Acceptance Scenarios**:

1. **Given** a media source at a fixed logical size, **When** it is rendered, **Then** the decoded bitmap's resolution appears only as the op's `EffectiveScale` (supply density), not as a change to the logical bounds — the seam that lets a future proxy swap resolution without moving content.
2. **Given** the media-open path, **When** no decode-scale hint is supplied, **Then** behavior is identical to today (native-resolution decode) and the document is unaffected.
3. **Given** a source drawn into a frame at render scale s, **When** rendered, **Then** its decoded pixels are mapped into a logical destination rect of `logicalSize × s`, so swapping the backing media changes only resolution, not position or size.

---

### User Story 4 - Editor interactions identical across preview scales (Priority: P2)

Selecting a layer, dragging a transform handle, and hit-testing all behave identically no matter what preview render scale is active. A handle drag produces the same document `Transform` values at 1.0× and 0.5×; clicking the same on-canvas point selects the same drawable.

**Why this priority**: A reduced-scale preview is unusable if it shifts where clicks land or corrupts the values produced by editing. The render scale must be invisible to the editing model.

**Independent Test**: Hit-test the same logical point at two render scales and assert the same drawable is returned; perform an equivalent handle drag at two render scales and assert identical resulting document Transform values.

**Acceptance Scenarios**:

1. **Given** a preview at reduced scale, **When** the user clicks a point over a drawable, **Then** the same drawable is hit as at full scale (hit-testing runs in logical space; the pointer is divided by display-zoom only, not by render scale).
2. **Given** a transform-handle drag, **When** performed at any preview scale, **Then** the resulting serialized Transform values are identical across scales.

---

### Edge Cases

- **scale = 1.0** must be the exact present-day path: the **raw rendered frame** is byte-identical to today (encoder tolerance applies only to the encoded stream) — it is the regression guard. The shared rounding helper (FR-007) MUST reproduce the current asymmetric `PixelRect.FromRect` rounding — **origin toward zero (the existing `(int)` cast, NOT floor); extent ceil** — at scale 1.0.
- **Fractional / non-integer device sizes**: `FrameSize × s` rarely lands on integers. Sizes round up (ceil); origins truncate **toward zero** (the existing `(int)` cast, NOT floor — they differ for the negative origins common after blur/shadow inflation), via one shared helper, so sub-pixel error does not compound across nested scaled rasterizations and effect chains (e.g. blur/drop-shadow inflate bounds by `sigma × 3`).
- **Render cache across a scale change**: a tile cached at scale A must never be blitted 1:1 into a scale-B pass. Scale is part of cache identity; a scale change invalidates (or resamples) cached tiles.
- **Independent nested-raster paths** — nested scenes, `DrawableBrush`, the particle renderer (a hard-coded fixed buffer today), audio-visualizer drawables, and 3D sub-renders — each currently render at their own independent resolution and composite 1:1; under a global render scale they must inherit the outer scale (or be resampled at the blit boundary).
- **Render-scale change concurrent with rendering**: rendering is dispatcher-affine and export frames are produced on a background task; a scale change and its cache invalidation must be applied atomically on the render dispatcher so no frame is composited from mixed-scale stale state.
- **Inherently resolution-sensitive effects** cannot be bit-identical at reduced scale; their reduced-scale preview is best-effort by parameter scaling, full-fidelity at export.
- **Export buffer size**: the encoder's source size must be derived from the actual rendered surface size, asserted equal before encode, so a scale change cannot cause a stride/size mismatch.
- **Supersampling (s > 1, export)**: export may render at `s > 1` and downscale to the output resolution for anti-aliasing (FR-034) — the one case that reintroduces an explicit final-resample stage. `s > 1` is a first-class export path, not merely "must not break".
- **Text at reduced scale**: glyphs must be **re-shaped** at the device scale, not matrix- or bitmap-scaled (hinting bakes resolution-specific grid-fitting).

## Requirements *(mandatory)*

### Functional Requirements

**Coordinate model & scale**

- **FR-001**: The system MUST define `1 logical unit = 1 device pixel at the project FrameSize` (render scale 1.0). All drawable and effect properties are interpreted in logical units. Existing project files require **no migration** and **no format version bump**.
- **FR-002**: The system MUST treat **render scale** as a property of the *render request*, distinct from (a) the logical frame size (`Scene.FrameSize`), (b) the editor display zoom, (c) the existing `FrameCacheConfigScale` (which downsizes already-rendered cache bitmaps for memory), and (d) any artistic `ScaleTransform`. None of these may be conflated.
- **FR-003**: The **root frame** device surface size MUST be `ceil(FrameSize × renderScale)`, chosen in exactly one place. Intermediate buffers (effect targets, brush/tile intermediates, nested-render and cache tiles) are sized at *their* target scale per FR-018/FR-019, not at the root size — FR-003 governs only the root frame buffer.
- **FR-004**: Render scale MUST propagate through the entire render-node tree so every node and operation that allocates a pixel buffer or reads integer pixel dimensions sees the active scale. A render operation MUST expose the scale it was actually rasterized at (its *effective scale*) for reconciliation.
- **FR-005**: Rendering at scale 1.0 MUST produce the **raw rendered frame** (the pre-encode `Bitmap` / frame buffer read back from the render surface) byte-identical to the pre-feature renderer; the "within encoder tolerance" allowance applies only to the *encoded* delivery stream, not to this raw-frame comparison. Scaling logic MUST affect only the `scale ≠ 1.0` paths. (The golden comparisons in SC-001/SC-004 target the raw frame buffer.)
- **FR-006**: Render scale v1 MUST be **uniform** (single factor). Storage and signatures SHOULD be chosen so an anisotropic (vector) scale can be introduced later without re-plumbing.
- **FR-007**: All logical→device rounding MUST go through one shared helper that applies the `× w` scaling with a consistent convention (ceil for sizes; **toward-zero truncation for origins** — NOT floor — matching `PixelRect.FromRect`/`PixelPoint.FromPoint`). **At `w = 1.0` each existing sink MUST preserve its current integer rounding** (the main rasterization sink already uses `PixelRect.FromRect`, unchanged at scale 1; the two filter-effect sinks keep their component-wise `(int)Width`/`(int)Height` truncation) so byte-identity (FR-005) holds; the shared `× w` convention governs only `w ≠ 1.0`. The helper is parameterized to reproduce each site's `w = 1.0` rounding, not to unify them at scale 1.

**Per-effect / brush / pen / text scale contract**

- **FR-008**: The system MUST apply a single uniform contract: every **spatial-length / pixel-magnitude** parameter is multiplied by the effect's **working scale `w`** (the supply-driven scale it actually runs at — FR-036, NOT the output scale `s_out`); every **magnitude-invariant** parameter (color, angle, percentage, ratio, relative coordinate, 0..1 value, blend mode, count, enum) is left unchanged.
- **FR-009**: For each built-in effect, the system MUST scale exactly the parameters enumerated in the per-effect matrix in `notes/rendering-analysis.md`, including parameters that also drive bounds math (e.g. a blur's `sigma × 3` inflation MUST use the scaled sigma). Pixel-magnitude scaling SHOULD be centralized in the effect context primitives so forwarding effects inherit it. The dossier matrix is **not yet exhaustive** — it omits the particle and audio-visualizer property sets (see FR-029/FR-030); `/speckit-plan` MUST complete it into a per-item test manifest before treating it as the source of truth. Each built-in effect MUST also declare a **resolution policy** (FR-036) in that manifest, and the chosen policy MUST NOT change the `s_out = 1.0` output (golden-gated).
- **FR-010**: Brush parameters MUST follow the contract: relative/percentage/0..1 brush parameters are unchanged (only the bounds they resolve against are device-scaled), while absolute parameters are scaled — specifically `PerlinNoiseBrush.BaseFrequency` is divided by the scale (period invariant in logical units), and tile/image/drawable intermediate raster resolution is multiplied by the scale.
- **FR-011**: Pen stroke width, offset, and dash lengths MUST scale with render scale so strokes look identical at any scale; `MiterLimit`, caps/joins/alignment, and `Trim*` are unchanged. Cached stroke geometry MUST remain correct across render scales and MUST NOT reuse a stale-scale path — satisfied **either** by a scale-invariant logical outline (no scale in the cache key) **or** by keying the cache on scale. (The chosen design outlines strokes in logical space and scales them via the canvas transform, so the cached outline is scale-invariant and `PenHelper` is unchanged — research.md D3.)
- **FR-012**: Text MUST be **re-shaped** at the device scale (font size, spacing, stroke thickness, and inline rich-text overrides scaled together); matrix-scaling or bitmap-upscaling shaped text is NOT permitted. The text shaping cache MUST be scale-aware. Hit-test paths (fill/stroke geometry) MUST stay in logical space.
- **FR-013**: Inherently resolution-sensitive effects (PixelSort, contour-based StrokeEffect / FlatShadow / PartsSplitEffect, AutoClip, integer-structuring Dilate/Erode, MosaicEffect, PerlinNoise-driven effects, and custom SKSL/GLSL shaders) MUST still apply the parameter-scaling contract; their reduced-scale preview MAY differ from the full-scale result and that approximation is accepted. Full fidelity MUST hold at export scale 1.0. (No force-full-scale subtree mechanism and no mismatch-warning UI are required for v1.) These effects rely on the default `Inherit` policy (FR-036) so a higher-resolution source feeding them is kept through the effect and downsampled only at the final stage; declaring `ClampToOutput` on them is forbidden (it would discard the resolution they exist to use). *(An earlier draft required them to declare `PreserveSource`; that policy was removed as it was identical to `Inherit`.)*
- **FR-014**: Custom-shader effects MUST keep their existing uniforms — SKSL `width`/`height`/`iResolution`/`fragCoord`, GLSL `Width`/`Height` push constants — meaning the **device** size of the *scaled* target, so existing shaders keep working (unchanged at scale 1.0; at reduced scale they simply see the smaller device size). The system MUST ADD a new, explicitly-named scale uniform (e.g. `iScale`/`uScale`) carrying the active render scale so author code can scale absolute-pixel literals; the new uniform's name and the backward-compatibility rule (scale-unaware shaders behave as scale 1.0, i.e. device == logical) MUST be documented in the published shader contract. The existing uniforms' device-pixel meaning MUST NOT be silently redefined to logical.
- **FR-015**: Script effects (C#) and the public effect-context API MUST give authors a documented way to read the active render scale, so an author can write scale-correct pixel constants. The chosen mechanism is part of the published extensibility contract.

**Mixed-scale compositing**

- **FR-016**: Compositing MUST happen in logical coordinate space, enforced at a single point (the render processor). When a container gathers child operations of heterogeneous effective scale, it MUST composite at `target = max(concrete child effective scales)` (lossless / `Unbounded` children are excluded from the max — they regenerate at the target). The output-scale cap applies **only at the root composite (final normalization)** or where an effect/subtree opts into `ClampToOutput` (FR-036) — an intermediate composite MAY exceed `s_out` (a preserved high-resolution source / SSAA) and MAY stay below `s_out` (a preserved proxy).
- **FR-017**: An operation whose effective scale differs from the composite target MUST be reconciled exactly once, at the blit boundary where it enters the composite: a lossless (`Unbounded`) op is **regenerated** at the target; a bitmap op is **resampled** (Mitchell). Never per-effect and never per-child repeatedly.
- **FR-018**: Each render operation MUST expose its **effective scale** as either **`Unbounded`** (vector content — shapes, geometry, text, Skia-filter results — re-rasterizable at any target) or a **concrete density** (images, video, decoded/proxy media, cached tiles, snapshots). `Unbounded` ops are regenerated at the composite target; concrete-scale ops are resampled (FR-017) and are never requested above their own density. The compositor keys on this value, not on a hard-coded type list. *(Replaces the earlier separate `LosslessReRasterizable` boolean — `Unbounded` subsumes it.)*
- **FR-019**: Effect intermediates MUST carry a per-target **effective scale** (FR-018) so divergent-scale inputs are normalized to the negotiated working scale `w` exactly once (FR-017) before any shared filter, union, or flatten step (covering LayerEffect, DelayAnimationEffect, InnerShadow/Blend/Mosaic custom targets). Today's scale-blind `Union` of mixed-density targets is a correctness bug this fixes.

**Working-scale negotiation (supply-driven)**

- **FR-036**: Each buffer-allocating boundary (effect, container, sink) MUST compute a **working scale `w`** from its inputs' effective scales and a declared **resolution policy**, and run/allocate at `w` — NOT at the output scale `s_out`. Policies: **`Inherit`** (default — `w` = the max concrete input density: a 0.5 proxy stays 0.5, a 2.0 source stays 2.0; `s_out` is **not** a ceiling), **`ClampToOutput`** (`w = min(supply, s_out)` — perf opt-out for heavy effects), **`Oversample(k)`** (`w ≥ k × s_out` — quality/SSAA opt-in even from a low input). `s_out` MUST NOT clamp an intermediate except via an explicit `ClampToOutput`. **As shipped, every built-in uses the default `Inherit`** (which already keeps a high source's density). *(A `PreserveSource` policy + cross-boundary floor was specced but removed — it was identical to `Inherit`.)* Out-of-tree effects default to `Inherit` and MUST render byte-identically at `s_out = 1.0`.
- **FR-037**: A configurable **global working-scale ceiling** MUST cap `w` (`w = min(w, MaxWorkingScale)`) so no combination of `Inherit` / `Oversample` / preserved high-resolution sources can exceed a bounded worst-case preview memory. The ceiling caps only the high side (it never reduces `w` below a proxy's supply) and MUST be inert at `s_out = 1.0` with unit-scale inputs (byte-identity preserved). It is a **per-render-request** value — low for preview, effectively unbounded for export — so a high-resolution source stays **full-fidelity at delivery** (FR-013). The **default preview ceiling is `2 × s_out`** (seeded at the editor preview `SceneRenderer`); **export uses no ceiling** (`+∞`), so a 4K source exports at full fidelity. *(As shipped this only caps a plugin `Oversample` — no built-in resolves a working scale above `s_out`.)*

**Caching, backdrop, nested renders**

- **FR-020**: The render cache MUST include render scale in its identity and MUST invalidate (or resample) on scale change; cache-size thresholds MUST be expressed so the same logical content is treated consistently across scales. When scale is added to any node or cache entry, it MUST participate in change-detection (`Update`/`Equals`/`GetHashCode`) so graph diffing does not silently skip updates.
- **FR-021**: Backdrop bounds and snapshot capture MUST be scale-aware; a captured snapshot MUST be tagged with its capture scale and resampled (or re-captured) on mismatch.
- **FR-022**: Nested scenes and `DrawableBrush` child subtrees MUST inherit the outer render scale rather than rendering at an independent fixed resolution. The particle renderer (FR-029) and audio-visualizer drawables (FR-030) fall under the same inherit-the-scale rule; 3D sub-renders are handled per FR-033.

**Media source decoupling (proxy foundation)**

- **FR-023**: The media render path MUST map a source's **logical** size to its **decoded pixel** size through an explicit destination rect (not a 1:1 native blit), so the decoded resolution is a *supply* density (the op's `EffectiveScale`) distinct from the drawable's logical footprint. **In 003, a source's logical size is still derived from its (full) decoded `FrameSize`** — 003 delivers the *seam* (the dest-rect mapping + `EffectiveScale` on the source op), NOT a separate stable intrinsic-logical-size channel, so a **future** reduced-decode supply (a smaller decoded bitmap with an unchanged logical footprint) drops in without further pipeline change.
- **FR-024**: Decoded source pixels MUST be drawn into a logical destination rect (mapping `logicalSize` to the op's working scale, not a 1:1 native-pixel blit), and the source op MUST report `EffectiveScale = decodedPixels / logicalSize` so the compositor reconciles it via the supply-driven rule (FR-016/FR-036).
- **FR-025**: `MediaOptions` and the media-open path MUST remain additively extensible for a future optional decode-target-size/scale hint (default = native). This feature MUST NOT add the decode-scale hint to the FFmpeg IPC protocol or worker, but MUST NOT structurally foreclose it.

**Output, editor, and API**

- **FR-026**: The export encoder's source size MUST be derived from the size of the buffer actually handed to the encoder (the post-supersample-downscale output buffer when `s > 1`, per FR-034) and asserted equal to that buffer before encoding, so a scale change cannot cause a stride / size mismatch.
- **FR-027**: Hit-testing and transform-handle math MUST run in logical space, independent of render scale; the editor pointer MUST be divided by display zoom only. `Matrix` decomposition used by editor gizmos and serialization MUST remain invariant across render scales (render scale MUST NOT be folded into the artistic transform matrix).
- **FR-028**: Public-surface changes (render context/operation, effect context, effect target, renderer / scene-renderer / graphics-context constructors) MUST ship as a breaking change (`refactor!:` / `feat!:` with a `BREAKING CHANGE:` footer naming affected projects), with all in-tree call sites updated in the same change and no `[Obsolete]` compatibility shims. Changes to the published extensibility surface MUST be routed through `beutl-design-reviewer`.

**Completeness, concurrency & invalidation** *(added after independent code-verification review)*

- **FR-029**: The particle renderer (`ParticleRenderNode`, which today allocates a hard-coded fixed-size buffer and rasterizes unscaled) MUST honor the active render scale like any other nested-raster path: its intermediate buffer is sized at the target scale and its pixel-magnitude particle properties follow the FR-008 contract.
- **FR-030**: Audio-visualizer drawables (waveform / spectrum shapes — e.g. bar width, block gap, and any hard-coded pixel minimums) MUST classify their parameters under the FR-008 contract (spatial-length params scale; magnitude-invariant params do not) and render correctly at reduced scale.
- **FR-031**: A render-scale change and the accompanying cache invalidation MUST be applied **atomically on the render dispatcher**, so the dispatcher-affine renderer and the background export path never composite a frame from mixed-scale stale state.
- **FR-032**: Render scale MUST be an explicit dimension of render-graph invalidation. Because scale is not an `EngineObject` property and does not bump `Resource.Version`, the system MUST fold the active scale into the cache / invalidation key so a scale change forces affected nodes (cached tiles, shaped text, effect intermediates) to re-measure / re-rasterize. Any source-generated resource field or cache key that becomes scale-dependent MUST be updated in the generator with matching `tests/SourceGeneratorTest` coverage.
- **FR-033**: 3D sub-renders (`Scene3DRenderNode`) MUST at minimum be handled correctly as mixed-scale ops (resampled to the composite target per FR-016/FR-017). Whether the 3D internal render additionally tracks the 2D render scale in lockstep (quality / perf) is deferred (Out of scope; Open Question 6); the expected reduced-scale preview mismatch MUST be documented.

**Preview & export scale surface** *(clarified 2026-05-30)*

- **FR-034**: For **export**, the system MUST support supersampling (`s > 1`): render the frame at `ceil(FrameSize × s)` and downscale to the output resolution as a final resample for anti-aliasing. This is the only path that requires an explicit final-resample stage; preview (`s ≤ 1`) requires none. The encoder still receives a buffer at the output resolution (FR-026).
- **FR-035**: The **preview** render scale MUST be exposed as the fixed options Full (1.0), Half (0.5), Quarter (0.25), and a Fit-to-previewer mode that derives the scale from the preview surface size (clamped to ≤ 1.0). The fixed options bound the primary test surface; the Fit-to-previewer mode MAY yield a fractional scale that the pipeline MUST still render correctly. This reuses the `FrameCacheConfigScale` vocabulary but is a **distinct axis** from it (FR-002). The preview render scale is **per-edit-view session state** and MUST NOT be persisted to the project file or conflated with the persisted `FrameCacheConfigScale`.

### Key Entities

- **Output scale (`s_out`)**: a uniform factor (default 1.0) on a render request; the **final normalization target only** — it never clamps an intermediate effect (FR-036). **Preview** uses a fixed enum — Full (1.0) / Half (0.5) / Quarter (0.25) / Fit-to-previewer (derived, ≤ 1.0) — held as **per-edit-view session state**, never persisted (FR-035). **Export** uses 1.0, or `s_out > 1` for supersampled anti-aliasing with a final downscale to the output resolution (FR-034).
- **Working scale (`w`)**: the supply-driven scale a given effect / boundary actually rasterizes at, computed from its inputs' effective scales and resolution policy (FR-036) and capped by the global ceiling (FR-037). Spatial-length parameters multiply by `w` (FR-008).
- **Logical frame size**: the resolution-independent project canvas (`Scene.FrameSize`), the unit anchor (`1 unit = 1 px at this size`).
- **Device target size**: `ceil(FrameSize × s)`; the sole physical pixel buffer size of the main frame.
- **Render-node context scale**: the top-down channel carrying the active scale to every node's allocation decisions.
- **Effective scale (`EffectiveScale`)**: a read-only value on each operation = the density its pixels exist at; **`Unbounded`** for vector / lossless ops (regenerate at any target), `At(scale)` for bitmap ops. Flows bottom-up; the compositor reconciles mismatches against the working scale (FR-018).
- **Effect target scale**: a per-target scale on effect intermediates enabling mixed-scale normalization before shared filter/flatten steps.
- **Effect scale contract**: the uniform rule (multiply spatial-length parameters by the working scale `w`; leave magnitude-invariant parameters unchanged) that every effect, brush, pen, and text path follows.
- **Resolution policy**: a per-effect / per-node declaration (`Inherit` default / `ClampToOutput` / `Oversample(k)` / `PreserveSource`) that drives the working-scale computation (FR-036).
- **Media logical size vs decoded pixel size**: two distinct dimensions of a source; the foundation that lets a future proxy decode at reduced resolution while layout stays fixed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Rendering any existing project at render scale 1.0 produces a **raw frame buffer** byte-identical to the pre-feature renderer (an encoded-stream comparison, if run, uses the encoder's tolerance), verified by golden-image regression across a representative project set that includes particles, audio visualizers, text, nested scenes, and a 3D scene.
- **SC-002**: Existing project files load and render unchanged with **zero** migration steps and no file-format version change.
- **SC-003**: For a defined benchmark scene (fixed scene file, fixed hardware / GPU baseline, warm cache excluded, render-stage time only — not decode or encode), the render stage at scale 0.5× completes in meaningfully smaller wall-clock time than at scale 1.0 (target: the rasterization-bound portion approaches ~¼). The benchmark scene, hardware baseline, and whether cache hits count MUST be pinned in `/speckit-plan`.
- **SC-004**: A per-effect / per-property **test manifest** (the completed FR-009 matrix, including particles and audio visualizers) classifies every item as "exact" or "best-effort" with its scaled params, invariant params, and acceptance threshold. Scale 1.0 is an **exact byte-equality gate** (per SC-001). Every "exact" item matches its scale-1.0 reference (rendered at scale 0.5× and upscaled) within an **SSIM threshold**; every "best-effort" item has a documented, asserted behavior and is full-fidelity at scale 1.0. The exact / best-effort lists and the SSIM threshold value MUST be pinned in `/speckit-plan`.
- **SC-005**: A mixed-scale scenario (full-scale content composited over reduced-scale nested content) composites without visible seams or registration drift beyond the rounding tolerance, verified against a full-scale reference.
- **SC-006**: Hit-testing the same logical point and performing an equivalent handle drag at two different render scales yield the same selected drawable and identical resulting document Transform values.
- **SC-007**: With a media source at a **fixed logical size** (an explicitly-sized drawable or a synthetic source double), backing it with two different pixel-resolution bitmaps of the same content changes only the op's `EffectiveScale` — not its position or logical bounds — and both composite correctly into the logical dest rect; the media-open path with no decode-scale hint is behaviorally identical to today. (Two backing files reporting the *same* logical size **without** an explicit size — i.e. an intrinsic-logical-size channel — is deferred with proxy decode, FR-025.)
- **SC-008**: No `ToSize(1)` call and no unguarded integer truncation / `(int)`-cast of logical bounds remain on the 2D render path — scoped to the render-path directories of `Beutl.Engine` (graphics rendering, filter effects, particles, audio visualizers, brushes, text formatting) and excluding UI / display-zoom paths and non-render helpers, with any approved exception annotated — enforced by a search-based test, proving the migration is not partial.
- **SC-009**: Exporting with supersampling (`s > 1`) produces output at exactly the configured output resolution and measurably reduces aliasing versus `s = 1` on a defined high-frequency test pattern (e.g. higher SSIM against a natively-high-resolution reference, or a lower aliasing-energy metric). The **user-facing export factors are Off (1×) / 2× / 4×** (the test suite additionally covers 1.5×); the final downscale uses **Mitchell for ≤ 2× and trilinear + mipmaps for > 2× (4×)**. The aliasing metric is the high-pass (Laplacian) energy of the difference plus the SC-004 SSIM margin.

## Assumptions

The following are reasonable defaults chosen where the description did not specify; the genuinely plan-level ones are listed under "Open Questions for Planning" for `/speckit-plan` to finalize.

- **Render scale is uniform `float` in v1**, with storage chosen to widen to a vector later (anisotropic scale is out of scope here).
- **Logical unit = 1 px at FrameSize**, so no file migration is needed (confirmed by the maintainer). Effect properties currently typed in pixel units (e.g. color-shift offsets) are reinterpreted as logical at scale 1.0, preserving their values.
- **Resolution-sensitive effects use parameter scaling only** (confirmed: "プロパティにスケールを乗算する"); reduced-scale preview is best-effort, no force-full-scale subtree mechanism and no warning UI in v1.
- **Mixed-scale composites at `max` concrete child scale** (confirmed), using Mitchell resampling; the output-scale cap is **deferred to the final root normalization** (or an explicit `ClampToOutput`) per the supply-driven model — it does not clamp intermediates (FR-016/FR-036).
- **Proxy decode is out of scope** for this feature (confirmed); only the render-scale plumbing and the additive extensibility of `MediaOptions` are delivered.
- **Render cache invalidates on scale change** (simple, correct default) rather than maintaining per-scale multi-entry caches; this may be revisited for scrubbing UX.
- **Text keeps current hinting** and is re-shaped at the device scale; reduced-scale text preview is therefore perceptually faithful but not necessarily bit-identical, consistent with the resolution-sensitive policy.
- **Particles and audio visualizers are in scope**: their independent raster paths and pixel-magnitude properties adopt the scale contract (the independent code-verification review surfaced these as missed coupling sites that would otherwise break reduced-scale preview).
- **3D is treated as a mixed-scale op in v1**: the `Scene3DRenderNode` surface is resampled at the composite boundary (correctness covered by the mixed-scale rule); making its internal render track the 2D scale in lockstep (quality / perf) is deferred unless the maintainer pulls it in — see Open Question 6.
- **Scale changes are applied atomically on the render dispatcher**, together with cache invalidation, consistent with the existing dispatcher-affine render model.
- **The preview render scale is a fixed enum** (Full / Half / Quarter / Fit-to-previewer) surfaced as an editor preview-quality control, held as **per-edit-view session state** and not persisted (FR-035); it originates from the render request and is distinct from the persisted `FrameCacheConfigScale`.
- **Export supersampling (`s > 1`) is in scope** (FR-034, SC-009): the export path may render above the output resolution and downscale for anti-aliasing; the offered factors are pinned in `/speckit-plan`.

### Open Questions for Planning

These do not block the spec; all remaining items are implementation-level for `/speckit-plan` (the spec-level scope/behavior decisions were resolved in the Clarifications session above):

1. **Scale propagation mechanism** — confirm the maintainer's "scale on RenderNodeOperation" is realized as context-driven propagation plus a read-only effective-scale on the operation, vs a writable per-operation field.
2. **Where the final normalization attaches** — a terminal node, a processor method, or the top-level renderer; and whether the composition-frame entity needs both logical size and scale (this includes the export supersample downscale, FR-034).
3. **Stroke geometry space** — generate strokes in logical space (only the matrix changes) vs device space (bake the scale in), given the non-linear stroke-splitting and corner-radius clamping.
4. **Renderer / cache rebuild trigger** — how a preview-scale change (FR-035) triggers the renderer / cache rebuild atomically on the render dispatcher (FR-031).
5. **Acceptance threshold numbers** — the exact SSIM threshold for reduced-scale "exact" effects (SC-004), the SC-003 benchmark scene / hardware baseline, and the SC-009 supersample factors + aliasing metric. (The metric *family* is decided: byte-equality gate at 1.0 + SSIM for reduced scale.)
6. **Scale invalidation key & 3D perspective** — how render scale enters the `EngineObject.Version` / resource-compare invalidation path (it is not an `EngineObject` property), whether any source-generated resource fields or cache keys need generator + `tests/SourceGeneratorTest` updates (FR-032), and the `Scene3DRenderNode` perspective non-commutativity rule (`S·P ≠ P·S`) for mixed-scale 3D.

## Dependencies

- **Render-node pipeline** (`Beutl.Engine` graphics rendering) — the primary surface of change.
- **Filter-effect subsystem** (`Beutl.Engine` filter effects) — every effect adopts the scale contract.
- **Scene / project-system render entry and export path** (`Beutl.ProjectSystem`) — supplies and consumes the render scale.
- **Editor view-models and overlays** (`Beutl` UI) — surface the preview scale and keep hit-test/handles logical.
- **Node-graph rendering** (`Beutl.NodeGraph`) and **out-of-tree plugin effects/drawables** — downstream consumers of the breaking public-API changes; covered by the breaking-change framing in FR-028.
- **Source generators** (`Beutl.Engine.SourceGenerators`) — generate per-property resource update / compare code; any scale-dependent generated resource field or cache key may require generator changes and `tests/SourceGeneratorTest` coverage (FR-032).
- **Future**: the proxy / optimized-media feature builds on FR-023..FR-025; the FFmpeg IPC decode-scale hint crosses the GPL/MIT boundary and is explicitly deferred.
