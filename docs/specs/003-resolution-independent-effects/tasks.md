---

description: "Task list for Resolution-Independent Pixel-Absolute Rendering"
---

# Tasks: Resolution-Independent Pixel-Absolute Rendering

**Input**: Design documents from `docs/specs/003-resolution-independent-effects/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/effect-helper-scaling.md`, `contracts/graphics-context-scaling.md`, `contracts/pen-scaling.md`, `contracts/transform-scaling.md`, `contracts/render-scale.md`, `quickstart.md`

**Tests**: Tests are REQUIRED (Constitution III, FR-011). Every implementation task is paired with a test task written first.

**Organization**: Tasks are grouped by user story. US1 + US2 are both P1; US3 is P2.

> **History**: Original draft (`2ad7bb5ae`, 57 tasks) was wrapper-type design. First pivot (`1b7e32fe1`, 62 → 36 tasks) moved to helper-internal scaling on `FilterEffectContext`. Second expansion (this commit) broadens scope to `GraphicsContext2D` / `Pen` / `Transform` / `Shape` per `spec.md` § Clarifications 2026-05-21 and `research.md` § R8 / R9 / R10. Net: ~48 tasks.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3) — Setup / Foundational / Polish have no story label
- Include exact file paths in descriptions

## Path Conventions

Engine code under `src/Beutl.Engine/`; project-system under `src/Beutl.ProjectSystem/`; tests under `tests/Beutl.UnitTests/Engine/` (with 3D-specific tests under `tests/Beutl.Graphics3DTests/`).

---

## Phase 1: Setup

- [X] T001 Walk `src/Beutl.Engine/Graphics/FilterEffects/` end-to-end and reconcile the in-scope effect list in `data-model.md`. **Done** — see `data-model.md` § "In-scope built-in effects". 13 effects; 0 effect-source modifications.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: New `RenderScale` value type + reference-frame plumbing + modified `FilterEffectContext` (scaled helpers + `*Raw` twins) + modified `GraphicsContext2D` (scaled helpers + `*Raw` twins) + Pen scaling helpers + Transform `CreateMatrix` scaling + `CompositionContext.RenderScale` plumbing + test infrastructure.

**⚠️ CRITICAL**: No user-story work begins until Phase 2 is complete.

### Block A — Core types and reference-frame plumbing

- [ ] T002 Add `RenderScale` struct in `src/Beutl.Engine/Graphics/Rendering/RenderScale.cs` per `contracts/render-scale.md` (constructor validation; `Identity`; `FromFrames` with uniform-scale enforcement `|sx − sy| ≤ 1e-3 * max(sx, sy)`; `ApplyX / Y / Uniform`; `Apply(Size)`; `Apply(Point)`; `IEquatable<RenderScale>`).
- [ ] T003 Add `ReferenceFrame` to `IRenderer` (default-interface returns `FrameSize`) in `IRenderer.cs`. Add `Renderer(int, int, PixelSize referenceFrame)` overload in `Renderer.cs`.
- [ ] T004 Add `(ReferenceFrame, RenderScale)` stack + `PushReferenceFrame(PixelSize)` to `GraphicsContext2D.cs`, initialized from the constructing `Renderer`'s pair.
- [ ] T005 In `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`, wrap inner `Render` draw in `using (context.PushReferenceFrame(r.ReferencedScene.FrameSize))` (FR-010).
- [ ] T006 In `src/Beutl.Engine/Graphics/FilterEffects/LayerEffect.cs`, push the inner reference frame before activating the sub-pipeline. Document the rule on `GraphicsContext2D.PushReferenceFrame` XML doc.

### Block B — `FilterEffectContext` scaling

- [ ] T007 Snapshot `(ReferenceFrame, RenderScale)` into `FilterEffectContext` at construction. Update `FilterEffectContext(Rect bounds)` and `Clone()` to carry the values.
- [ ] T008 **Modify every length-taking helper on `FilterEffectContext`** (`Blur(Size)`, `DropShadow(Point, Size, Color)`, `DropShadowOnly`, `InnerShadow`, `InnerShadowOnly`, `Erode(float, float)`, `Dilate(float, float)`) to multiply by `this.RenderScale` before forwarding. Signatures unchanged. Add `NaN` / negative-length guards.
- [ ] T009 Add `*Raw` twin for every helper modified in T008 (`BlurRaw`, `DropShadowRaw`, `DropShadowOnlyRaw`, `InnerShadowRaw`, `InnerShadowOnlyRaw`, `ErodeRaw`, `DilateRaw`). XML doc on each references `contracts/effect-helper-scaling.md`.

### Block C — `GraphicsContext2D` direct-helper scaling

- [ ] T010 **Modify every length-taking helper on `GraphicsContext2D`** (`DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushTransform(Matrix)`, `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect, ...)`) to multiply Rect / Matrix-translation by `this.RenderScale` before forwarding. Signatures unchanged. `PushTransform(Transform.Resource)` reads `transform.Matrix` **verbatim** — the Transform already scaled at `CreateMatrix` time per T015–T017 (see `contracts/graphics-context-scaling.md` "no re-scaling" note).
- [ ] T011 Add `*Raw` twin for every helper modified in T010 (`DrawRectangleRaw`, `DrawEllipseRaw`, `PushTransformRaw(Matrix)`, `PushTransformRaw(Transform.Resource)`, `PushClipRaw(Rect)`, `PushLayerRaw(Rect)`, `PushOpacityMaskRaw`). XML doc on each references `contracts/graphics-context-scaling.md`.

### Block D — `Pen` scaling

- [ ] T012 Add `PenHelper.GetScaledThickness(pen, scale)`, `GetScaledDashOffset(pen, scale)`, `GetScaledOffset(pen, scale)`, `GetScaledRealThickness(alignment, pen, scale)` to `src/Beutl.Engine/Graphics/Rendering/PenHelper.cs` per `contracts/pen-scaling.md`. Existing `GetRealThickness` and `GetBounds` stay (used by non-rendering callers).
- [ ] T013 Update consumption sites to use the scaled helpers: `src/Beutl.Engine/Graphics/ImmediateCanvas.cs` (5 reads of `pen.Thickness` in rendering paths), `src/Beutl.Engine/Graphics/Shapes/Shape.cs` (`GetRealThickness` → `GetScaledRealThickness`), `src/Beutl.Engine/Graphics/FilterEffects/StrokeEffect.cs` (thickness read for Skia stroke). Each site uses the `RenderScale` from the surrounding `GraphicsContext2D` / `FilterEffectContext` snapshot.
- [ ] T014 Leave `pen.Thickness` raw at non-rendering call sites (e.g. `PenHelper.GetBounds(rect, pen)`) — bounds calculation lives in project space, not render space. Document the kept-raw call sites in `contracts/pen-scaling.md`.

### Block E — `Transform.CreateMatrix` scaling + `CompositionContext` plumbing

- [ ] T015 Add `RenderScale RenderScale { get; }` to `CompositionContext` (`src/Beutl.Engine/Graphics/Effects/CompositionContext.cs` or wherever the type lives). Source from the active scene's `(FrameSize, ReferenceFrame)` pair. Plumb via `SceneCompositor` / `SceneRenderer` so a top-level scene gets `Identity` (today's behavior) and nested scenes / `LayerEffect` carry their own scale.
- [ ] T016 Modify `TranslateTransform.CreateMatrix(CompositionContext)` to scale `X * context.RenderScale.ScaleX`, `Y * context.RenderScale.ScaleY` before constructing the translation matrix.
- [ ] T017 Modify `Rotation3DTransform.CreateMatrix(CompositionContext)` to scale `CenterX / CenterY / CenterZ / Depth` by the appropriate axis of `context.RenderScale` (rotation angles unchanged).
- [ ] T018 Modify `MatrixTransform.CreateMatrix(CompositionContext)` to scale the translation column (`M31 * ScaleX`, `M32 * ScaleY`) of the materialized matrix (other columns unchanged).

### Block F — Foundational tests (TDD — write before Block B–E implementation)

- [ ] T019 [P] `RenderScaleTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: `Identity`, `FromFrames` happy path, `Apply*`, equality, NaN / zero / negative rejection, `FromFrames` rejects non-uniform per-axis ratios within / outside the 1e-3 tolerance.
- [ ] T020 [P] `ReferenceFramePropagationTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: stack discipline, `FilterEffectContext` / `GraphicsContext2D` snapshot semantics, `SceneDrawable` nested-scene rule.
- [ ] T021 [P] `FilterEffectContextScalingTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`: for each scaled helper — Identity = pre-feature; non-identity = scaled; `*Raw` bypasses; zero passes through; sub-pixel passes through; NaN throws; negative-length on Blur/Erode/Dilate throws (scaled), `*Raw` does not.
- [ ] T022 [P] `GraphicsContext2DScalingTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: for each scaled helper — Identity = pre-feature; non-identity Rect is per-axis scaled (X, Y, Width, Height); Matrix translation column scaled, other columns unchanged; `*Raw` bypasses; `PushTransform(Transform.Resource)` does NOT re-scale (since Transform already scaled at materialization).
- [ ] T023 [P] `PenScalingTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: `PenHelper.GetScaledThickness(pen, scale)` returns `pen.Thickness * scale.ApplyUniform(1)`; same for DashOffset / Offset; `GetScaledRealThickness` combines alignment + scale; raw `pen.Thickness` read is unchanged.
- [ ] T024 [P] `TransformScalingTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Transformation/`: `TranslateTransform.CreateMatrix(ctx)` translation = `(X * ctx.RenderScale.ScaleX, Y * ctx.RenderScale.ScaleY)`; `Rotation3DTransform.CreateMatrix` Center scaled; `MatrixTransform.CreateMatrix` translation column scaled (rotation / scale / skew columns untouched); pure-rotation / pure-scale / pure-skew transforms unchanged.

### Block G — Test infrastructure (shared by Phases 3 / 4 / 5)

- [ ] T025 [P] SSIM helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/SsimHelper.cs`.
- [ ] T026 [P] Bicubic upscale helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/BicubicResampler.cs`.
- [ ] T027 `ResolutionTestHarness` in `tests/Beutl.UnitTests/Engine/Graphics/Testing/ResolutionTestHarness.cs` + fixture loader for `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/`.

**Checkpoint**: Foundation is ready. With `RenderScale.Identity` everywhere today, behavior is byte-identical to before.

---

## Phase 3: User Story 1 - Proxy preview visually matches export (Priority: P1) 🎯 MVP

**Goal**: Demonstrate via SSIM that every in-scope primitive (FilterEffects, GraphicsContext2D direct draws, Pen-stroked shapes, Transforms) produces visually equivalent output at proxy (1/4) and at export resolution. **No effect / shape `.cs` files are modified** — Phase 2 changes inside the helpers do all the work.

**Independent Test**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"`. Every `[TestCase]` renders at full export size and at 1/4 size via `ResolutionTestHarness`, upscales the proxy, and asserts SSIM ≥ 0.97.

- [ ] T028 [US1] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` with parameterized `Effect_ProxyMatchesExport_WithinSSIMTolerance(string fixture)`.
- [ ] T029 [P] [US1] Add FilterEffect fixtures + `[TestCase]` rows for the 13 in-scope effects (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform × 3 subclasses, MosaicEffect, ShakeEffect, SplitEffect, Clipping). Fixture filenames per `data-model.md`.
- [ ] T030 [P] [US1] Add **Shape fixtures** (`rectshape-default.json`, `ellipseshape-default.json`, `roundedrect-default.json`) — each draws a 200×100 shape stroked with a 4 px pen, asserts proxy and export look proportional.
- [ ] T031 [P] [US1] Add **Transform fixtures** — `translate-100-50.json` (TranslateTransform applied to a shape), `rotation3d-default.json`, `matrixtransform-translate.json` — each verifies the translation component scales.
- [ ] T032 [P] [US1] Add **direct-draw fixtures** — `pushclip-rect.json` (drawable that calls PushClip with a Rect and draws inside it), `pushlayer-bounded.json`, `pushtransform-raw-matrix.json` (a custom drawable that directly pushes a Matrix translation).
- [ ] T033 [P] [US1] Add **combined fixture** — `combined-shape-transform-pen-effect.json` covering RectShape + TranslateTransform + stroked Pen + Blur effect in one scene. Demonstrates end-to-end resolution independence.
- [ ] T034 [P] [US1] 3D-with-2D-filter resolution-equivalence test in `tests/Beutl.Graphics3DTests/FilterEffects/Render3DWithFilterResolutionTests.cs`.

**Checkpoint**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` is green across FilterEffects, Shapes, Transforms, direct draws. MVP done.

---

## Phase 4: User Story 2 - Existing projects keep their current appearance (Priority: P1)

**Goal**: Pre-feature project files render byte-identically at export resolution; no on-disk migration.

**Independent Test**: `dotnet test --filter "LegacyRenderingTests|LegacyRoundTripTests"`.

- [ ] T035 [US2] `LegacyCorpusLoader.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Testing/`.
- [ ] T036 [US2] Baseline PNG capture from `main` (pre-feature). Document in `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/README.md`. Include shapes / transforms / pens in the corpus.
- [ ] T037 [P] [US2] Curate the legacy corpus — ≥ 1 project per in-scope primitive (effect, shape, transform, pen-stroke, direct-draw).
- [ ] T038 [US2] `LegacyRenderingTests.cs` asserting SSIM ≥ 0.97 vs baseline at export resolution.
- [ ] T039 [US2] `LegacyRoundTripTests.cs` asserting JSON byte-equality on serialization round-trip — proves no rewrite.

**Checkpoint**: US1 + US2 both deliverable.

---

## Phase 5: User Story 3 - Authoring units predictable across project sizes (Priority: P2)

- [ ] T040 [US3] `CrossResolutionTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`. Render at `scene.FrameSize` and at `2 * scene.FrameSize` (with `referenceFrame = 2 * scene.FrameSize` on the larger). Assert SSIM ≥ 0.97 between upscaled-small and large.
- [ ] T041 [P] [US3] `cross-resolution-multi-primitive.json` combining Blur + DropShadow + RectShape + TranslateTransform + stroked Pen on a single scene.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T042 [P] Plugin-author migration guide `docs/extensibility/resolution-independent-rendering.md` based on the four contract docs. Headline: "do nothing for default behavior; here are the `*Raw` opt-outs and the `PenHelper.GetScaled*` helpers if you need raw-raster".
- [ ] T043 [P] Update `docs/ai-workflow/coding-guidelines-for-ai.md` to mention the helper-internal-scaling contract and the convention for new rendering primitives.
- [ ] T044 [P] Update `.claude/skills/beutl-filter-effect/SKILL.md` and `.claude/skills/beutl-drawable/SKILL.md` to teach the helper contract.
- [ ] T045 [P] Demonstrate the helper contract in scripting samples: extend `tests/PackageSample/` with a `CSharpScriptEffect` using scaled helpers and one using `*Raw`. Satisfies SC-004.
- [ ] T046 Microbenchmark in `tests/Beutl.Benchmarks/Rendering/RenderScaleOverheadBench.cs` measuring per-frame overhead on a scene that exercises every scaled API (RectShape + TranslateTransform + stroked Pen + Blur). Target ≤ 0.5% wall-clock.
- [ ] T047 Run `/beutl-format` (verify mode).
- [ ] T048 Run `/beutl-build` and `/beutl-test` on the full solution.
- [ ] T049 Run `/beutl-coverage Beutl.Engine`; confirm no regression on `Graphics/FilterEffects/FilterEffectContext.cs`, `Graphics/Rendering/GraphicsContext2D.cs`, `Graphics/Rendering/PenHelper.cs`, `Graphics/Transformation/*`.
- [ ] T050 Run `/beutl-pre-pr`.
- [ ] T051 Open PR with `@beutl-design-reviewer` mentioned, focusing the design-priorities audit on the public surface (`RenderScale`, `IRenderer.ReferenceFrame`, `GraphicsContext2D.PushReferenceFrame` + scaled helpers + `*Raw` twins, `FilterEffectContext` scaled helpers + `*Raw` twins, `PenHelper.GetScaled*` helpers, `CompositionContext.RenderScale`, `Transform.CreateMatrix` semantic change).
- [ ] T052 Update `checklists/requirements.md` Notes with summary + forward pointers to deferred follow-ups (`Geometry`, `TextBlock`, `Brush rects`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (T001)**: ✓ DONE.
- **Phase 2 (T002–T027)**: depends on T001.
  - Block A (T002–T006) is the linchpin — everything else in Phase 2 depends on `RenderScale` + `ReferenceFrame` plumbing.
  - Block B (T007–T009 = FilterEffectContext) depends on A.
  - Block C (T010–T011 = GraphicsContext2D) depends on A.
  - Block D (T012–T014 = Pen) depends on A.
  - Block E (T015–T018 = Transform / CompositionContext) depends on A (for `RenderScale`) and on `SceneCompositor` plumbing (T015 itself).
  - Block F tests (T019–T024) — TDD: write before the corresponding implementation block.
  - Block G test infra (T025–T027) — parallel with everything else.
- **Phase 3 (T028–T034)**: depends on Phase 2.
- **Phase 4 (T035–T039)**: depends on Phase 2 + baseline capture from `main` before Phase 2 lands.
- **Phase 5 (T040–T041)**: depends on Phase 3 fixtures.
- **Phase 6 (T042–T052)**: depends on all previous phases.

### Parallel Opportunities

- Blocks B / C / D / E in Phase 2 are largely independent of each other (different files); after Block A lands, all four can be worked in parallel by separate engineers.
- Block F tests (T019–T024) are all in different files — parallel.
- Block G infra (T025–T027) — parallel.
- Phase 3 fixtures (T029–T033) — parallel; only T028 (the harness file) needs to land first.

---

## Implementation Strategy

### MVP (User Story 1)

1. ✓ Phase 1 (T001).
2. Phase 2 Block A (foundational plumbing).
3. Phase 2 Blocks B–E in parallel (FilterEffectContext / GraphicsContext2D / Pen / Transform).
4. Phase 2 Block F (tests) — TDD, written first per block.
5. Phase 2 Block G (test infra).
6. Phase 3 (T028–T034).
7. **STOP, VALIDATE**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` green.
8. Ship as `feat: make rendering helpers resolution-independent` PR.

### Parallel Team Strategy

With 3 engineers:

1. All three drive Phase 2 Block A together (~1 day).
2. Engineer A: Block B (FilterEffectContext, T007–T009, T021).
3. Engineer B: Block C (GraphicsContext2D, T010–T011, T022) + Block D (Pen, T012–T014, T023).
4. Engineer C: Block E (Transform / CompositionContext, T015–T018, T024) + Block G (test infra, T025–T027).
5. Reconvene for Phase 3 fixture authoring (T029–T033 parallel).
6. Engineer A drives Phase 4 baseline capture (T036) — can start as soon as `main` is identified.
7. Phase 5 / 6 short, either engineer.

---

## Notes

- `[P]` tasks live in different files and have no incomplete dependencies on each other.
- **TDD discipline (Constitution III)**: in Phase 2 write test (Block F) before the matching implementation (Blocks B / C / D / E).
- The single biggest change is **Phase 2 Blocks B + C + D + E**. `beutl-design-reviewer` should focus there.
- `Geometry` / `TextBlock.Size` / `Brush rects` are explicitly out of scope (`data-model.md` § "Deferred follow-ups"). Each is a candidate for a separate spec when prioritized.
- Proxy-preview *UX* is out of scope (research R1) — this feature ships the rendering-layer plumbing only.
- Commit per Block or logical group. Conventional Commits per `AGENTS.md`. Umbrella PR title: `feat: make rendering helpers resolution-independent`.
- Stop at the Phase 3 checkpoint to validate the MVP before continuing.
