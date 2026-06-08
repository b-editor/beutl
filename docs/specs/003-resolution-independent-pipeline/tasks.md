# Tasks: Resolution-Independent Rendering Pipeline

**Feature**: 003 | **Branch**: `speckit/003-resolution-independent-pipeline`
**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md) (D1–D7), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4 (user-story phases only)
- Tests are **included** (Beutl Constitution III: new logic in `src/` ships with NUnit tests). Pixel/golden tests are Vulkan-gated (`VulkanTestEnvironment` → `Assert.Ignore` on GPU-less CI); the SC-008 search and SC-002 migration tests run without a GPU.

## Path Conventions

Single-repo .NET solution. Engine: `src/Beutl.Engine/`; project system: `src/Beutl.ProjectSystem/`; node graph: `src/Beutl.NodeGraph/`; editor: `src/Beutl/`; tests: `tests/Beutl.UnitTests/`, `tests/Beutl.Benchmarks/`, `tests/SourceGeneratorTest/`.

**The supply-driven model in one line**: output scale `s_out` is the final target only; each op carries an `EffectiveScale` (vector = `Unbounded`); each boundary computes a working scale `w = ResolveWorkingScale(inputs, s_out, policy)`; effects multiply spatial-length params by `w`; **byte-identical at `s_out=1.0` with unit-scale inputs.**

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: value types + test harness every later phase needs. No behavior change.

- [X] T001 [P] Create `EffectiveScale` value type (`readonly record struct`; `Unbounded`/`At(float)`/`Value`/`IsUnbounded`; `default == Unbounded` via an inverted `_bounded` flag) in `src/Beutl.Engine/Graphics/Rendering/EffectiveScale.cs`
- [~] T002 [P] ~~Create `ResolutionPolicy` value type + `ResolutionPolicyKind` enum~~ **REVERTED (2026-06-09).** `ResolutionPolicy.cs` was created then deleted — the policy concept was removed (zero in-tree users; a custom `FilterEffectRenderNode` is more flexible). See the requirements.md amendment log. The working scale is supply-driven only.
- [X] T003 [P] Create Vulkan-gated golden harness `GoldenImageHarness` (`RenderAtScale`, `MitchellResampleTo`, `AssertByteIdentical`, `AssertReducedScaleExact`, `AssertSupersampleReducesAliasing`; bodies wrapped in `VulkanTestEnvironment.EnsureAvailable()` + `InvokeOnRenderThread`) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenImageHarness.cs`
- [X] T004 [P] Create `ImageMetrics` (`Ssim`/`MeanAbsoluteError`/`AliasingEnergy` over `Bitmap.GetPixelSpan<ushort>()` RgbaF16 linear) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ImageMetrics.cs`
- [X] T005 [P] Create `GoldenThresholds` constants (`ExactSsimMin=0.985`, `ExactMaeMax=0.02`, `SeamMaxDelta=0.05`, `SupersampleSsimMargin=0.01`, `SupersampleFactors={1.5,2.0,4.0}`) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenThresholds.cs`
- [ ] T006 [P] Add `BEUTL_GOLDEN_UPDATE=1` baseline-regeneration switch + `Assets/Golden/**` embedded-resource wiring (raw RgbaF16 `.bin`, not PNG) in `tests/Beutl.UnitTests/Beutl.UnitTests.csproj`
- [ ] T007 [P] SC-008 non-GPU search test `NoPixelCouplingOnRenderPathTest`. **Status:** NOT added — the strict "no `ToSize(1)` / no `(int)` cast on the render path" premise does not hold and shouldn't (e.g. `DrawBackdrop` legitimately builds a LOGICAL `ToSize(1)` rect; effect sinks deliberately keep `(int)` truncation at `w=1` for byte-identity). A meaningful version needs an allowlist of the legitimate sites; deferred in favor of the behavioural buffer-activation goldens (`CustomEffectSupersampleTests`, etc.) which prove the migration is no longer partial.

**Checkpoint**: value types compile; harness + search test build and run (search test green only after the migration lands).

---

## Phase 2: Foundational — Slice 0 plumbing skeleton (Blocking Prerequisites)

**Purpose**: thread the three scales with `w = 1` everywhere. **Gate: byte-identical at scale 1.0 + dual-target build.** BLOCKS every user story.

- [~] T008 ~~Add `virtual ResolutionPolicy ResolutionPolicy => Inherit` to `RenderNode` / `FilterEffect`~~ **REVERTED (2026-06-09)** with the `ResolutionPolicy` removal — neither virtual exists; the working scale is supply-driven. (`RenderNode.ResolutionPolicy` was never wired even before the revert — it would have been a dead duplicate.)
- [X] T009 Add get-only `OutputScale` (default `1f`), `MaxWorkingScale` (default `+∞`), and `static float ResolveWorkingScale(...)` to `RenderNodeContext` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`. **Deviation as shipped**: `maxWorkingScale` is an explicit method parameter (default `+∞`) and a get-only context property, supplied by the caller (wired in T061), rather than a mutable static global — orthogonal + unit-testable. The originally-planned `PreserveFloor` was removed with FR-036. All policy branches implemented (T036 becomes verification).
- [X] T010 Add read-only `EffectiveScale EffectiveScale` (default `Unbounded`, non-abstract) to `RenderNodeOperation` + `EffectiveScale effectiveScale = default` on `CreateLambda`/`CreateFromRenderTarget`/`CreateFromSurface`/`CreateDecorator` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeOperation.cs`
- [X] T011 Add `float outputScale = 1f` ctor param + `OutputScale` getter to `RenderNodeProcessor`; seed `new RenderNodeContext(input, OutputScale)` at the single `Pull` site (`:121`); add internal `RasterizeAt(op, w)` seam generalizing `RasterizeToRenderTargets` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`
- [X] T012 Switch the three rasterization sinks (`RenderNodeProcessor.cs:26,52,75`) to `PixelRect.FromRect(op.Bounds, w)` + pre-push `Matrix.CreateScale(w)` with the **equal-scale short-circuit** (`w==1`→today's path) in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`
- [X] T013 [P] Add `float renderScale = 1f` ctor param + `RenderScale`/`DeviceSize` (`ceil(FrameSize×s_out)`) to `Renderer` and `IRenderer` (default-interface-impl → `1f`/`FrameSize`); keep `FrameSize` logical; `HitTest`/`RecalculateBoundaries` pass `1f` in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs` and `src/Beutl.Engine/Graphics/Rendering/IRenderer.cs`
- [X] T014 [P] Add `float renderScale = 1f` to `SceneRenderer` ctor, forward to `base(...)` in `src/Beutl.ProjectSystem/SceneRenderer.cs`
- [X] T015 [P] Add `float outputScale = 1f` + `OutputScale` to `GraphicsContext2D` ctor in `src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`. **Deviation**: `DrawBackdrop` keeps `canvasSize.ToSize(1)` (logical) — `canvasSize` is the logical `FrameSize` and the root CTM applies the scale, so pre-scaling the backdrop's logical bounds would shrink them at `s>1`. Capture-scale reconciliation moves to T046 (FR-021).
- [X] T016 Update all in-tree consumers of the changed ctors per `contracts/public-api.md`: NodeGraph (`NodeGraphFilterEffectRenderNode` passes `context.OutputScale`), the full `new GraphicsContext2D(...)` call-site inventory, and `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs:181` (nested `new Renderer(...)`); thumbnail/fixed-size sites stay `1f` — across `src/Beutl.NodeGraph/`, `src/Beutl.Engine/`, `src/Beutl.ProjectSystem/`, `src/Beutl/`
- [ ] T017 Byte-identity gate test vs a committed pre-feature `.bin` baseline. **Status:** the frozen-baseline form is DEFERRED pending the CI GPU-lane decision (plan.md residual) — a MoltenVK-specific `.bin` would break on a different golden backend. Byte-identity at `s=1` is currently guarded by: (a) every scale-aware path keeping a `w == 1` / `scale == 1` branch that is character-identical to the pre-feature code; (b) `ScaleOne_IsDeterministic` / `BlurredShape_ScaleOne_IsDeterministic` (determinism on the live path); (c) every reduced-scale + export golden comparing against a ground-truth/structural reference. When the GPU lane is pinned, freeze the `.bin` and flip to `AssertByteIdentical`.
- [X] T018 SC-002 no-migration regression: load a committed pre-feature `.belm`/`.bobj` project fixture and assert it opens with **zero** migration steps and no file-format version bump in `tests/Beutl.UnitTests/ProjectSystem/NoMigrationRegressionTests.cs`
- [~] T019 Dual-target build gate. **net10.0 verified locally** (editor app + `Beutl.UnitTests` + `Beutl.Graphics3DTests` green; 107 compositor/rendering non-GPU tests pass). `net10.0-windows` is **CI-only on macOS** (Windows Desktop SDK unavailable on darwin) — must be confirmed by CI. Note: `dotnet build Beutl.slnx -f net10.0` (whole solution) fails with `NETSDK1005` on the `netstandard2.0` source-generator/SDK projects — build per-project or without `-f` instead.

**Checkpoint**: dual-target build green; `dotnet test` green incl. byte-identity (T017) + no-migration (T018); `NoPixelCouplingOnRenderPathTest` still red until US1/US2 adopt scaling.

---

## Phase 3: User Story 1 — Faster preview, faithful at export (Priority: P1) 🎯 MVP

**Goal**: whole-frame reduced-scale preview for vector + Skia-filter content; byte-identical at export 1.0.
**Independent test**: render a vector+Skia-filter scene at 0.5×, Mitchell-upscale, SSIM ≥ 0.985 vs 1.0; render at 1.0 byte-identical to pre-feature.

### Tests for User Story 1

- [~] T020 [P] [US1] Golden byte-identity @ 1.0 for a vector + Skia-filter scene in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/Slice1ByteIdentityTests.cs`
- [X] T021 [P] [US1] SSIM @ 0.5 reduced-scale-exact for shapes/text/gradients/Blur/DropShadow/color effects, **including a Fit-derived fractional scale (e.g. 0.333)** so non-power-of-two preview renders correctly (FR-035), in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/Slice1ReducedScaleTests.cs`
- [X] T022 [P] [US1] Root-surface sizing assertion `surface == ceil(FrameSize×s)` for `s ∈ {1, 0.5, 0.25, 0.333, 1.5, 2}` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RootSurfaceSizingTests.cs`

### Implementation for User Story 1

- [X] T023 [US1] Root output scale in `Renderer.Render`: push one `Matrix.CreateScale(RenderScale)` around the drawable loop (extracted `RenderObjects`), `RenderScale==1`→exact pre-feature path (byte-identical); FPS overlay stays unscaled; device surface already `ceil(FrameSize×RenderScale)` from T013; `GraphicsContext2D(node, FrameSize)` stays logical in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`
- [~] T024 [US1] **Satisfied by the root CTM for the MVP**: `TextRenderNode` emits a lambda that calls `canvas.DrawText(textBlob)` directly, so under `CreateScale(s)` Skia re-rasterizes glyph outlines at device resolution (crisp, like vector) — no functional gap for uniform output scale. Exact target-scale **reshaping** of the `FormattedText` shaping cache (`_textBlob`/paths/metrics, hinting-level) is **deferred** as a quality refinement; not touched to preserve scale-1 byte-identity. Hit-test fill/stroke remain logical (D3).
- [~] T025 [US1→Phase 4] **Re-sequenced to Phase 4 with T034.** A device-res cache target drawn under the root CTM `scale(s)` double-scales (DrawSurface draws 1 surface-px per CTM-unit), so crisp device-res caching REQUIRES the `ImmediateCanvas.DrawSurface`-with-dest-rect resample (T034). For US1 the cache stays logical-res (single-scale soft upscale under the root CTM; byte-identical at `s=1`). `CachedWorkingScale` + scale-keyed reuse land with T034.
- [X] T026 [US1] **Verified by design**: Skia-filter-mode effects (Blur/DropShadow/color/gradients) draw via `SKImageFilter` whose sigma/offset are in local space; the root `CreateScale(s_out)` CTM scales them for free — no `FilterEffectContext` API change in Slice 1 (that is Phase 4). Golden coverage `Slice1SkiaFilterTests.cs` authored with the harness (T003–T006) under the Vulkan gate.

**Checkpoint**: "preview at 0.5×" renders a vector+Skia-filter scene faster; T020–T022 green; export at 1.0 byte-identical. **MVP shippable.**

---

## Phase 4: User Story 2 — Resolution-independent properties, per-effect + mixed-scale (Priority: P1)

**Goal**: every effect/brush/particle/audio-visualizer honors `w`; differently-scaled subtrees composite correctly (supply-driven; cap deferred to root).
**Independent test**: per-effect golden manifest (exact SSIM / best-effort structural invariant); full-res shape over reduced-res nested scene composites within tolerance, no seam.

### Tests for User Story 2

- [~] T027 [P] [US2] FR-009 survey + per-effect faithfulness gate. **Landed:** `EffectScaleSurveyTests.cs` (the 0.5× reduced-scale survey across effect categories) and `CustomEffectSupersampleTests.cs` (the real density gate — Mosaic 2×-delivered vs 1:1 SSIM 1.0000 proving logical tiles are preserved AND closer-to-truth proving density gain). **Deferred:** the exhaustive per-property `[TestCaseSource]` manifest (`EffectScaleManifestTests.cs`) incl. particles + audio visualizers.
- [ ] T028 [P] [US2] Mixed-scale scenario (full-res shape over reduced-res nested `SceneDrawable`): SSIM + MAE + boundary-seam ≤ 0.05 vs full-scale reference in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/MixedScaleCompositingTests.cs`
- [ ] T029 [P] [US2] Particle + audio-visualizer reduced-scale tests (byte-identity @ 1.0; structural invariant @ 0.5) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ParticleAudioScaleTests.cs`

> **✅ BUFFER ACTIVATION IMPLEMENTED + VERIFIED ON MoltenVK (correcting the earlier "unnecessary" note).**
> The earlier ledger concluded `×w` was unnecessary because its only evidence was 0.5× DOWNSCALE
> fidelity (where every effect rides the root CTM). That conclusion did NOT cover the shipped s_out>1
> export-supersampling feature, for which render-target ("Custom") effects were proven to render at
> LOGICAL density and be soft-upscaled by the CTM. Buffer activation is now implemented:
> - `FilterEffectActivator.Flush` sizes flushed buffers `ceil(OriginalBounds×w)` + `CreateScale(w)` and
>   tags them `EffectiveScale.At(w)`; `CustomFilterEffectContext.CreateTarget` does the same.
> - `EffectTarget.Draw` / `RenderNodeOperation.CreateFromRenderTarget|FromSurface` resample a concrete-
>   scale buffer into its logical rect via `DrawRenderTargetScaled`/`DrawSurfaceScaled`; `w==1` keeps the
>   bare point-blit (byte-identical).
> - Custom effects multiply ABSOLUTE-length pixel params by `w`: Mosaic `tileSize×w`, DisplacementMap
>   translate/pivot `×w`, PartsSplit contour bounds `/w`, SKSL/GLSL resolution uniforms `×w` (+ `iScale`).
>   Skia `SKImageFilter` primitives (blur sigma, etc.) are deliberately NOT `×w` — they ride the root CTM
>   and stay crisp (so Blur's 1.0000 is preserved).
> - 3D scenes render at `ceil(size×s_out)` (`Scene3DRenderNode`) and report `EffectiveScale.At(s_out)`.
> Verified: `CustomEffectSupersampleTests` (Mosaic 2×-delivered vs 1:1 SSIM=1.0000 → logical tiles
> preserved; MAE-to-truth ss<1:1 → real density), all reduced-scale + export goldens still green,
> byte-identity at s=1 preserved (all `w==1` branches are character-identical to the pre-feature path).
> **KNOWN LIMITATION (deferred):** DrawableBrush / TileBrush FILL content (`BrushConstructor`) is still
> logical-density (soft fill at s_out>1; the filled shape's edges stay crisp) — full TileBrush density
> needs per-TileMode/Transform goldens and is scoped out to avoid shipping mistiled brushes.

- [X] T030 [US2] **Landed.** `FilterEffectContext` ctor `(outputScale, workingScale)` + `WorkingScale`/`OutputScale` accessors (FR-015), propagated through `Clone`/`CreateChildContext`; `w` resolved & threaded by `FilterEffectRenderNode` into the activator. (Skia `SKImageFilter` primitives intentionally NOT `×w` — they ride the root CTM; see the note above.)
- [X] T031 [US2] **Landed.** `CustomFilterEffectContext.WorkingScale` added; `CreateTarget` sizes `ceil(bounds×w)` for `w≠1` keeping `(int)` at `w=1` and tags `EffectiveScale.At(w)`; the Custom effects draw over the device buffer (`SKSLShader.ApplyToNewTarget` device-rect) in `src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs`
- [X] T032 [US2] **Landed.** `FilterEffectActivator.Flush`: `ceil(OriginalBounds×w)` for `w≠1` keeping `(int)` at `w=1`; `CreateScale(w)`; tags result ops `EffectiveScale.At(w)`; `FilterEffectRenderNode` threads `w` into the activator and propagates `i.Scale` to the output op in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs`
- [X] T033 [US2] `EffectTarget`: added `EffectiveScale Scale` (default `Unbounded`) set from the producing op, threaded through the `RenderTarget` ctor + `Clone`, and now CONSUMED by `EffectTarget.Draw` (resampled blit when `At(w)`); removed obsolete `Empty`/`Size`. Byte-identical (Scale defaults Unbounded). **`EffectTargets.MaxScale()`/`ResolveScale()` and the inert `RenderNodeContext.PreserveFloor` property were removed** — they had no consumer (the supply `max` is computed inside `ResolveWorkingScale`; there is **no** `preserveFloor` resolver parameter — `ResolveWorkingScale(inputs, outputScale, policy, maxWorkingScale)`).
- [X] T034 [US2] **Landed + wired.** `ImmediateCanvas.DrawRenderTargetScaled` / `DrawSurfaceScaled` / `DrawBitmapScaled` resample a concrete-scale buffer into a LOGICAL dest rect (Mitchell) via `DrawImage`; the Unbounded / `w==1` paths keep the bare 1:1 blit (byte-identity). Consumed by `EffectTarget.Draw`, `RenderNodeOperation.CreateFromRenderTarget|FromSurface`, and the snapshot-backdrop path in `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`
- [~] T035 [US2] Compositor: `targetScale = max(concrete child EffectiveScale)` (Unbounded excluded), cap to `s_out` only at the root (no per-effect clamp — the `ResolutionPolicy` was removed); resample off-target bitmap ops, regenerate `Unbounded` ops at `targetScale` (via `RasterizeAt`) in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`. **As shipped (distributed; Finding-1 re-analysed benign — 2026-06-09):** the rule is **distributed, not centralized in `RenderNodeProcessor`** — `LayerRenderNode.Process` computes the `max(concrete child)` density and per-op resampling rides `ImmediateCanvas.Draw*Scaled` (T034). Codex flagged `LayerRenderNode` over-reporting (it reports `max(child)` but `PushLayer` does `Canvas.SaveLayer()` at the current CTM density). On closer analysis this is **benign**: (a) the layer op is a `CreateLambda` that **re-renders its children every time it is drawn**, so the `SaveLayer` always allocates at the *consumer's* canvas density and children rasterise at that density — there is no frozen low-density buffer; the `At(max child)` value is a correct "max useful density" hint, not a promise of pre-baked pixels. (b) **No built-in creates a `LayerRenderNode`** — `GraphicsContext2D.PushLayer` is called only from `GraphicsContext2DTests`, so the path is unreachable in production. (The cached-tile case — a frozen tile defaulting to `Unbounded` — is the separate **T025** cache-scale deferral, not this.) The genuine remaining T035 work (centralised `RasterizeAt` regeneration) is an optional consolidation, not a correctness fix.
- [X] T036 [US2] `ResolveWorkingScale` (supply-driven) + the `maxWorkingScale` ceiling implemented in `RenderNodeContext` (landed with T009) and **validated by non-GPU unit tests** (`ResolutionScaleTests.cs`) incl. R1 (0.5 proxy not upsampled) and R2 (2.0 source not clamped). **Updated 2026-06-09:** the policy branches (`Inherit`/`ClampToOutput`/`Oversample`) and the `policy` parameter were **removed** with the `ResolutionPolicy` type; the rule collapsed to `min(supply, maxWorkingScale)` (supply = densest concrete input; vector/mixed floor at `s_out`). The policy-specific tests (`ClampToOutput`/`Oversample`/C8 escape) were dropped.

### Implementation for User Story 2 — per-effect clusters (FR-009 traceability over the ~40-effect matrix)

- [X] T037 [US2] **Supply-driven + buffer activation landed.** `Dilate`/`Erode`/`FlatShadow` run supply-driven (no per-effect policy). The sigma/offset/radius are Skia `SKImageFilter` primitives that ride the root CTM (no `×w` — verified Blur 1.0000), and any flushed buffer they share is sized `ceil(bounds×w)` by the activator, so they gain density under supersampling without per-param scaling.
- [X] T038 [US2] **Supply-driven + buffer activation landed + verified.** `Mosaic`/`Displacement`/`PixelSort` run supply-driven; `Mosaic.tileSize×w`, DisplacementMap translate/pivot `×w` (with a null-shader guard at `w≠1`), PixelSort is content-relative (no abs param, auto-scales). Verified by `CustomEffectSupersampleTests`.
- [X] T039 [US2] **Confirmed magnitude-invariant → no `w`**: the `SKColorFilter`-based color effects (`Brightness`/`Gamma`/`Saturate`/`HueRotate`/`Invert`/`Negaposi`/`ColorGrading`/`Curves`/`HighContrast`/`LumaColor`/`LutEffect`/`Threshold`/`ChromaKey`/`ColorKey`) have no spatial params and correctly need no change. **`ColorShift` pixel-offset `×w` is DONE** (`ColorShift.OnApply` multiplies every offset + `minOffset` by `context.WorkingScale`; `TransformBoundsCore` stays logical and is correctly scaled by the `ceil(bounds×w)` buffer allocation — the earlier "deferred" note was stale). `ContourTracer`-based effects ride buffer activation (T040).
- [X] T040 [US2] **Supply-driven + buffer activation landed.** `Stroke`/`Clipping`/`PartsSplit` run supply-driven (no per-effect policy); PartsSplit converts its device-pixel contour bounds back to logical (`/w`) so `CreateTarget` re-densifies; Stroke/Clipping are Skia-`SKImageFilter` (ride the CTM). Custom-effect authors read `CustomFilterEffectContext.WorkingScale` for any absolute-px param. **`Shake.Strength` is already scale-correct** — it translates the effect target's *logical* bounds, which the pipeline scales by `×w` at buffer allocation, so multiplying by `w` would double-scale it (empirically confirmed: `Tier1ParameterScaleProbeTests.ShakeEffect_LogicalDisplacement_AcrossScale` SSIM 0.999 across scale; the earlier "follow-up" note was wrong). `LayerEffect` mixed-scale flatten remains a follow-up (Tier 3).
- [X] T041 [US2] SKSL/GLSL: scale uniform. **Deviation as shipped**: SKSL exposes `iScale` = `w` and scales `width`/`height`/`iResolution` by `w`; GLSL ships **no** scale uniform — the author derives `w` from the device-px `Width`/`Height` (see shader-uniforms.md). Scale-unaware shaders behave as `w=1.0`. In `src/Beutl.Engine/Graphics/FilterEffects/SKSLScriptEffect.cs`, `GLSLScriptEffect.cs`, `GLSLShader.cs`
- [ ] T042 [US2] Brushes: tile/image/drawable intermediate raster × `w`; `DrawableBrush` child inherits `w`; opacity-mask threads `w` in `src/Beutl.Engine/Graphics/TileBrushCalculator.cs`, `src/Beutl.Engine/Graphics/Rendering/OpacityMaskRenderNode.cs`. **`PerlinNoiseBrush.BaseFrequency ÷ w` was DROPPED — empirically disproven (2026-06-09):** `Tier1ParameterScaleProbeTests` shows 0.5× SSIM 0.70, but dividing `BaseFrequency` by the render scale made it **WORSE (0.63)** — `SkPerlinNoiseShader` already follows the CTM (the noise period is logical-invariant), so 0.70 is the inherent best-effort downsampling loss of a high-frequency procedural texture (FR-013), not a frequency mismatch. The dossier's "÷w" recommendation is wrong for the shipped pipeline; PerlinNoise needs no param scaling and threads no scale. The remaining tile/drawable-intermediate work is its own item (the device-coupled bit is the TileBrush **fill** intermediate raster size, A-1 / Tier 3), NOT a shared "scale into BrushConstructor" change.
- [X] T043 [US2] **Landed.** `ParticleRenderNode` inherits `w = context.OutputScale`, rasterizes the per-particle drawable into a `ceil(bounds×w)` buffer (threading `w` into the inner `GraphicsContext2D` + `RenderNodeProcessor`), blits it scaled into its logical footprint, tags the op `EffectiveScale.At(w)`, and rebuilds the cache when the scale changes. (The `1920×1080` literal remains only as the LOGICAL measurement-canvas hint; the buffer is sized from bounds, not it.) The per-particle props (position, size ratio, rotation) are logical and ride the CTM, so none need a manual `×w`. (FR-029) in `src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs`
- [X] T044 [US2] Audio-visualizer drawables: **already scale-correct — verified by code analysis (2026-06-09), no change needed.** `BarSpectrumShape`/`SpectrumShape`/waveform shapes draw their geometry (`slotWidth = width/barCount`, `barWidth`, `barHeight`, bar rects) in **logical bounds coordinates** via `canvas.DrawRectangle`/`DrawRect`, so the root `CreateScale(s)` CTM scales them to device exactly like any shape (same mechanism as `ShakeEffect`). The hard-coded `MathF.Max(0.5f, BarWidth)` / `MathF.Max(1f, …)` minimums are **logical-unit floors**, not device-px, so they scale correctly too. No intermediate buffer is allocated (direct canvas draw), so there is no device-coupling to fix. The "device-baked ×w" premise was a holdover from the old device-px model.
- [~] T045 [US2] 3D as mixed-scale bitmap: `Scene3DRenderNode` renders at `ceil(size×s_out)` and tags the surface op `EffectiveScale.At(w)` (w==s_out), resampled at the boundary — **done**; nested `SceneDrawable` now inherits the outer `s_out`/ceiling into its own `Renderer` and reports `At(w)` (FR-022) — **done** (`src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`). **Remaining (perspective append): RESOLVED — already correct, probe-verified (2026-06-09).** `Perspective3DScaleProbeTests` renders a `Rotation3DTransform` child across scales: SSIM **0.998** at 0.5× and **1.0000** at 2× SSAA. The root `Matrix.CreateScale(s)` is pushed at the device boundary (`Renderer.Render`), so the perspective projection runs in logical space and the device scale effectively post-multiplies the projected output — the `S·P≠P·S` trap is structurally avoided. No `Rotation3DTransform` change needed.
- [X] T046 [US2] Backdrop/snapshot scale-aware: **DONE** (verified 2026-06-09). `ImmediateCanvas.Snapshot()` tags the capture with its `OutputScale` (`new TmpBackdrop(_renderTarget.Snapshot(), OutputScale)`, CSM-3) and the replay un-scales by that capture scale via `DrawBitmapScaled` into the logical dest rect, so a backdrop captured at density `w` is not double-scaled on replay. Covered by `BackdropScaleTests` (`SnapshotBackdrop_ReplayedInFlush_NotDoubleScaled`, `SnapshotBackdropRenderNode_CapturedOnNestedFlushCanvas_NotDoubleScaled` — both green).

**Checkpoint**: every built-in effect + particles + audio visualizers honor `w`; mixed-scale composites correctly; T027–T029 green; `NoPixelCouplingOnRenderPathTest` green.

---

## Phase 5: User Story 3 — Proxy-ready foundation (Priority: P2)

**Goal**: the media seam — decoded resolution becomes the op's `EffectiveScale`, distinct from the logical footprint; `MediaOptions` kept additively extensible. (Reduced-decode itself is the deferred proxy feature.)
**Depends on**: US2 compositor (T035) + `ImmediateCanvas` resample (T034).
**Independent test**: at a fixed logical size, two different-resolution bitmaps of the same content → identical logical bounds, only `EffectiveScale` differs; media-open with no decode hint identical to today.

### Tests for User Story 3

- [ ] T047 [P] [US3] SC-007 seam test (fixed logical size, two-resolution bitmaps → same bounds/hit region, differing `EffectiveScale`; composites into `logicalSize×s`) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/MediaScaleSeamTests.cs`
- [X] T048 [P] [US3] `MediaOptions` additive test (no decode-scale hint → behaviorally identical to today) in `tests/Beutl.UnitTests/Engine/Media/MediaOptionsExtensibilityTests.cs`

### Implementation for User Story 3

- [ ] T049 [US3] Map source logical size → decoded pixel size via a dest rect (not 1:1); `ImageSourceRenderNode`/`VideoSourceRenderNode` emit `EffectiveScale = decodedPixels / logicalSize` (FR-023/FR-024) in `src/Beutl.Engine/Graphics/Rendering/ImageSourceRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs`, `src/Beutl.Engine/Graphics/SourceImage.cs`, `src/Beutl.Engine/Graphics/SourceVideo.cs`
- [ ] T050 [US3] `DrawBitmap`/`DrawImageSource`/`DrawVideoSource`: draw into a logical dest rect `logicalSize×w` (not native-px 1:1) in `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`
- [X] T051 [US3] Confirm `MediaOptions` stays additively extensible for a future decode-scale hint; document the seam (no IPC/worker change in 003) in `src/Beutl.Engine/Media/Decoding/MediaOptions.cs`

**Checkpoint**: T047–T048 green; proxy/optimized-media can later slot in by lowering the decoded size without touching layout.

---

## Phase 6: User Story 4 — Editor parity + export (Priority: P2)

**Goal**: preview-scale control (per-edit-view, non-persisted) with atomic renderer/cache rebuild; hit-test/handles identical across scales; export supersampling AA.
**Independent test**: hit-test same logical point and equivalent handle drag at two scales → same drawable + identical document Transform; export at `s>1` → output exactly `FrameSize`, lower aliasing.

### Tests for User Story 4

- [~] T052 [P] [US4] Hit-test parity (same logical point → same drawable) + handle-drag parity (identical serialized `Transform`) across two render scales in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/HitTestParityTests.cs`
- [X] T053 [P] [US4] Export size guard (`s>1` → snapshot == `FrameSize` before encode, FR-026) + SSAA aliasing reduction (SC-009) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ExportSupersampleTests.cs`
- [ ] T054 [P] [US4] `PreviewScale` non-persistence test (set Quarter → `SaveState`/`RestoreState` → resets to Full; no scale key in `.config`) in `tests/Beutl.UnitTests/ViewModels/EditViewModelPreviewScaleTests.cs`

### Implementation for User Story 4

- [X] T055 [US4] `RenderScale` enum (`Full`/`Half`/`Quarter`/`FitToPreviewer`) + `ToFloat(PixelSize, Size)` clamped to `(0,1]` (preview never upscales) in `src/Beutl/Models/RenderScale.cs`; compiles in the `Beutl` app. **Test-project gap**: no test project references the `Beutl` app, so `RenderScale` (and the US4 ViewModel tests T052–T054, which target `tests/Beutl.UnitTests/ViewModels/` but that project references `Beutl.Editor`, not `Beutl`) cannot be unit-tested as-specced. Resolve by adding a `Beutl`-app test project (or relocating the tested logic) when wiring Phase 6.
- [X] T056 [US4] `EditViewModel`: add non-persisted `ReactivePropertySlim<RenderScale> PreviewScale`; combine `(FrameSizeProperty, PreviewScale)` into one observable that rebuilds `SceneRenderer` + `FrameCacheManager` (`DisposePreviousValue`) on the render dispatcher; leave `SaveState`/`RestoreState` untouched in `src/Beutl/ViewModels/EditViewModel.cs`. Any XAML surfacing the preview-scale selector MUST declare `x:CompileBindings="True"` + `x:DataType` (Constitution IV)
- [~] T057 [US4] `PlayerViewModel`: subscribe `PreviewScale` → `QueueRender()`; render lambda reads `Renderer.Value`/`FrameCacheManager.Value` fresh inside the dispatcher (atomic swap, FR-031) in `src/Beutl/ViewModels/PlayerViewModel.cs`
- [~] T058 [US4] Logical hit-test/handles: `Renderer.HitTest` + `TransformHandlesOverlay`/`PlayerView` divide the pointer by **display zoom only**; keep render scale out of `Matrix.TryDecomposeTransform`/the artistic matrix; 3D gizmo (FR-027) in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`, `src/Beutl/Views/TransformHandlesOverlay.cs`, `src/Beutl/Views/PlayerView.axaml.cs`
- [X] T059 [US4] Export supersampling: `OutputViewModel` offers **Off / 2× / 4×** and threads the factor into `SceneRenderer`; `FrameProviderImpl.RenderCore` downscales `Snapshot()` to `FrameSize` when `RenderScale>1` (**Mitchell for ≤2×, trilinear+mipmaps for 4×**); derive `VideoEncoderSettings.SourceSize` from the actual buffer and assert `== FrameSize` before encode (FR-026/FR-034) in `src/Beutl/ViewModels/Tools/OutputViewModel.cs`, `src/Beutl/Models/FrameProviderImpl.cs`

**Checkpoint**: preview-quality control rebuilds atomically; clicks/handles land identically at any scale; export SSAA delivers output-resolution AA; T052–T054 green.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [~] T060 [P] SC-003 benchmark: committed `bench.scene` + ratio test (`median(0.5)/median(1.0) < 0.6`) `[Explicit][Category("Benchmark")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/RenderScaleBenchmarkTests.cs` + a BenchmarkDotNet entry in `tests/Beutl.Benchmarks/RenderScaleBenchmark.cs`
- [X] T061 Wire the pinned values. **As shipped**: `MaxWorkingScale` = **`2 × s_out` preview / `max(8, 4 × s_out)` export** (FR-037; the export bound is the finite C7 cap, not `+∞`) is threaded `Renderer → RenderNodeProcessor → RenderNodeContext → FilterEffectRenderNode` and seeded at the editor preview `SceneRenderer` and `OutputViewModel`. **The per-effect `ResolutionPolicy` was removed entirely (2026-06-09)** — every built-in is supply-driven; `MaxWorkingScale` is the sole upper bound. Supersample factors **Off / 2× / 4×** (SC-009) are in `src/Beutl/ViewModels/Tools/OutputViewModel.cs`.
- [X] T062 [P] Document `iScale`/`uScale` + the working-scale shader contract for authors in `docs/ai-workflow/` (reference `contracts/shader-uniforms.md`)
- [ ] T063 If any effect property's type/unit changes (e.g. `ColorShift` `PixelPoint`→`Point`): keep the source generator green with a compile-only case in `tests/SourceGeneratorTest/RenderContext.cs` plus behavioral coverage in `tests/Beutl.UnitTests/`
- [ ] T064 Ship as a breaking change: `refactor!:`/`feat!:` + `BREAKING CHANGE:` footer (naming `Beutl.Engine`/`Beutl.NodeGraph`/`Beutl.ProjectSystem`); route the public surface through `beutl-design-reviewer`; run `dotnet format Beutl.slnx` + `dotnet build Beutl.slnx` + `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings`
- [ ] T065 Run the [quickstart.md](./quickstart.md) validation loop (the four golden gates + the preview/export walkthroughs) — runbook step, no source file

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2, Slice 0)**: depends on Setup — **BLOCKS all user stories**. Gate: dual-target build (T019) + byte-identical at 1.0 (T017) + no-migration (T018).
- **US1 (Phase 3)**: depends on Foundational. The MVP.
- **US2 (Phase 4)**: depends on Foundational; builds on US1's root-scale + cache. Carries the compositor (T035) + `ImmediateCanvas` resample (T034) that US3/US4 reuse. The per-effect clusters (T037–T040) depend on the context core (T030–T036).
- **US3 (Phase 5)**: depends on Foundational + US2 T034/T035.
- **US4 (Phase 6)**: depends on Foundational; the mixed-scale preview path uses US2's compositor, but the control/hit-test/export tasks are independent.
- **Polish (Phase 7)**: after the desired user stories.

### Parallel opportunities

- **Phase 1**: T001–T007 all `[P]`.
- **Phase 2**: T013/T014/T015 are `[P]` (distinct types); T011/T012 must follow T009/T010 (same processor/context pair).
- **Within each US**: the `[P]` test tasks and distinct-file impl tasks run in parallel. In US2, once the core (T030–T036) lands, the per-effect clusters (T037–T040) and T041–T046 are largely `[P]` across distinct effect files.

---

## Implementation Strategy

1. **MVP = Phase 1 + Phase 2 + Phase 3 (US1)** — reduced-scale preview for the common vector+Skia-filter case, with the byte-identical-at-1.0 + no-migration regression guards. Ship this first.
2. **Increment 2 = Phase 4 (US2)** — full effect coverage (per-effect clusters) + mixed-scale; the heaviest phase, gated by the per-effect golden manifest.
3. **Increment 3 = Phase 5 (US3)** — the proxy seam.
4. **Increment 4 = Phase 6 (US4)** — editor preview control + export SSAA.
5. **Polish (Phase 7)** — benchmark, pinned residuals, docs, breaking-change PR.

**Hard invariant across every phase** *(superseded 2026-06-08 — see the requirements.md amendment log)*: ~~`s_out = 1.0` with unit-scale inputs MUST stay byte-identical~~. The coherent density model abolished universal byte-identity as a design constraint: a transform now re-scales a bitmap's density (FR-019) and a scaled bitmap into an effect is intentionally not byte-identical at `s_out = 1.0`. What REMAINS invariant: vector / Skia-filter / unscaled-bitmap content stays byte-identical at `s_out = 1.0`, and the two filter-effect sinks keep their `(int)` rounding at `w = 1.0` (the per-sink fast paths are untouched).
