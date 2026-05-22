---

description: "Task list for Per-Clip Proxy via RenderNodeOperation CorrectionScale"
---

# Tasks: Per-Clip Proxy via RenderNodeOperation CorrectionScale

**Input**: Design documents from `docs/specs/003-resolution-independent-effects/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/render-node-operation-scale.md`, `contracts/source-node-proxy.md`, `contracts/transformer-node-scale-handling.md`, `contracts/compositor-blit.md`, `quickstart.md`

**Tests**: REQUIRED (Constitution III, FR-011). Every implementation block is paired with a test block written first.

**Organization**: Tasks grouped by user story / functional block. US1 + US2 + US3 are all P1 (per-clip proxy mechanism; extension-author non-breakage; backward compatibility).

> **History**: This task list is a full rewrite (2026-05-22). Prior versions (commits `2ad7bb5ae` → `d4728ede9`) were built on the scene-wide-proxy assumption that was abandoned during design review. Effect-helper / GraphicsContext2D / Pen / Transform helper-internal scaling tasks are all gone. See `research.md` § "Historical pivots" for details.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 — none for Setup / Foundational / Polish
- Include exact file paths in descriptions

## Path Conventions

Engine code: `src/Beutl.Engine/Graphics/Rendering/`. Tests: `tests/Beutl.UnitTests/Engine/Graphics/Rendering/` (and `Engine/Graphics/FilterEffects/` for the legacy regression corpus).

---

## Phase 1: Setup / Audit

- [X] T001 **RenderNode classification audit** — walk `src/Beutl.Engine/Graphics/Rendering/` end-to-end and classify every `RenderNode` subclass as: source-producing (Type A media-decoding or Type B sub-canvas-rendering), transformer (filter / transform / container / push-state), or N/A. For each, record the proxy-handling responsibility in `data-model.md` § "Modified types" tables. Use the initial classification from `research.md` § R2 as the starting point; verify against actual code; file follow-ups for any unclassified case.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: `RenderScale` value type + `RenderNodeOperation.CorrectionScale` virtual property + factory overloads + test infra. All other phases depend on this.

**⚠️ CRITICAL**: Phase 3+ blocked until Phase 2 complete.

### Block A — Core types

- [X] T002 Add `RenderScale` struct in `src/Beutl.Engine/Graphics/Rendering/RenderScale.cs` per `contracts/render-node-operation-scale.md` (constructor validation `≥ 1 && finite`; `Identity = (1, 1)`; `FromRatio(float)`; `FromFrames(raster, bounds) = bounds / raster`; `ToRasterX / ToRasterY / ToRasterUniform`; `ToRaster(Size)`; `ToRaster(Point)`; `ToAuthoringX / ToAuthoringY`; `IEquatable<RenderScale>`). The numeric convention is fixed: `CorrectionScale ≥ 1` is the bounds-over-raster upscale ratio.
- [X] T003 Add `virtual CorrectionScale` property to `src/Beutl.Engine/Graphics/Rendering/RenderNodeOperation.cs` (default = `RenderScale.Identity`); update `LambdaRenderNodeOperation` private class to store a `_correctionScale` field and override the virtual.
- [X] T004 Add `CorrectionScale` parameter (default `Identity`) to the four `RenderNodeOperation` factory methods: `CreateLambda`, `CreateFromRenderTarget`, two `CreateFromSurface` overloads. Normalize `default(RenderScale) == (0, 0)` to `Identity` inside the factories so existing callers stay byte-identical without specifying the new parameter.
- [X] T005 Add `CreateDecorator` semantics: inherits `CorrectionScale` from the wrapped child operation. No new parameter; the factory reads `child.CorrectionScale` and constructs the wrapper accordingly.

### Block A tests (TDD — write before Block A implementation)

- [X] T006 [P] `RenderScaleTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: `Identity` is `(1, 1)`; `FromRatio(4.0)` is `(4.0, 4.0)`; `FromFrames(raster: 480×270, bounds: 1920×1080) = (4.0, 4.0)` (the upscale ratio); validation rejects `< 1` (raster larger than bounds), zero, negative, NaN, Infinity; `ToRasterX(20)` with `ScaleX = 4` returns `5` (authoring → raster divide); `ToAuthoringX(5)` with `ScaleX = 4` returns `20` (raster → authoring multiply); equality.
- [X] T007 [P] `RenderNodeOperationCorrectionScaleTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: default virtual returns `Identity`; `CreateLambda(...)` without the new arg → `Identity`; `CreateLambda(..., correctionScale: (4, 4))` → reports `(4, 4)`; `CreateDecorator(child, ...)` inherits child's `CorrectionScale`; `CreateFromRenderTarget` and `CreateFromSurface` overloads honour the arg.

---

## Phase 3: User Story 1 - Per-clip proxy mechanism works end-to-end (Priority: P1) 🎯 MVP

**Goal**: A scene containing one source at `CorrectionScale = 4` and another at `CorrectionScale = 1` renders correctly; the proxy frame matches the export frame (SSIM ≥ 0.97).

**Independent Test**: `dotnet test --filter "ResolutionEquivalenceTests|CompositorBlitTests"`. Mixed-resolution scene fixtures pass.

### Block B — Source-producing nodes

- [ ] T008 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs`: at `Process` time, determine a `CorrectionScale` based on per-clip proxy configuration (out of scope here — read from a placeholder / test-injected source; default `Identity`). Pass `correctionScale` to the operation factory call.
- [ ] T009 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/ImageSourceRenderNode.cs`: same pattern as Video. Static images default to `Identity` (proxy rarely useful) but the mechanism is wired so a test can inject non-Identity.
- [ ] T010 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/DrawableRenderNode.cs` (and / or whatever class produces the operation for a Drawable's render pass, including `SceneDrawable`'s sub-scene path): when rendering into a sub-canvas, allocate the target raster at `bounds.PixelSize × scaleRatio`, construct the inner `ImmediateCanvas` with `SKCanvas.Scale(1 / scaleRatio)` pre-applied, run the inner render pass in authoring space, and declare `CorrectionScale = scaleRatio` on the produced operation.
- [ ] T011 [US1] Add test-only API in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Testing/ProxyTestHarness.cs` that lets tests inject specific `CorrectionScale` values into source nodes without depending on production proxy-config persistence.

### Block C — Transformer nodes

- [X] T012 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs`: in `Process`, read upstream `CorrectionScale`; divide length-typed filter parameters (Sigma, Position, RadiusX/Y, Offset, etc.) by it before invoking Skia; compute output `Bounds` in authoring space using the **authored** (un-divided) parameters; propagate `CorrectionScale` unchanged to the output operation. Reference `research.md` § R3 / R4 for per-effect math.
- [X] T013 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs`: read upstream `CorrectionScale`; apply the matrix in authoring space (bounds transformation); propagate `CorrectionScale` unchanged (transformer does not re-rasterize).
- [X] T014 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/ContainerRenderNode.cs`: aggregate child operations independently. Each child's `CorrectionScale` flows through unchanged.
- [X] T015 [US1] Modify push-state RenderNode subclasses (ClipRenderNode, LayerRenderNode, OpacityMaskRenderNode, any other audit-identified): read upstream `CorrectionScale`; clip / layer bounds stay in authoring space; propagate `CorrectionScale`. The `PushLayer`-saveLayer materialization case is a hybrid (source-like for downstream) — handled per audit.

### Block D — Compositor blit

- [X] T016 [US1] Modify `src/Beutl.Engine/Graphics/Rendering/Renderer.cs` (the top-level compositor): when iterating final operations, if `op.CorrectionScale != Identity`, push a `SKCanvas.Scale(op.CorrectionScale.ScaleX, op.CorrectionScale.ScaleY, op.Bounds.X, op.Bounds.Y)` transform before calling `op.Render(finalCanvas)`, pop after. Identity case bypasses the push (no transform overhead).
- [ ] T017 [US1] Extend `src/Beutl.Engine/Graphics/ImmediateCanvas.cs` (or whatever absorbs the blit-time scale logic): if needed, add a helper `DrawRenderTargetScaled(rt, position, scale)` for the common case. The existing `DrawRenderTarget` / `DrawSurface` paths stay unchanged; the new helper is used by the compositor.

### Block B / C / D tests (TDD)

- [ ] T018 [P] [US1] `SourceNodeCorrectionScaleTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: Type A (Video / Image) with mocked decoder asserts `CorrectionScale` is reported correctly; Type B (Drawable-as-source) sub-canvas rendered at 1/4 produces a 1/4 raster and reports `(4, 4)`; default sources report `Identity`.
- [X] T019 [P] [US1] `TransformerNodeCorrectionScaleTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: for each transformer subclass, given an upstream operation with `CorrectionScale = (4, 4)` and known authored parameters, verify (a) the produced Skia call receives divided parameters, (b) output `Bounds` is computed with authored parameters, (c) propagation preserves CorrectionScale. Sub-pixel / zero / NaN / negative-length guards. Mixed-scale composition (two upstream operations with different CorrectionScale through a Container).
- [X] T020 [P] [US1] `CompositorBlitTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/`: Identity path = no transform pushed; non-Identity path = bilinear upscale produces expected output size; multiple operations with different CorrectionScale blit independently.

### Block E — End-to-end equivalence tests

- [ ] T021 [P] [US1] Add SSIM helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/SsimHelper.cs`.
- [ ] T022 [P] [US1] Add bicubic upscale helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/BicubicResampler.cs`.
- [ ] T023 [US1] Add `ResolutionEquivalenceTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`: parameterized over the 13 in-scope effect fixtures. For each, render the fixture twice — once with proxy enabled on a synthetic source (injected `CorrectionScale = (4, 4)`), once at full — and assert SSIM ≥ 0.97 between proxy-after-compositor-blit and the full render.
- [ ] T024 [P] [US1] Add per-effect fixtures under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/`: 13 effects × 1 fixture each (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform × 3, MosaicEffect, ShakeEffect, SplitEffect, Clipping).
- [ ] T025 [P] [US1] Add Shape / Text / Brush / Geometry fixtures: `rectshape-proxy.json`, `textblock-proxy.json`, `geometry-proxy.json`, `imagebrush-proxy.json`. Confirms automatic participation via Type B source SKCanvas.Scale.

**Checkpoint (US1)**: `dotnet test --filter "ResolutionEquivalenceTests|SourceNodeCorrectionScaleTests|TransformerNodeCorrectionScaleTests|CompositorBlitTests"` is green. Per-clip proxy mechanism is functional end-to-end.

---

## Phase 4: User Story 2 - Extension authors don't change code (Priority: P1)

**Goal**: Recompile the 13 in-scope effects + sample plugins against the new build; all existing tests pass without `.cs` modifications.

**Independent Test**: `dotnet test` (full suite). No `*.cs` file under `src/Beutl.Engine/Graphics/FilterEffects/` (other than `FilterEffectRenderNode.cs`) is modified.

- [ ] T026 [US2] Verify in code review that no `*.cs` file under `src/Beutl.Engine/Graphics/FilterEffects/` (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform, MosaicEffect, ShakeEffect, SplitEffect, Clipping) has been modified by this PR. The only file there that may be modified is the FilterEffect base / Resource / Activator if a small adapter is needed — and even that should be minimal.
- [ ] T027 [US2] Verify no Shape / Drawable / Brush / Pen / Transform / TextBlock files under `src/Beutl.Engine/Graphics/` (outside `Rendering/`) are modified.
- [ ] T028 [US2] Add `ExtensionAuthorNoOpTests.cs` in `tests/Beutl.UnitTests/`: load the 13 in-scope effects + sample CSharpScriptEffect; verify each produces correct output under `CorrectionScale = (4, 4)` via the test harness. No assertion on the effect's `.cs` content other than via git history check ("unchanged from main").

**Checkpoint (US2)**: extension authors have zero code to write to benefit.

---

## Phase 5: User Story 3 - Existing projects unchanged (Priority: P1)

**Goal**: Pre-feature project corpus renders SSIM ≥ 0.97 vs baseline; JSON round-trip byte-equal.

**Independent Test**: `dotnet test --filter "LegacyRenderingTests|LegacyRoundTripTests"`.

- [ ] T029 [US3] Capture baseline PNGs from `main` (pre-feature) for a curated legacy corpus.
- [ ] T030 [P] [US3] Curate the corpus under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/` — ≥ 1 project per in-scope effect, plus Shape / Transform / TextBlock samples.
- [ ] T031 [US3] `LegacyRenderingTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`: walks the corpus, renders each project at export resolution on the new build, asserts SSIM ≥ 0.97 against the captured baseline.
- [ ] T032 [US3] `LegacyRoundTripTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`: round-trips each project's JSON via `Beutl.Serialization`, asserts byte-equal output. Proves no silent rewrite.

**Checkpoint (US3)**: upgrade-safe.

---

## Phase 6: Polish

- [ ] T033 [P] Add RenderNode-author migration guide `docs/extensibility/render-node-correction-scale.md` based on `contracts/render-node-operation-scale.md` + `contracts/source-node-proxy.md` + `contracts/transformer-node-scale-handling.md`. Audience: low-level RenderNode subclass authors (rare).
- [ ] T034 [P] Update `docs/ai-workflow/coding-guidelines-for-ai.md` to mention the per-clip proxy mechanism and the RenderNode source / transformer classification.
- [ ] T035 [P] Update `.claude/skills/beutl-filter-effect/SKILL.md` to clarify: FilterEffect authors do NOT touch CorrectionScale; the mechanism lives one layer below in the corresponding FilterEffectRenderNode.
- [ ] T036 [P] Update `.claude/skills/beutl-drawable/SKILL.md`: Drawable authors do NOT touch CorrectionScale; if authoring a sub-canvas-rendering drawable (rare), see `contracts/source-node-proxy.md` Type B.
- [ ] T037 Add a microbenchmark in `tests/Beutl.Benchmarks/Rendering/CorrectionScaleOverheadBench.cs` measuring per-frame overhead on representative scenes. Target ≤ 0.5% wall-clock vs pre-feature baseline (i.e. the `Identity` fast path is free).
- [ ] T038 Run `/beutl-format` (verify mode).
- [ ] T039 Run `/beutl-build` and `/beutl-test` on the full solution.
- [ ] T040 Run `/beutl-coverage Beutl.Engine`; confirm no regression on `Graphics/Rendering/`.
- [ ] T041 Run `/beutl-pre-pr`.
- [ ] T042 Open PR with `@beutl-design-reviewer` mentioned; focus the audit on the new public surface (`RenderScale`, `RenderNodeOperation.CorrectionScale` virtual + factory overloads, source / transformer / compositor semantic changes).
- [ ] T043 Update `docs/specs/003-resolution-independent-effects/checklists/requirements.md` Notes with one-line summary and forward pointer to the follow-up "per-clip proxy UX + persistence" feature.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (T001 audit)**: ~1 day. Output informs Phases 3/4.
- **Phase 2 (T002–T007 Block A + tests)**: depends on T001. Blocks Phase 3.
- **Phase 3 (T008–T025)**: depends on Phase 2. Block B / C / D are largely independent; Block E (SSIM tests) depends on B/C/D.
- **Phase 4 (T026–T028)**: depends on Phase 3 (verification of no-op for plugin code).
- **Phase 5 (T029–T032)**: depends on Phase 3 + baseline capture from `main` before Phase 3 lands.
- **Phase 6 (T033–T043)**: depends on all previous.

### Parallel Opportunities

- Within Phase 2 Block A: T002 (RenderScale) is the linchpin. After T002 lands, T003/T004/T005 can be parallel.
- Within Phase 3 Block B: T008 / T009 / T010 are in different files — parallel.
- Within Phase 3 Block C: T012 / T013 / T014 / T015 are in different files — parallel (after Block A lands).
- Block D (compositor) requires Block A but not B/C; can run in parallel with B/C.
- Block E (tests) require everything else; mostly parallel within themselves.
- Phase 6 docs (T033–T036) — different files, parallel.

---

## Implementation Strategy

### MVP (US1 only)

1. Phase 1 audit (T001).
2. Phase 2 Block A + tests (T002–T007).
3. Phase 3 Block B + C + D in parallel + their tests (T008–T020).
4. Phase 3 Block E (T021–T025).
5. **STOP and VALIDATE**: `dotnet test --filter "ResolutionEquivalenceTests|..."` is green; eyeball verification per `quickstart.md` shows visual parity.
6. Ship as `feat(engine): per-clip proxy via RenderNodeOperation.CorrectionScale` PR.

### Incremental Delivery

1. Phase 1 + Phase 2 → foundation merges (the new public surface is visible; default behavior unchanged).
2. Phase 3 → MVP ships.
3. Phase 4 → verification of plugin non-breakage.
4. Phase 5 → backward-compat regression proof.
5. Phase 6 → docs, benchmark, design review.

### Parallel Team Strategy

With 2–3 engineers:

1. All drive Phase 1 audit jointly (small task).
2. Engineer A: Block A (T002–T007), then Block B (T008–T010).
3. Engineer B: Block C (T012–T015).
4. Engineer C: Block D (T016–T017) + Block E test infra (T021/T022).
5. Reconvene for SSIM tests (T023–T025) and Phase 4/5 verification.
6. Phase 6 split across team.

---

## FR / SC coverage map

Explicit mapping from each functional requirement and success criterion in `spec.md` to the task(s) that verify it. Reviewers should be able to read down the table and confirm every requirement has at least one verifier.

| Requirement | Verifying tasks | Notes |
|---|---|---|
| FR-001 (`CorrectionScale` carried on operation, ratio = `bounds / raster`) | T002, T003, T006, T007 | Type + factory + virtual + tests. |
| FR-002 (source nodes declare CorrectionScale) | T008, T009, T010, T018 | Per source-node modification + test. |
| FR-003 (transformer nodes consume + propagate) | T012, T013, T014, T015, T019 | Per transformer-subclass + parameter math test. |
| FR-004 (compositor consumes via upscale blit) | T016, T017, T020 | Compositor change + test. |
| FR-005 (existing projects unchanged, Identity default) | T003, T007, T031, T032 | Default virtual + factory normalize + legacy corpus + JSON round-trip. |
| FR-006 (13 in-scope effects participate automatically) | T012, T023, T024 | FilterEffectRenderNode handles per-effect math; SSIM tests cover the 13. |
| FR-007 (Shape / TextBlock / Geometry / Brush automatic) | T010, T025 | Type B SKCanvas.Scale + fixtures for each surface. |
| FR-008 (plugin authors don't change code) | T026, T027, T028 | Code-review checks + ExtensionAuthorNoOpTests. |
| FR-009 (sub-pixel / zero / NaN guards) | T012, T019 | Guards inside FilterEffectRenderNode + tests. |
| FR-010 (nested scenes) | T010, T019, T023, T030 | DrawableRenderNode Type B + transformer chain test + nested-scene fixture in corpus. |
| FR-011 (existing tests pass + new tests cover) | T026 (verify), T031, T032, all Block A–E tests | Full-suite green + corpus. |
| SC-001 (proxy ↔ full SSIM ≥ 0.97 per effect) | T023, T024 | ResolutionEquivalenceTests parameterised across 13 effects. |
| SC-002 (legacy corpus SSIM ≥ 0.97; JSON byte-equal) | T029, T030, T031, T032 | Baseline capture + corpus curation + LegacyRendering / LegacyRoundTrip tests. |
| SC-003 (mixed-resolution scene renders correctly) | T019 (multi-upstream Container test), T020 (compositor mixed input) | Tested at both layers — transformer propagation + compositor blit. |
| SC-004 (extension authors verified, including scripting) | T026, T027, T028 | Code-review + at least one CSharpScriptEffect sample exercised. |
| SC-005 (RenderNode-author migration documented + example) | T033 (migration guide) | New `docs/extensibility/render-node-correction-scale.md`. |

## Notes

- `[P]` tasks live in different files and have no incomplete dependencies.
- **TDD discipline (Constitution III)**: Block A/B/C/D each pair implementation with tests; write the tests first (T006/T007/T018/T019/T020) and confirm they fail before implementing.
- The **biggest single behaviour change** is `RenderNodeOperation.CorrectionScale` propagation. `beutl-design-reviewer` should focus there.
- Per-clip proxy *UX and persistence* are explicitly out of scope (see `spec.md` § Assumptions and § Future work). Wired in a follow-up feature.
- The 13 in-scope effects + Shapes + TextBlock + Geometry + Brushes all benefit **automatically** — no `*.cs` files under `Graphics/FilterEffects/`, `Graphics/Shapes/`, `Graphics/Transformation/`, `Graphics/Brushes/`, etc. are modified by this PR. The change is concentrated in `Graphics/Rendering/`.
- Commit per Block or per logical group. Conventional Commits per `AGENTS.md`. Umbrella PR title: `feat(engine): per-clip proxy via RenderNodeOperation.CorrectionScale`.
- Stop at the Phase 3 checkpoint to validate the MVP before continuing.
