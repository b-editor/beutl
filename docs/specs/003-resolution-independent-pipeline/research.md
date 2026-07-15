# Phase 0 Research: Resolution-Independent Rendering Pipeline

**Feature**: 003 | **Date**: 2026-05-30 | **Spec**: [spec.md](./spec.md) | **Dossier**: [notes/rendering-analysis.md](./notes/rendering-analysis.md)

Resolves the spec's six Open Questions for Planning into implementable decisions and records the primitive signatures and best-practices the plan leans on, each verified against the current code (file:line cited). The maintainer-confirmed clarifications (logical unit = 1px@FrameSize; uniform float v1; parameter-scaling for resolution-sensitive effects; mixed-scale at max child scale; export supersampling in scope; preview scale = fixed enum, per-edit-view, non-persisted; byte-equality at 1.0 + SSIM for reduced) are fixed inputs.

---

## D1 — Scale model: supply-driven, output scale at the final stage only (Open Question 1)

> **Refined after maintainer review — supersedes the original top-down D1.** The first draft propagated a single top-down render scale on `RenderNodeContext.Scale` that every effect multiplied by and sized buffers at. The maintainer requires **supply-driven** behavior: an intermediate effect runs at its *input's* density — never **above** what the input supplies (a 2.0 / 4K source on a 1080 timeline is not downsampled, so quality effects can use the detail) — and the output scale is applied **only at the final stage** ("最終的な部分でスケールを調整する"). See **D7** for the negotiation rule.
>
> **Amended 2026-06-15 — `s_out` is the working-scale FLOOR; supply density is the floor only for a denser source.** The early "a 0.5 proxy runs the effect at 0.5, do NOT upsample" half is **replaced**. The working scale is now `w = min( max(s_out, densest concrete supply), maxWorkingScale )`: `s_out` is a **floor** an effect never runs below, and a denser concrete supply runs **above** it. Source *detail* and the effect's own *working resolution* (blur kernel / shadow / shader grid) are distinct: running the effect below `s_out` only discards resolution the deliverable can use without fabricating source detail, so a **sub-output** concrete supply (an enlarged / low-density bitmap, `At(0.5)`) feeding an effect at a `1.0` export is **floored to `w = 1.0`** (matching the pre-feature renderer) rather than staying at `0.5`. A genuine proxy is still cheap in **preview**: at a `0.5` preview a `0.5` proxy gives `max(0.5, 0.5) = 0.5`. `s_out` is still **never a ceiling** (FR-016 preserved). See **D7** for the rule body and **FR-019/FR-036**.

**Decision**: Three scales, three owners.

1. **Output scale `s_out`** — `float`, get-only on the render request: `Renderer` ctor → `RenderNodeProcessor.OutputScale` → **`RenderNodeContext.OutputScale`** (renamed from the first draft's `Scale`; seeded once at `RenderNodeProcessor.Pull`, `RenderNodeProcessor.cs:121`, the sole production construction site; test code constructs it directly and is updated alongside). Preview 0.5/0.25, export 1.0, export-SSAA 2.0. **It is the final normalization target only** — consumed structurally at the root composite, entering intermediate math only as the fallback/ceiling term of the working-scale rule (D7). It never sizes an intermediate buffer directly.
2. **Effective scale `e`** — read-only **`EffectiveScale`** value type on `RenderNodeOperation` (the maintainer's literal "scale on `RenderNodeOperation`"), flowing bottom-up: the density the op's pixels actually exist at. Vector/lossless ops report **`EffectiveScale.Unbounded`** (re-rasterizable at any target); bitmap-backed ops (decoded media, cached tiles, nested-scene/3D surfaces, flushed effect targets) report `EffectiveScale.At(scale)`. **`LosslessReRasterizable` (the first draft's bool) is dropped — `IsUnbounded` subsumes it** (no contradictory "lossless but `e=2.0`" state).
3. **Working scale `w`** — computed, not stored on the context: each buffer-allocating boundary (the three sinks `RenderNodeProcessor.cs:26,52,75`; `FilterEffectActivator.Flush` `:29`; `CustomFilterEffectContext.CreateTarget` `:52`; brush/tile intermediates; `ParticleRenderNode`) computes `w = RenderNodeContext.ResolveWorkingScale(inputs, OutputScale)` (D7), sizes `ceil(bounds × w)`, opens an `ImmediateCanvas` that **bakes the base CTM `CreateScale(w)`** at construction (feature 003 — no manual push), and tags its emitted op `e = w`. Sub-processor spawn sites forward `OutputScale` (`ReferencesChildRenderNode.cs:25`, `NodeGraphFilterEffectRenderNode.cs:42`, `ParticleRenderNode.cs:144`).

The model: ops carry their own scale `e`; intermediates run at `w` derived from their input supply; `s_out` adjusts only at the final part.

**Rationale**: One rule (D7) satisfies both **R1** (a reduced-scale proxy stays cheap in preview — at a `0.5` preview a `0.5` proxy floors to `max(0.5, 0.5) = 0.5`; *amended 2026-06-15:* the same proxy at a `1.0` export floors to `1.0`, rendering at the deliverable density — `s_out` is a floor, not a cap) and **R2** (2.0 source input → effect runs at 2.0: no forced downsample, high res available to intermediate quality effects), with the only forced resample being the single final-stage normalization to `s_out`. **Byte-identical at the default**: `s_out=1.0` with all inputs `Unbounded`/`At(1.0)` → `w = max(1, 1) = 1.0` everywhere; every new branch is gated on `e ≠ w`, which never fires. `default(EffectiveScale) = Unbounded`, so a plugin op that ignores the new param is safe.

**Alternatives rejected**:
- *Top-down `s_out` as the working scale (the original D1)* — forces every effect to the requested scale: upsamples proxies (synthetic detail, no perf win) and downsamples high-res sources before quality effects can use them. Rejected per the supply-driven requirement.
- *Writable per-op `Scale` field as the primary channel* — forces every op-creation site to set it; one miss silently produces wrong output (no compile signal).
- *Root `Matrix.CreateScale(s)` only (CTM carries everything)* — every effect/source that allocates an intermediate or reads integer pixel dims bypasses the CTM (dossier §3.2): rasterizes at logical (low) res then upscales, degrading exactly the effects users care about, no perf win.

---

## D2 — Final normalization attach point & export supersample downscale (Open Question 2)

**Decision**: Seed scale on the render request; apply it once at the root `Renderer`; **no terminal normalization node and no processor final stage**. The device surface is `ceil(FrameSize × s)`; `FrameSize` stays the **logical** project size. As shipped (feature 003 base-CTM model), the root `ImmediateCanvas` **bakes the base CTM `CreateScale(s)` at construction** (no per-frame push in `Render`/`RenderDrawable`; the FPS overlay re-enters device space via `PushDeviceSpace`). `GraphicsContext2D(node, FrameSize.ToSize(1))` takes the **logical** size so the recorder/backdrop stay logical (FR-021/FR-027).
- **`CompositionFrame` gets NO scale field**: its `Size` already is the logical frame size (`SceneCompositor.cs:71,94`), and scale is a render-request property (FR-002/FR-035). Audio/thumbnail callers pass `Size=default` and stay unaffected.
- **Export supersample downscale (FR-034)** happens in **`FrameProviderImpl.RenderCore`** (`FrameProviderImpl.cs:47-52`): when `renderer.RenderScale > 1`, Mitchell-downscale `Snapshot()` to exactly `Scene.FrameSize` before returning, and assert the returned bitmap size == `FrameSize` before the encoder memcpy (`FFmpegEncodingController.cs:240` copies into `SourceSize`-sized `Data[0]` with no negotiation). Keep `videoSettings.SourceSize = Model.FrameSize`. Preview (`s ≤ 1`) needs no downscale — it snapshots the reduced buffer and the UI upscales via display zoom.

**Rationale**: Since `logical × s == device` by construction, the root buffer is already device-size, so vector content needs no separate normalization pass. The only explicit final resample is the `s > 1` export case, localized to one place feeding the encoder, keeping FR-026 intact.

**Alternatives rejected**: a terminal normalization `RenderNode` (adds a tree node for what the root push already does); putting scale on `CompositionFrame`/`Scene` (risks leaking into serialization, conflating with `FrameSize`, violating FR-002/FR-035).

---

## D3 — Stroke geometry space (Open Question 3)

**Decision**: **LOGICAL space.** Do **not** bake scale into `PenHelper` or the corner math — **zero changes** to `PenHelper`, `RoundedRectGeometry`, or the `Geometry.Resource` stroke cache. Strokes are pre-outlined into a logical `SKPath` (`GetCachedStrokePath`/`PenHelper.CreateStrokePath`) and painted as a **fill** (`ImmediateCanvas.cs:441` `IsStroke=false`); the root `Matrix.CreateScale(s)` on the active CTM scales the outline to device pixels for free, exactly like fill geometry. The stroke-path cache key stays `(Version, pen.GetOriginal(), pen.Version)` with **no scale** — the cached logical outline is genuinely scale-invariant, so FR-011 is satisfied vacuously and a preview-scale toggle triggers **no** stroke-cache rebuild.

**Rationale**: The non-linear hazards commute with uniform scale: the corner clamp `min(cr, min(w,h)/2)` and per-corner `Math.Clamp` scale by `s` on all operands; the `maxAspect < thickness` stroke-split predicate and iteration count are scale-invariant (`s` cancels), so the same number of `SKPath.Op(Union)` passes runs at every scale and the CTM scales the result. A device-space rebuild would run those non-affine-equivariant boolean ops on scaled coords, risking per-scale divergence and a different `s=1` result. Byte-identity at `s=1` is trivial (nothing changes).

**Documented exception**: text is **re-shaped** at device scale (FR-012), so a `FormattedText` stroke built from an already-device-scaled fill path must **not** also be CTM-scaled. Reconciled in the text-shaping slice (`FormattedText.cs:267-279`).

**Alternatives rejected**: device-space stroke generation (bake `s` into thickness/corners, rebuild path) — breaks `s=1` byte-identity, risks float drift at corners and thick-stroke unions, forces a scale-keyed stroke cache (needless misses, touches the source-generated `Resource.Version` path).

---

## D4 — Renderer/cache rebuild trigger on preview-scale change (Open Question 4)

**Decision**: Render scale is a **constructor parameter** on `Renderer`/`SceneRenderer` (immutable per instance), **not** a settable property. A preview-scale change **rebuilds the renderer + frame cache** by widening the existing `Scene.FrameSizeProperty` rebuild trigger (`EditViewModel.cs:51-66`) to `(FrameSize OR PreviewScale)`:
- New non-persisted per-edit-view state `EditViewModel.PreviewScale` (a `RenderScale` value type wrapping the fixed enum + uniform float). `SaveState`/`RestoreState` are **untouched** → SC-002/FR-035 preserved.
- Combine the renderer + `FrameCacheManager` observables into one `CombineLatest(FrameSizeProperty, PreviewScale)` so they rebuild **together** (today two independent `FrameSizeProperty` chains that would desync under an independent scale axis). `DisposePreviousValue()` already disposes the old `SceneRenderer` (surface + caches + compositor) before the new one is observed.
- **Atomicity (FR-031)** is structural, no new lock: `Renderer.Value`/`FrameCacheManager.Value` are read *fresh inside* the `RenderThread.Dispatcher` render closure (`PlayerViewModel.cs:1270-1271`), the serial dispatcher runs each frame work-item to completion, and surface allocation already happens on the dispatcher (`Renderer.cs:48`). So any in-flight frame ran fully on the old or fully on the new renderer — never a mix. `PreviewScale.Subscribe` calls `QueueRender()` to repaint.

**Rationale**: Reuses the proven dispose-and-rebuild path; `renderScale` immutable per instance means a half-rendered frame can never observe a changed scale. The settable-property alternative cannot be made atomic without a render-thread barrier and forces imperative invalidation of every cache/intermediate.

**Defense-in-depth (from D6)**: even with rebuild-by-replacement, tag each `RenderNodeCache` tile with the working scale it was stored at (`CachedWorkingScale`) and reuse-with-downsample when `≥` the required scale / miss when `<`, so a stale-scale tile can never be blitted 1:1 (named silent-corruption Risk #2).

**Alternatives rejected**: `Renderer.SetRenderScale(float)` mutating the live surface (the OQ6 agent's secondary suggestion) — heavier, error-prone in-place `SKSurface` resize, deferred to a possible scrubbing-UX follow-up.

---

## D5 — Acceptance thresholds & golden-image test harness (Open Question 5)

**Decision**: A tiered, Vulkan-gated golden-image harness plus four pinned numeric gates.
- **Scale-1.0 byte-equality (SC-001/SC-005)**: bit-identical **RgbaF16 raw frame buffer**. Compare `RenderTarget.Snapshot()` pixels via `MemoryMarshal.Cast<byte,ushort>(snapshot.GetPixelSpan())` `SequenceEqual` over `W*H*4` half-words, **zero epsilon**. Baseline captured once from the pre-feature renderer (`main`) as raw RgbaF16 `.bin` (not PNG — PNG loses F16/linear precision) under `tests/Beutl.UnitTests/Assets/Golden/<Scene>/scale1.0.bin`. A mismatch dumps a PNG diff to the artifact dir.
- **Reduced-scale "exact" (SC-004)**: render at 0.5, Mitchell-upscale to 1.0 device size, require **SSIM ≥ 0.985** (mean SSIM on perceptual luma from linear RGB, 8×8 Gaussian window σ=1.5, L=1.0) **and** mean-absolute-error **≤ 0.02** (linear 0..1, catches registration drift SSIM tolerates).
- **Best-effort effects (FR-013 list)**: no SSIM floor at 0.5; assert (a) scale-1.0 byte-equality and (b) a per-effect **structural invariant** at 0.5 (e.g. mosaic tiles = `ceil(tileSize×0.5)` device px; ColorShift bounds inflate by `round(offset×0.5)`) on op `Bounds`/metadata via `[TestCaseSource]` over the FR-009 manifest.
- **Mixed-scale (SC-005)**: exact gate (SSIM ≥ 0.985, MAE ≤ 0.02) vs full-scale reference, plus a **seam check** (max per-pixel delta along the composite boundary rows/cols ≤ 0.05).
- **Supersample (SC-009)**: factors `s ∈ {2.0}` required first-class, `{1.5, 4.0}` additionally tested. Assert post-downscale `encodedBuffer.Width/Height == ceil(FrameSize)` exactly (FR-026). The enforced gate is MAE-to-ground-truth strictly decreases versus `s = 1` plus an SSIM no-degradation tolerance: `SSIM(s≥2) − SSIM(s=1) ≥ −0.01` *(amended to match spec.md SC-009 and the shipped `ExportSupersampleTests`; the original `≥ 0.01` was an improvement margin, the corrected `≥ −0.01` is a degradation tolerance)*.
- **Benchmark (SC-003)**: committed `bench.scene` (1920×1080, ~12 vector shapes w/ fills+strokes, 3 TextBlocks, 2 gradients, Blur+DropShadow chain, 1 nested scene, 1 particle, 1 audio visualizer, 1 3D scene — doubles as the SC-001 representative set). Measure `Render+Snapshot` wall-clock only, warm cache excluded (discard frame 1). The **ratio** `median(0.5)/median(1.0)` is the gate (`< 0.6`, hardware-independent; ~0.25× documented as the target, not hard-asserted). Lives in `[Explicit][Category("Benchmark")]` NUnit + a `tests/Beutl.Benchmarks` entry, **not** in the default CI gate.
- **Harness shape**: new `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/` — `GoldenImageHarness`, `GoldenThresholds`, `ImageMetrics`. Reuses the **existing Vulkan gate** (`VulkanTestEnvironment.EnsureAvailable()` → `Assert.Ignore` on GPU-less CI; `InvokeOnRenderThread`), exactly like `ImmediateCanvasVulkanTests`/`PixelSortEffectTests`. `BEUTL_GOLDEN_UPDATE=1` regenerates baselines. **SC-008** (no `ToSize(1)`) was planned as a separate non-GPU search test but was **deferred** (T007): a naive scan false-positives on the load-bearing logical-`ToSize(1)` / `(int)`-at-`w=1` sites, so it needs an annotated allowlist; SC-008 was reframed to "no NEW unguarded truncation" with completeness carried by the behavioural buffer-activation goldens.

**Rationale**: F16-linear surfaces make SSIM/SSAA correct in linear light; the byte-equality gate on raw F16 is the strongest possible regression anchor; the ratio-based benchmark is hardware-independent.

**Residual for plan**: whether CI adds a GPU lane (SwiftShader/llvmpipe) or pixel-goldens stay dev/self-hosted only (maintainer/CI-workflow decision — see Constitution note); whether RgbaF16 byte-equality is reproducible across MoltenVK/SwiftShader/native closely enough for zero-epsilon (else a tiny ULP tolerance, validated empirically).

---

## D6 — Scale invalidation key, source-generator impact, mixed-scale 3D/perspective (Open Question 6)

**Decision (three parts)**:

1. **Scale is invalidation identity at the RENDERER level, not an `EngineObject` property.** Render-graph diffing keys on `Resource.Version` (bumped only by source-generated `CompareAndUpdate*` on `IProperty` changes, `EngineObject.cs:407-465`) + per-frame `RenderNode.HasChanges`. Scale is a render-request property on `Renderer` → `RenderNodeContext.OutputScale`, correctly never touching `Resource.Version`. On an output-scale change, clear all render-node caches (rebuild-by-replacement per D4) and re-render. **Defense-in-depth + supply-aware reuse**: tag each `RenderNodeCache` tile with **`CachedWorkingScale`** = the `w` it was rasterized at (which may exceed `s_out` for a preserved high-res source). `Pull` **reuses** a tile when `CachedWorkingScale ≥ requiredScale` (Mitchell-**downsample** at the `CreateFromRenderTarget` blit — so an SSAA export can reuse a high preview cache) and treats `CachedWorkingScale < requiredScale` as a **miss** (cannot invent detail). `RenderCacheRules.Match` thresholds divide by `CachedWorkingScale²`. `CachedWorkingScale` participates in change-detection (FR-020).

2. **No source-generator change for the common path.** `ResourceClassEmitter` only emits per-`IProperty` fields + `CompareAndUpdate` (`ResourceClassEmitter.cs:51-267`); scale is not an `IProperty`, so the generated `Resource.Update`/`Version` stay scale-free (they run at composition time, upstream of render scale). The only generator-adjacent work is effect properties whose **unit/type** is reinterpreted/widened (e.g. `ColorShift` `PixelPoint`→`Point`, dossier §5.5/§8.3) — ordinary `IProperty` type changes the generator already handles; the `tests/SourceGeneratorTest` NUnit snapshot suite (a `CSharpGeneratorDriver` harness over the kept generator inputs) must stay green, behavioral assertions go in `tests/Beutl.UnitTests`. **Net**: FR-032 resolves to "nothing becomes scale-dependent in the resource model; document this so reviewers don't add scale to it."

3. **Mixed-scale 3D / perspective rule**: keep render scale **out of the W (perspective-divide)** by **appending** the device scale *after* any user transform, never prepending it into the projective column.
   - *2D faux-3D* (`Rotation3DTransform`): composition is row-vector; `Append(S)` post-multiplies (`M_total = M_u · S`), scaling X/Y outputs by `s` but leaving the perspective column (`M13/M23/M33`) untouched → "project in logical space, then scale" = correct device mapping, bit-identical at `s=1`. Prepending (`S · M_u`) feeds device-x into the W denominator (the `S·P ≠ P·S` trap) — forbidden. Realize by applying the root scale once at the device boundary (`Renderer` root push), never folded into per-node artistic matrices (preserves FR-027). `Center`/`Depth` stay logical, unscaled.
   - *Real 3D* (`Scene3DRenderNode`): treat its surface op as an ordinary **bitmap** op tagged `EffectiveScale.At(1.0)` relative to its authored `RenderWidth/Height`; the compositor Mitchell-resamples it to the composite target at the blit boundary (FR-033). No internal-render change; reduced-scale preview mismatch is tolerance-based, not byte-equal. Lockstep deferred.

**Rationale**: Keeps the resource model and generators untouched; the append-not-prepend rule is the precise, code-grounded fix for perspective non-commutativity.

---

## D7 — Resolution policy & working-scale negotiation (refinement)

> **SUPERSEDED (2026-06-09): the `ResolutionPolicy` type was removed.** This section records the original design — a declarative per-effect policy choosing the working scale. The shipped pipeline has **no policy value**: every default boundary runs **supply-driven** (`w` = the densest concrete input, vector/mixed floor at `s_out`, capped by `MaxWorkingScale`), which is exactly the former `Inherit` branch — the only branch any built-in ever used. `ClampToOutput`/`Oversample(k)`/`PreserveSource` had zero in-tree users. A narrow exception selects a `PlanFilterEffectRenderNode` through `FilterEffect.Resource.PlanRenderNodeFactory` and overrides `ResolveWorkingScale`, preserving the standard compiler, ROI, pooling, and caches. Read the rest of this section as the rationale for the supply-driven *rule* (still current); treat every mention of a *policy enum / declaration point / precedence* as historical. The normative rule is **FR-036**; the global ceiling (preview `2 × s_out`, export `+∞` — no quality ceiling, *amended 2026-06-15*) is **FR-037**.

**Decision (historical)**: A declarative `ResolutionPolicy` per effect/node drives one shared working-scale rule.

`ResolutionPolicy = { Inherit (default) | ClampToOutput | Oversample(factor) | PreserveSource }`.

**The one rule** — `RenderNodeContext.ResolveWorkingScale(ReadOnlySpan<EffectiveScale> inputs, float outputScale, ResolutionPolicy policy)`:
- `supply` = max `e.Value` over the **concrete** (non-`Unbounded`) inputs; `0` if all inputs are vector/`Unbounded`.
- `baseline` = `max(outputScale, supply)` — `s_out` is the **floor** *(amended 2026-06-15: the historical body said `baseline = supply > 0 ? supply : outputScale`, i.e. `s_out` only as a vector-only fallback. The shipped rule floors at `s_out` for **every** boundary, so a sub-output concrete supply is lifted to `s_out`. See FR-036.)*; `s_out` is **never a ceiling** (a denser supply runs above it).
- `Inherit` → `baseline` — supply-driven: a denser supply runs above `s_out`, a sub-output supply is floored to `s_out`.
- `ClampToOutput` → `min(baseline, outputScale)` — perf: drop a too-high input early.
- `Oversample(k)` → `max(baseline, k × outputScale)` — quality: force ≥ `k×` even from a low input.
- `PreserveSource` → `baseline`, and floors any ancestor `ClampToOutput`.
- Finally clamp to the **global ceiling**: `w = min(w, MaxWorkingScale)` (memory backstop, below).

**Default = `Inherit` for ALL effects, built-in and plugin** *(maintainer choice)*. Every effect preserves its input's density by default; **heavy effects opt OUT** with `ClampToOutput`; **resolution-sensitive effects** (FR-013) declare `PreserveSource` so an ancestor clamp can't strip the resolution they exist to use; `Oversample(k)` is the SSAA-on-demand opt-in. Out-of-tree plugins default to `Inherit` (a supply-driven passthrough, byte-identical at `s_out=1.0`). *(The maintainer rejected the design panel's cheaper-built-ins-auto-clamp default in favor of pure `Inherit` + the global ceiling.)*

**Global working-scale ceiling `MaxWorkingScale`** *(maintainer choice: ceiling ON for preview)*: a configurable, **per-render-request** cap applied as the last step of `ResolveWorkingScale`, so no combination of `Inherit`/`Oversample`/preserved high-res sources can blow up worst-case **preview** memory (RgbaF16 is 8 bytes/px; `w²` memory). It caps **only the high side**: never pulling `w` below a proxy's supply (R1 unaffected) and inert at `s_out=1.0` with 1.0 inputs (byte-identity unaffected). **As shipped (preview default `2 × s_out`; export `+∞` — no quality ceiling).** *(Value history: the original D7 said export `+∞`, then narrowed to `max(8, 4 × s_out)`; that finite export ceiling was **removed again 2026-06-15** as a quality clip masquerading as an OOM backstop — it silently discarded detail from any source denser than the ceiling (e.g. a 4096-px logo in a 256-px box = supply 16, clipped at 8) far below any allocation limit. Export now imposes **no** working-scale quality ceiling; allocatability on export is the per-buffer **dimension** clamp (`ClampWorkingScaleToBufferBudget`, 16384 px/axis, using each buffer's own bounds) plus the documented request-scoped aggregate byte/area budget follow-up — see FR-037.)* The preview ceiling stays a tight, interactive backstop.

**Declaration points & precedence**: `FilterEffect.ResolutionPolicy` (virtual, default `Inherit`) governs that effect's own intermediates; `RenderNode.ResolutionPolicy` (virtual, default `Inherit`) governs a container's composite allocation. They never apply to the same buffer (no conflict): a `ClampToOutput` container over an `Oversample(2)` child means the child oversamples its own intermediate, then is resampled down at the container's blit (FR-017). A `PreserveSource` floor is the one cross-boundary scalar (a per-pull `max(floors)`, inert unless a `PreserveSource` effect is present).

**Final adjustment & SSAA are one machine**: the root device surface is `ceil(FrameSize × s_out)`; an op whose `e ≠ s_out` is Mitchell-resampled **once** at its `DrawSurface`/`DrawRenderTarget` blit onto the root grid (down for a preserved 2.0; *a sub-output supply is floored to `s_out` at its own effect boundary (FR-036) so it generally reaches the root already at `s_out`*). Export-SSAA (`s_out=2.0`) is literally R2: the device grid is `ceil(FrameSize×2.0)` and the `FrameProviderImpl` 2.0→1.0 downscale (D2) is the terminal normalization to delivery resolution.

**Mixed inputs** (a vector shape `Unbounded` + a 0.5 proxy + a 2.0 source under one boundary): `supply = max(0.5, 2.0) = 2.0` (the `Unbounded` shape excluded); default `Inherit → w = 2.0`. Each input is normalized to `w` **once** before the shared filter/flatten via a per-`EffectTarget` scale (FR-019): the `Unbounded` shape **re-rasterizes** losslessly at `w` (regenerate, not upsample), the 0.5 proxy Mitchell-upsamples, the 2.0 source is 1:1. Today's `EffectTargets.CalculateBounds` (`EffectTargets.cs:27`) is a scale-blind `Union` and `EffectTarget` has no scale field, so a mixed-scale flatten currently composites at mismatched densities — a **correctness** bug this fixes, not a perf detail.

**Rationale**: One rule covers low-input (R1), high-input (R2), the per-effect perf knob, and SSAA; the global ceiling makes "preserve by default" memory-safe; pure `Inherit` default keeps byte-identity trivial and the plugin story zero-ceremony.

---

## Cross-cutting confirmations (best-practices brief)

**Primitive signatures (already exist; never called with non-1 scale on the render path)** — SkiaSharp 3.119.2 (`Directory.Packages.props:80`):
- `PixelSize.ToSize(float/Vector)` divides; `PixelSize.FromSize(Size, float/Vector)` multiplies + **ceils** each axis (`PixelSize.cs:187,209`) — the "ceil for sizes" half of FR-007.
- `PixelPoint.FromPoint(Point, float/Vector)` uses `(int)` → **toward-zero truncation, NOT floor** (`PixelPoint.cs:226,237`). For negative origins (common after blur/shadow inflation) `(int)(-2.3)==-2` vs `floor==-3`. **The FR-007 helper MUST reproduce `(int)` toward-zero at scale 1.0** for byte-equality — do not silently "fix" to floor (that would break SC-001 on any negative-origin bound). The dossier §12.1 phrase "floor origin" is imprecise; the code is toward-zero.
- `PixelRect.FromRect(Rect, float/Vector)` (`PixelRect.cs:391`) = origin `(int)` toward-zero + bottom-right `(int)Math.Ceiling` → asymmetric, growing rect. **This IS the FR-007 shared helper** for the main rasterization sink. The two **filter-effect sinks** (`FilterEffectActivator.cs:29`, `CustomFilterEffectContext.cs:52`) do component-wise `(int)width`/`(int)height` truncation (unlike `FromRect`'s corner-based rounding) — **at `w = 1.0` they MUST keep that `(int)` truncation** (switching to `FromRect` would change scale-1.0 output and break byte-identity); they apply `ceil(× w)` only for `w ≠ 1.0`, and are **scale-1.0-sensitive** sites that must be golden-tested at `w = 1.0`.
- All overloads exist in both `float` and `Beutl.Graphics.Vector` forms → the FR-006 anisotropic-widening path needs **no new primitive work**.

**SkiaSharp resampling**:
- `ImmediateCanvas.DrawSurface`/`DrawRenderTarget` (`:106-126`) today have **no `SKSamplingOptions`** — pure 1:1 blit relying on the CTM. This is the single highest-value primitive gap for FR-017. Route them through `DrawImage(srcRect, destRect, sampling, paint)` (snapshot-to-`SKImage` precedent at `FilterEffectActivator.cs:135`) using `SkiaSharpExtensions.ToSKSamplingOptions` (`SkiaSharpExtensions.cs:10`).
- **Upsample (preview mixed-scale)**: `SKCubicResampler.Mitchell` (matches `DrawBitmap` at `ImmediateCanvas.cs:166`); once per off-scale op (FR-017).
- **Downsample (`s>1` export SSAA)**: **Mitchell for `≤2×`; trilinear+mipmaps for `>2×`**. Offered factors **Off / 2× / 4×** (maintainer choice; SC-009): the 2× path is single-pass Mitchell, the 4× path adds the trilinear+mipmap downscale.
- **RgbaF16 + linear sRGB surfaces** (`RenderTarget.cs:48,62`): resampling happens in linear light → SSAA genuinely reduces aliasing (SC-009) for free; F16 headroom absorbs cubic overshoot. Memory scales `s²`: a 2× SSAA of 1080p root surface ≈ 127 MB; dispose scaled intermediates promptly (`SKSurfaceCounter` ref-counting). Preview `s<1` drops memory `s²` — the intended win.

**Dispatcher atomicity**: one serial `RenderThread.Dispatcher`; all render mutation is dispatcher-affine (`VerifyAccess`). A frame's evaluate→render→snapshot is one work-item, so it can't be torn mid-composite. The safe scale swap is the D4 rebuild-by-replacement, read fresh inside the dispatched render lambda; use `InvokeAsync` (awaitable) if a caller must await the rebuild before requesting the next frame.

---

## Resolved Open Questions ↔ Decisions

| Spec Open Question | Resolved by |
|---|---|
| 1. Scale propagation mechanism | **D1 + D7** — supply-driven: `OutputScale` (final-only) + per-op `EffectiveScale` + computed `WorkingScale` via `ResolveWorkingScale` (the per-effect `ResolutionPolicy` was later removed — supply-driven only) |
| 2. Final normalization attach point | **D2** — root `Renderer` push; no terminal node; export downscale in `FrameProviderImpl` |
| 3. Stroke geometry space | **D3** — logical space; zero PenHelper/cache change |
| 4. Renderer/cache rebuild trigger | **D4** — ctor param; widen the FrameSize rebuild observable; structural atomicity |
| 5. Acceptance threshold numbers | **D5** — byte-equality@1.0; SSIM ≥ 0.985 / MAE ≤ 0.02; SSAA 2×; ratio benchmark |
| 6. Invalidation key & 3D perspective | **D6** — renderer-level cache identity; no generator change; append-not-prepend perspective rule |
