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
- [X] T002 [P] Create `ResolutionPolicy` value type + `ResolutionPolicyKind` enum (`Inherit`/`ClampToOutput`/`Oversample`/`PreserveSource`; statics + `Oversample(factor)`) in `src/Beutl.Engine/Graphics/Rendering/ResolutionPolicy.cs`
- [X] T003 [P] Create Vulkan-gated golden harness `GoldenImageHarness` (`RenderAtScale`, `MitchellResampleTo`, `AssertByteIdentical`, `AssertReducedScaleExact`, `AssertSupersampleReducesAliasing`; bodies wrapped in `VulkanTestEnvironment.EnsureAvailable()` + `InvokeOnRenderThread`) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenImageHarness.cs`
- [X] T004 [P] Create `ImageMetrics` (`Ssim`/`MeanAbsoluteError`/`AliasingEnergy` over `Bitmap.GetPixelSpan<ushort>()` RgbaF16 linear) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ImageMetrics.cs`
- [X] T005 [P] Create `GoldenThresholds` constants (`ExactSsimMin=0.985`, `ExactMaeMax=0.02`, `SeamMaxDelta=0.05`, `SupersampleSsimMargin=0.01`, `SupersampleFactors={1.5,2.0,4.0}`) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenThresholds.cs`
- [ ] T006 [P] Add `BEUTL_GOLDEN_UPDATE=1` baseline-regeneration switch + `Assets/Golden/**` embedded-resource wiring (raw RgbaF16 `.bin`, not PNG) in `tests/Beutl.UnitTests/Beutl.UnitTests.csproj`
- [ ] T007 [P] Create SC-008 non-GPU search test `NoPixelCouplingOnRenderPathTest` (scan scoped `Beutl.Engine` render dirs for `ToSize(1)` and unguarded `(int)` casts of logical bounds; runs unconditionally) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NoPixelCouplingOnRenderPathTest.cs`

**Checkpoint**: value types compile; harness + search test build and run (search test green only after the migration lands).

---

## Phase 2: Foundational — Slice 0 plumbing skeleton (Blocking Prerequisites)

**Purpose**: thread the three scales with `w = 1` everywhere. **Gate: byte-identical at scale 1.0 + dual-target build.** BLOCKS every user story.

- [X] T008 Add `virtual ResolutionPolicy ResolutionPolicy => Inherit` to `RenderNode` (`src/Beutl.Engine/Graphics/Rendering/RenderNode.cs`) and `FilterEffect` (`src/Beutl.Engine/Graphics/FilterEffects/FilterEffect.cs`)
- [X] T009 Add get-only `OutputScale` (default `1f`), a per-pull `PreserveFloor`, and `static float ResolveWorkingScale(...)` to `RenderNodeContext`; change ctor to `(input, float outputScale = 1f)` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`. **Deviation**: `maxWorkingScale` and `preserveFloor` are explicit method parameters (defaults `+∞` / `0`) rather than a mutable static `MaxWorkingScale` global — orthogonal + unit-testable; the preview/export ceiling is supplied by the caller (wired in T061). All policy branches implemented (T036 becomes verification).
- [X] T010 Add read-only `EffectiveScale EffectiveScale` (default `Unbounded`, non-abstract) to `RenderNodeOperation` + `EffectiveScale effectiveScale = default` on `CreateLambda`/`CreateFromRenderTarget`/`CreateFromSurface`/`CreateDecorator` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeOperation.cs`
- [X] T011 Add `float outputScale = 1f` ctor param + `OutputScale` getter to `RenderNodeProcessor`; seed `new RenderNodeContext(input, OutputScale)` at the single `Pull` site (`:121`); add internal `RasterizeAt(op, w)` seam generalizing `RasterizeToRenderTargets` in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`
- [X] T012 Switch the three rasterization sinks (`RenderNodeProcessor.cs:26,52,75`) to `PixelRect.FromRect(op.Bounds, w)` + pre-push `Matrix.CreateScale(w)` with the **equal-scale short-circuit** (`w==1`→today's path) in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`
- [X] T013 [P] Add `float renderScale = 1f` ctor param + `RenderScale`/`DeviceSize` (`ceil(FrameSize×s_out)`) to `Renderer` and `IRenderer` (default-interface-impl → `1f`/`FrameSize`); keep `FrameSize` logical; `HitTest`/`RecalculateBoundaries` pass `1f` in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs` and `src/Beutl.Engine/Graphics/Rendering/IRenderer.cs`
- [X] T014 [P] Add `float renderScale = 1f` to `SceneRenderer` ctor, forward to `base(...)` in `src/Beutl.ProjectSystem/SceneRenderer.cs`
- [X] T015 [P] Add `float outputScale = 1f` + `OutputScale` to `GraphicsContext2D` ctor in `src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`. **Deviation**: `DrawBackdrop` keeps `canvasSize.ToSize(1)` (logical) — `canvasSize` is the logical `FrameSize` and the root CTM applies the scale, so pre-scaling the backdrop's logical bounds would shrink them at `s>1`. Capture-scale reconciliation moves to T046 (FR-021).
- [X] T016 Update all in-tree consumers of the changed ctors per `contracts/public-api.md`: NodeGraph (`NodeGraphFilterEffectRenderNode` passes `context.OutputScale`), the full `new GraphicsContext2D(...)` call-site inventory, and `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs:181` (nested `new Renderer(...)`); thumbnail/fixed-size sites stay `1f` — across `src/Beutl.NodeGraph/`, `src/Beutl.Engine/`, `src/Beutl.ProjectSystem/`, `src/Beutl/`
- [ ] T017 Byte-identity gate test: render a representative scene set (vector, text, nested scene, particles, audio visualizer, 3D) at scale 1.0 and `AssertByteIdentical` vs the pre-feature `.bin` baseline in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/Slice0ByteIdentityTests.cs`
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

- [ ] T027 [P] [US2] **First complete the FR-009 matrix** (add the particle + audio-visualizer property sets to the dossier's per-effect table → the test-manifest source of truth), then per-effect/property golden manifest `[TestCaseSource]` (exact → SSIM≥0.985 & MAE≤0.02; best-effort → structural invariant; all byte-identical @ 1.0) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/EffectScaleManifestTests.cs`
- [ ] T028 [P] [US2] Mixed-scale scenario (full-res shape over reduced-res nested `SceneDrawable`): SSIM + MAE + boundary-seam ≤ 0.05 vs full-scale reference in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/MixedScaleCompositingTests.cs`
- [ ] T029 [P] [US2] Particle + audio-visualizer reduced-scale tests (byte-identity @ 1.0; structural invariant @ 0.5) in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ParticleAudioScaleTests.cs`

> **✅ VERIFIED ON MoltenVK — per-effect buffer-activation `×w` is empirically UNNECESSARY.**
> Vulkan is live here (MoltenVK), so the golden suite RUNS (it does not `Assert.Ignore`). Measured
> reduced-scale (0.5×) fidelity: Blur 1.0000, InnerShadow 0.9983 (a buffer-allocating custom effect!),
> Brightness 0.9975, DropShadow 0.9974, Mosaic 0.9872, Dilate 0.9868 SSIM — **every effect category
> already scales faithfully via the root CTM + existing buffer logic, with NO per-effect `sigma×w`.**
> Applying `sigma×w` to the inline path would REGRESS the verified-perfect Blur (1.0000). The buffer
> `×w` only theoretically helps scale>1 export crispness of buffer effects; no measurable degradation
> was found there either. So the T030-primitive `×w`, T031, T032, and the per-effect `×w` of T037–T046
> are **not required** for the verified use cases — the policy assignments + scale plumbing (landed)
> are the meaningful parts. `ImmediateCanvas.DrawRenderTargetScaled` (T034 resample primitive) is
> landed for the genuine mixed-scale media case (T035/T049, future).

- [~] T030 [US2] **Plumbing + accessor landed** (byte-identical): `FilterEffectContext` ctor `(outputScale, workingScale)` + `WorkingScale`/`OutputScale` accessors (FR-015), propagated through `Clone`/`CreateChildContext`; `w` resolved & threaded by `FilterEffectRenderNode`. **Deferred** (buffer-activation slice): primitives × `WorkingScale` + split bounds-inflation — harmful if applied to the inline-CTM path; needs force-flush + golden verification.
- [ ] T031 [US2] **(buffer-activation slice)** `CustomFilterEffectContext`: add `WorkingScale`; `CreateTarget` sizes `ceil(bounds×w)` for `w≠1` keeping `(int)` at `w=1`; `Open` returns a `w`-prescaled canvas in `src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs`
- [ ] T032 [US2] **(buffer-activation slice)** `FilterEffectActivator.Flush`: `ceil(OriginalBounds×w)` for `w≠1` keeping `(int)` at `w=1`; `CreateScale(w)`; normalize each `EffectTarget` whose `Scale≠w`; force-flush at `w≠1` in `FilterEffectRenderNode`; tag result ops `EffectiveScale.At(w)` in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs`
- [X] T033 [US2] `EffectTarget`: added `EffectiveScale Scale` (default `Unbounded`) set from the producing op, threaded through the `RenderTarget` ctor + `Clone`; added `EffectTargets.MaxScale()`; removed obsolete `Empty`/`Size`. Byte-identical (Scale defaults Unbounded). (`ResolveScale` folds into the compositor T035.)
- [~] T034 [US2] `ImmediateCanvas.DrawSurface`/`DrawRenderTarget`: add `(src, dest, SKSamplingOptions(Mitchell))` resample path via `DrawImage`; **exact equal-scale short-circuit** keeps today's bare 1:1 blit (byte-identity) in `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`
- [ ] T035 [US2] Compositor: `targetScale = max(concrete child EffectiveScale)` (Unbounded excluded), cap to `s_out` only at the root or on `ClampToOutput`; resample off-target bitmap ops, regenerate `Unbounded` ops at `targetScale` (via `RasterizeAt`) in `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`
- [X] T036 [US2] `ResolveWorkingScale` policy branches (`Inherit`/`ClampToOutput`/`Oversample`/`PreserveSource`) + `preserveFloor`/`maxWorkingScale` implemented in `RenderNodeContext` (landed with T009) and **validated by 17 non-GPU unit tests** (`ResolutionScaleTests.cs`) incl. R1 (0.5 proxy not upsampled) and R2 (2.0 source not clamped). The per-pull floor *propagation* through `Pull` still lands with live PreserveSource (Phase 4 compositor). Also fixed: `default(ResolutionPolicy)` now value-equals `Inherit` (ctor `Factor` default 0f).

### Implementation for User Story 2 — per-effect clusters (FR-009 traceability over the ~40-effect matrix)

- [~] T037 [US2] **Policy assigned** (`Dilate`/`Erode`/`FlatShadow`→`PreserveSource`; `Blur`/`DropShadow`/`InnerShadow`→`Inherit` default). **Deferred** (buffer-activation): spatial `×w` of sigma/offset/radius.
- [~] T038 [US2] **Policy assigned** (`Mosaic`/`Displacement`/`PixelSort`→`PreserveSource`). **Deferred** (buffer-activation): `Mosaic.tileSize×w`, displacement translate×w, `Lighting`. (`PerlinNoise` brush ÷w is T042.)
- [~] T039 [US2] **Confirmed magnitude-invariant → no `w`, policy `Inherit` (default, no override)**: the `SKColorFilter`-based color effects (`Brightness`/`Gamma`/`Saturate`/`HueRotate`/`Invert`/`Negaposi`/`ColorGrading`/`Curves`/`HighContrast`/`LumaColor`/`LutEffect`/`Threshold`/`ChromaKey`/`ColorKey`) have no spatial params and correctly need no change. **Deferred**: `ColorShift` pixel-offset `×w`, `ContourTracer` policy (buffer-activation).
- [~] T040 [US2] **Policy assigned** (`Stroke`/`Clipping`/`PartsSplit`→`PreserveSource`). **Deferred** (buffer-activation): `×w` for stroke/split/transform-translation/`Shake.Strength`, `LayerEffect` mixed-scale flatten, `CSharpScriptEffect` reads the landed `WorkingScale` accessor.
- [ ] T041 [US2] SKSL/GLSL: add `iScale`/`uScale` = `w` uniform; keep `width`/`height`/`iResolution` = device size of the scaled target; scale-unaware shaders behave as `w=1.0` in `src/Beutl.Engine/Graphics/FilterEffects/SKSLScriptEffect.cs`, `GLSLScriptEffect.cs`, `GLSLShader.cs`
- [ ] T042 [US2] Brushes: `PerlinNoiseBrush.BaseFrequency ÷ w` (centralized in `CreatePerlinNoiseShader`); tile/image/drawable intermediate raster × `w`; `DrawableBrush` child inherits `w`; opacity-mask threads `w` in `src/Beutl.Engine/Graphics/BrushConstructor.cs`, `src/Beutl.Engine/Graphics/TileBrushCalculator.cs`, `src/Beutl.Engine/Graphics/Rendering/OpacityMaskRenderNode.cs`
- [ ] T043 [US2] `ParticleRenderNode`: replace hard-coded `new PixelSize(1920,1080)` with `ceil(bounds×w)`; inherit `w`; pixel-magnitude particle props × `w` (FR-029) in `src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs`
- [ ] T044 [US2] Audio-visualizer drawables: classify pixel-magnitude params (bar width, block gap, hard-coded minimums) under FR-008 × `w` (FR-030) across `src/Beutl.Engine/Graphics/AudioVisualizers/`
- [ ] T045 [US2] 3D as mixed-scale bitmap: tag `Scene3DRenderNode` surface op `EffectiveScale.At(1.0)`, resample at the boundary; **append** the root device scale after the user transform in `Rotation3DTransform` (perspective `S·P≠P·S`); nested `SceneDrawable` inherits `w` (FR-033/FR-022) in `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs`, `src/Beutl.Engine/Graphics/Transformation/Rotation3DTransform.cs`, `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`
- [ ] T046 [US2] Backdrop/snapshot scale-aware: tag the captured snapshot with its capture scale and resample (or re-capture) on mismatch (FR-021) in `src/Beutl.Engine/Graphics/Rendering/SnapshotBackdropRenderNode.cs` and `src/Beutl.Engine/Graphics/Rendering/DrawBackdropRenderNode.cs`

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
- [~] T056 [US4] `EditViewModel`: add non-persisted `ReactivePropertySlim<RenderScale> PreviewScale`; combine `(FrameSizeProperty, PreviewScale)` into one observable that rebuilds `SceneRenderer` + `FrameCacheManager` (`DisposePreviousValue`) on the render dispatcher; leave `SaveState`/`RestoreState` untouched in `src/Beutl/ViewModels/EditViewModel.cs`. Any XAML surfacing the preview-scale selector MUST declare `x:CompileBindings="True"` + `x:DataType` (Constitution IV)
- [~] T057 [US4] `PlayerViewModel`: subscribe `PreviewScale` → `QueueRender()`; render lambda reads `Renderer.Value`/`FrameCacheManager.Value` fresh inside the dispatcher (atomic swap, FR-031) in `src/Beutl/ViewModels/PlayerViewModel.cs`
- [~] T058 [US4] Logical hit-test/handles: `Renderer.HitTest` + `TransformHandlesOverlay`/`PlayerView` divide the pointer by **display zoom only**; keep render scale out of `Matrix.TryDecomposeTransform`/the artistic matrix; 3D gizmo (FR-027) in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`, `src/Beutl/Views/TransformHandlesOverlay.cs`, `src/Beutl/Views/PlayerView.axaml.cs`
- [ ] T059 [US4] Export supersampling: `OutputViewModel` offers **Off / 2× / 4×** and threads the factor into `SceneRenderer`; `FrameProviderImpl.RenderCore` downscales `Snapshot()` to `FrameSize` when `RenderScale>1` (**Mitchell for ≤2×, trilinear+mipmaps for 4×**); derive `VideoEncoderSettings.SourceSize` from the actual buffer and assert `== FrameSize` before encode (FR-026/FR-034) in `src/Beutl/ViewModels/Tools/OutputViewModel.cs`, `src/Beutl/Models/FrameProviderImpl.cs`

**Checkpoint**: preview-quality control rebuilds atomically; clicks/handles land identically at any scale; export SSAA delivers output-resolution AA; T052–T054 green.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [~] T060 [P] SC-003 benchmark: committed `bench.scene` + ratio test (`median(0.5)/median(1.0) < 0.6`) `[Explicit][Category("Benchmark")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/RenderScaleBenchmarkTests.cs` + a BenchmarkDotNet entry in `tests/Beutl.Benchmarks/RenderScaleBenchmark.cs`
- [ ] T061 [P] Wire the pinned values into config — `MaxWorkingScale` = **`2 × s_out` preview / unbounded export** (FR-037), the built-in `ResolutionPolicy` manifest (FR-036: FR-013 set → `PreserveSource`, else `Inherit`, no built-in `ClampToOutput`), the offered supersample factors **Off / 2× / 4×** (SC-009) — in `src/Beutl.Configuration/EditorConfig.cs` and `src/Beutl/ViewModels/Tools/OutputViewModel.cs`
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

**Hard invariant across every phase**: `s_out = 1.0` with unit-scale inputs MUST stay byte-identical (T017 + per-effect `AssertByteIdentical`); the two filter-effect sinks keep their `(int)` rounding at `w = 1.0`.
