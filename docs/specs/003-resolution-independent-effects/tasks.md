---

description: "Task list for Resolution-Independent Pixel-Absolute Effects"
---

# Tasks: Resolution-Independent Pixel-Absolute Effects

**Input**: Design documents from `docs/specs/003-resolution-independent-effects/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/parameter-wrappers.md`, `contracts/render-scale.md`, `quickstart.md`

**Tests**: Tests are REQUIRED. The constitution mandates Test-First with NUnit (Principle III) and FR-011 requires automated tests covering proxy↔export visual-equivalence and the legacy-file equivalence criterion. Test tasks are included throughout. **Within each (implementation, test) pair, write the test first, confirm it fails, then implement.**

**Organization**: Tasks are grouped by user story so each story is independently implementable and testable. US1 + US2 are both P1 (US2 is backward-compatibility for the same mechanism); US3 is P2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

This feature touches existing single-solution layout. Engine code lives under `src/Beutl.Engine/`, tests under `tests/Beutl.UnitTests/Engine/` (e.g. `tests/Beutl.UnitTests/Engine/Graphics/`); 3D-specific tests under `tests/Beutl.Graphics3DTests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the audit list is complete before writing code. No new project / dependency is added.

- [X] T001 Walk `src/Beutl.Engine/Graphics/FilterEffects/` end-to-end and reconcile the in-scope effect list in `docs/specs/003-resolution-independent-effects/data-model.md` against the actual code. **Done — see `data-model.md` § "In-scope built-in effect migrations" (audit corrections tagged in the `Source` column) and § "Property-editor registration (audit finding — NEW)".** Net: 15 → 13 effects (dropped `PartsSplitEffect` and `TransformEffect`); 7 row corrections; 1 new foundational obligation (3 entries in `src/Beutl/Services/PropertyEditorService.cs` + 3 new ViewModel files) that must land in Phase 2 before per-effect migration begins.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new types + scale-propagation plumbing + test infrastructure. Every per-effect migration depends on these.

**⚠️ CRITICAL**: No user-story work begins until Phase 2 is complete.

### Core types and plumbing

- [ ] T002 Add `RenderScale` struct in `src/Beutl.Engine/Graphics/Rendering/RenderScale.cs` per `contracts/render-scale.md` (constructor validation, `Identity`, `FromFrames` **with uniform-scale enforcement (throw `ArgumentException` when `|sx − sy| > 1e-3 * max(sx, sy)`)**, `ApplyX/Y/Uniform`, `Apply(Size)`, `Apply(Point)`, `IEquatable<RenderScale>`).
- [ ] T003 [P] Add `PixelLength` struct in `src/Beutl.Engine/Graphics/PixelLength.cs` per `contracts/parameter-wrappers.md` (constructor, `ReferencePixels`, `Zero`, implicit cast from `float`, `ResolveX/Y/Uniform`, `IEquatable`, `IFormattable`, NaN rejection at construction).
- [ ] T004 [P] Add `PixelExtent` struct in `src/Beutl.Engine/Graphics/PixelExtent.cs` (constructors from `float, float` and `Size`, `Width`, `Height`, `ToSize`, `Empty`, `Resolve(RenderScale)`, `IEquatable`, `IFormattable`, NaN rejection at construction).
- [ ] T005 [P] Add `PixelOffset` struct in `src/Beutl.Engine/Graphics/PixelOffset.cs` (constructors from `float, float` and `Point`, `X`, `Y`, `ToPoint`, `Zero`, `Resolve(RenderScale)`, `IEquatable`, `IFormattable`, NaN rejection at construction).
- [ ] T006 Register animators in `src/Beutl.Engine/Animation/AnimatorRegistry.cs`: `Animator<PixelLength>`, `Animator<PixelExtent>`, `Animator<PixelOffset>` (linear interpolation on the underlying components). Add the three animator classes alongside (e.g. `PixelLengthAnimator.cs`, `PixelExtentAnimator.cs`, `PixelOffsetAnimator.cs` in `src/Beutl.Engine/Animation/`).
- [ ] T007 Add `ReferenceFrame` to `IRenderer` (with a default-interface implementation returning `FrameSize`) in `src/Beutl.Engine/Graphics/Rendering/IRenderer.cs`. Add `Renderer(int width, int height, PixelSize referenceFrame)` overload in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`; keep existing constructor with `ReferenceFrame == FrameSize`.
- [ ] T008 Add `(ReferenceFrame, RenderScale)` stack + `PushReferenceFrame(PixelSize)` to `src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`, initialized from the constructing `Renderer`'s pair.
- [ ] T009 Snapshot `(ReferenceFrame, RenderScale)` into `FilterEffectContext` at construction in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs`; expose as `RenderScale` / `ReferenceFrame` read-only properties. Update `FilterEffectContext(Rect bounds)` and `Clone()` to carry the values.
- [ ] T010 Add wrapper-aware overloads on `FilterEffectContext` (`src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs`) that resolve and forward to the existing raw overloads: `Blur(PixelExtent)`, `DropShadow(PixelOffset, PixelExtent, Color)`, `DropShadowOnly(PixelOffset, PixelExtent, Color)`, `InnerShadow(PixelOffset, PixelExtent, Color)`, `InnerShadowOnly(PixelOffset, PixelExtent, Color)`, `Erode(PixelLength, PixelLength)`, `Dilate(PixelLength, PixelLength)`. Existing raw overloads stay unchanged.
- [ ] T011 In `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`, wrap the inner draw of `Render` in `using (context.PushReferenceFrame(r.ReferencedScene.FrameSize))` so nested scenes resolve against their own frame (FR-010).
- [ ] T012 In `src/Beutl.Engine/Graphics/FilterEffects/LayerEffect.cs` (and any other container that materializes a sub-raster), push the inner reference frame before activating the sub-pipeline. Document the rule in inline XML doc on `GraphicsContext2D.PushReferenceFrame`.

### Foundational tests

- [ ] T013 [P] Tests for `RenderScale` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderScaleTests.cs`: validation, `Identity`, `FromFrames`, `Apply*` per axis, equality, NaN/zero rejection, **`FromFrames` rejects non-uniform per-axis ratios (e.g. `FromFrames(480×270, 1920×1081)` throws `ArgumentException`) and accepts off-by-one rounding within the 1e-3 tolerance.**
- [ ] T014 [P] Tests for `PixelLength` in `tests/Beutl.UnitTests/Engine/Graphics/PixelLengthTests.cs`: construction, implicit `float` cast, `Resolve*` round-trip with `RenderScale.Identity` and a non-identity scale, equality, serialization round-trip via `Beutl.Serialization`.
- [ ] T015 [P] Tests for `PixelExtent` in `tests/Beutl.UnitTests/Engine/Graphics/PixelExtentTests.cs`: construction from `(float,float)` and `Size`, `Resolve(RenderScale)` anisotropic correctness, `Empty`, equality, JSON shape equivalence with the previous `Size`-serialized form.
- [ ] T016 [P] Tests for `PixelOffset` in `tests/Beutl.UnitTests/Engine/Graphics/PixelOffsetTests.cs`: same shape as T015 but for `Point`-equivalent JSON, plus zero-preservation through `Resolve`.
- [ ] T017 [P] Animator tests in `tests/Beutl.UnitTests/Engine/Animation/PixelWrapperAnimatorTests.cs`: keyframe lerp for each of the three wrappers; assert animated values equal the underlying primitive's lerp.
- [ ] T018 Reference-frame propagation tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ReferenceFramePropagationTests.cs`: `PushReferenceFrame` stack discipline (push/pop nesting), `FilterEffectContext` snapshot does not observe later pushes, `SceneDrawable` nested-scene rule (FR-010) by constructing a sub-scene of a different size and asserting an effect inside sees the inner frame.

### Edge-case foundational tests (FR-009)

- [ ] T019 [P] Sub-pixel / zero / NaN behavior tests in `tests/Beutl.UnitTests/Engine/Graphics/SubPixelAndZeroTests.cs`:
  - `RenderScale.Apply*` and each `Pixel*.Resolve` preserve `0` exactly across `RenderScale.Identity` and a 0.25 / 4.0 non-identity scale.
  - `0 < resolved < 1` passes through without being clamped to 0 (sub-pixel values reach the rasterizer).
  - `PixelLength(NaN)` / `PixelExtent(NaN, …)` / `PixelOffset(NaN, …)` are rejected at construction (`ArgumentException`).
  - Negative values are rejected by the validators on properties where negativity is nonsensical (sigma, radius, length) — exercise via `Property.CreateAnimatable<PixelLength>(..., validator)` use sites listed in `data-model.md` § Validation summary.

### Test infrastructure shared by US1 / US2 / US3

- [ ] T020 [P] Add an SSIM helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/SsimHelper.cs` (`float Compute(SKBitmap a, SKBitmap b)`). Use a self-contained reference implementation (no model dependency).
- [ ] T021 [P] Add a bicubic upscale helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/BicubicResampler.cs` (`SKBitmap UpscaleTo(SKBitmap src, SKSizeI target)`).
- [ ] T022 Add a `ResolutionTestHarness` in `tests/Beutl.UnitTests/Engine/Graphics/Testing/ResolutionTestHarness.cs` exposing `Render(Scene, PixelSize renderSize)` that constructs a `Renderer` with the given size and `referenceFrame = scene.FrameSize`. Include a fixture loader for JSON scene files under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/`.

**Checkpoint**: Foundation is ready — all per-effect work in Phase 3+ can now proceed in parallel.

---

## Phase 3: User Story 1 - Proxy preview visually matches export (Priority: P1) 🎯 MVP

**Goal**: With Phase 2 in place, migrate every in-scope built-in effect so its pixel-absolute parameters are typed as `PixelLength` / `PixelExtent` / `PixelOffset`, and prove via SSIM that proxy (1/4) and export renderings agree (≥ 0.97 after bicubic upscale).

**Independent Test**: Run `ResolutionEquivalenceTests`. Every `[TestCase]` (one per migrated effect) renders the same fixture at full export size and at 1/4 size, upscales the latter with bicubic, and asserts SSIM ≥ 0.97 against the export. All cases must pass.

### Per-effect migrations (each: source change + matching test [TestCase] + fixture JSON)

- [ ] T023 [P] [US1] Migrate `Blur.Sigma` to `IProperty<PixelExtent>` in `src/Beutl.Engine/Graphics/FilterEffects/Blur.cs`; call `context.Blur(r.Sigma)` (overload resolution picks the wrapper-aware version). Add fixture `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/blur-soft.json` and a `[TestCase("blur-soft.json")]` to `ResolutionEquivalenceTests.cs` (see T039).
- [ ] T024 [P] [US1] Migrate `DropShadow` in `src/Beutl.Engine/Graphics/FilterEffects/DropShadow.cs`: `Position: IProperty<PixelOffset>`, `Sigma: IProperty<PixelExtent>`. Add fixture `dropshadow-default.json` + `[TestCase]`.
- [ ] T025 [P] [US1] Migrate `InnerShadow` in `src/Beutl.Engine/Graphics/FilterEffects/InnerShadow.cs`: same shape as T024. Add fixture `innershadow-default.json` + `[TestCase]`.
- [ ] T026 [P] [US1] Migrate `StrokeEffect` in `src/Beutl.Engine/Graphics/FilterEffects/StrokeEffect.cs`: `Offset: IProperty<PixelOffset>`. **`Pen.Thickness` stays raw-pixel for now (research R6); leave a `// TODO(resolution-independent-pen):` comment referencing this feature spec.** Add fixture `stroke-default.json` + `[TestCase]` (assertion: offset proportion matches; thickness mismatch is a known gap until the follow-up).
- [ ] T027 [P] [US1] Migrate `Erode.RadiusX / RadiusY` to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/Erode.cs`. Add fixture `erode-default.json` + `[TestCase]`.
- [ ] T028 [P] [US1] Migrate `Dilate.RadiusX / RadiusY` to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/Dilate.cs`. Add fixture `dilate-default.json` + `[TestCase]`.
- [ ] T029 [P] [US1] Migrate `FlatShadow.Length` to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/FlatShadow.cs` (keep `Angle` as raw `float`, dimensionless). Add fixture `flatshadow-default.json` + `[TestCase]`.
- [ ] T030 [P] [US1] Migrate `ColorShift` per-channel offsets to `PixelOffset` (or `PixelLength` per channel if axis-symmetric) in `src/Beutl.Engine/Graphics/FilterEffects/ColorShift.cs`. Add fixture `colorshift-default.json` + `[TestCase]`.
- [ ] T031 [P] [US1] Migrate `DisplacementMapTransform.X / Y / CenterX / CenterY` to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/DisplacementMapTransform.cs` (keep `Scale / ScaleX / ScaleY` as raw `float`, dimensionless percentages). Add fixture `displacementmap-default.json` + `[TestCase]`.
- [ ] T032 [P] [US1] Migrate `MosaicEffect` tile size to `PixelLength` (or `PixelExtent` if anisotropic) in `src/Beutl.Engine/Graphics/FilterEffects/MosaicEffect.cs`. Add fixture `mosaic-default.json` + `[TestCase]`.
- [ ] T033 [P] [US1] Migrate `ShakeEffect` amplitude to `PixelLength` in `src/Beutl.Engine/Graphics/FilterEffects/ShakeEffect.cs`. Add fixture `shake-default.json` + `[TestCase]`.
- [ ] T034 [P] [US1] Migrate `SplitEffect.HorizontalSpacing / VerticalSpacing` to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/SplitEffect.cs`. Add fixture `split-default.json` + `[TestCase]`.
- [ ] T035 [P] [US1] Migrate `PartsSplitEffect` spacing to `IProperty<PixelLength>` in `src/Beutl.Engine/Graphics/FilterEffects/PartsSplitEffect.cs`. Add fixture `partssplit-default.json` + `[TestCase]`.
- [ ] T036 [P] [US1] Migrate `Clipping` pixel `Rect` to `PixelOffset + PixelExtent` (or introduce a dedicated `PixelRect` if T001 found it warranted) in `src/Beutl.Engine/Graphics/FilterEffects/Clipping.cs`. Add fixture `clipping-default.json` + `[TestCase]`.
- [ ] T037 [P] [US1] Migrate `TransformEffect` translation component to `PixelOffset` while keeping rotation/scale dimensionless in `src/Beutl.Engine/Graphics/FilterEffects/TransformEffect.cs`. Add fixture `transform-translate.json` + `[TestCase]`.
- [ ] T038 [US1] **Audit follow-up** — for each effect added in T001 beyond this list, append a parallel-able task to the same pattern (source change + fixture + `[TestCase]`). Use the next free T-number after T038a (i.e. T038b, T038c, …) so the existing numbering above stays stable.
- [ ] T038a [P] [US1] 3D-with-2D-filter resolution-equivalence test in `tests/Beutl.Graphics3DTests/FilterEffects/Render3DWithFilterResolutionTests.cs`: render a minimal 3D scene whose output has a `Blur` and a `DropShadow` applied, at full export size and at 1/4 size; upscale and assert SSIM ≥ 0.97. Confirms the 2D filter path correctly reads the outer `GraphicsContext2D`'s `RenderScale` when its source is a 3D framebuffer.

### US1 test harness

- [ ] T039 [US1] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` with a parameterized test method that takes a fixture filename, renders at export size and at 1/4 size via `ResolutionTestHarness`, upscales the proxy with `BicubicResampler`, and asserts `SsimHelper.Compute(...) >= 0.97f`. Tasks T023–T037 add `[TestCase("…json")]` rows to this file.

**Checkpoint**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` is green. The MVP (US1) is done — a proxy-preview workflow (not built here) would now produce correct visuals.

---

## Phase 4: User Story 2 - Existing projects keep their current appearance (Priority: P1)

**Goal**: Prove that pre-feature project files, when opened on the new build, render at export resolution identically (SSIM ≥ 0.97) to the previous build. No on-disk migration step is introduced — values pass through `Resolve(RenderScale.Identity)` unchanged.

**Independent Test**: Run `LegacyRenderingTests`. The harness loads every project under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`, renders it at the saved export resolution on the new build, and asserts SSIM ≥ 0.97 against the captured baseline PNG. All cases must pass.

### Legacy regression infrastructure

- [ ] T040 [US2] Add legacy fixture loader in `tests/Beutl.UnitTests/Engine/Graphics/Testing/LegacyCorpusLoader.cs` that walks `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/` and yields `(projectFile, baselinePng)` pairs.
- [ ] T041 [US2] Capture baseline PNGs by checking out `main` (pre-feature), rendering each curated legacy project at export resolution via a one-shot helper, and committing the resulting PNGs alongside their project files under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`. Document the capture procedure in `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/README.md` so future intentional visual changes can re-baseline.
- [ ] T042 [P] [US2] Curate the legacy corpus (≥ 1 project per migrated effect from US1) under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`. Each project's JSON must contain `Size` / `Point` / `float` parameters in their old form, matching the property names this feature renames.

### Legacy regression tests

- [ ] T043 [US2] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRenderingTests.cs` that uses `LegacyCorpusLoader` to drive a parameterized test asserting `SsimHelper.Compute(newRender, baseline) >= 0.97f` at the saved export resolution.
- [ ] T044 [P] [US2] Add `LegacyDeserializationTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/` that deserializes one fixture per migrated effect (saved in pre-feature shape: `Size`/`Point`/`float` values) and asserts (a) the property now hydrates the new wrapper type and (b) `ReferencePixels` / `Width` / `Height` / `X` / `Y` equal the saved values bit-for-bit — proving FR-003's "no project-file migration" guarantee.
- [ ] T045 [US2] Add `LegacyRoundTripTests.cs` that loads a legacy fixture, serializes it back via `Beutl.Serialization`, and asserts the resulting JSON is byte-equal to the original (modulo whitespace) — confirms no silent rewrite.

**Checkpoint**: `dotnet test --filter "LegacyRenderingTests|LegacyDeserializationTests|LegacyRoundTripTests"` is green. US1 and US2 are both deliverable.

---

## Phase 5: User Story 3 - Authoring units predictable across project sizes (Priority: P2)

**Goal**: Show that changing a project's export resolution (e.g. 1920×1080 → 3840×2160) preserves the relative visual strength of every effect without re-tuning parameters.

**Independent Test**: Run `CrossResolutionTests`. The harness loads a project, renders it at the original export size and at a 2× scaled export size, upscales the original to match, and asserts SSIM ≥ 0.97. All cases must pass.

- [ ] T046 [US3] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/CrossResolutionTests.cs`. Reuse `ResolutionTestHarness` and `SsimHelper`. Parameterize over the same fixtures US1 uses; for each, render at `scene.FrameSize` and at `2 * scene.FrameSize` (with `referenceFrame = 2 * scene.FrameSize` on the larger one to mimic "user changed the export size"). Assert SSIM ≥ 0.97 between the upscaled small render and the larger render.
- [ ] T047 [P] [US3] Add a focused fixture `cross-resolution-multi-effect.json` combining Blur, DropShadow, and StrokeEffect on a single shape, demonstrating proportional scaling end-to-end. Add as a `[TestCase]` in `CrossResolutionTests.cs`.

**Checkpoint**: All three user stories are independently validated.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, plugin-author migration, performance check, and the pre-PR sweep.

- [ ] T048 [P] Add plugin-author migration guide `docs/extensibility/resolution-independent-effects.md` based on `contracts/parameter-wrappers.md` § "Migration discipline for plugin authors". Link from `docs/extensibility/README.md` if one exists; otherwise create one.
- [ ] T049 [P] Update `docs/ai-workflow/coding-guidelines-for-ai.md` to mention: "new built-in or plugin effects with pixel-valued parameters MUST use `PixelLength` / `PixelExtent` / `PixelOffset` from `Beutl.Graphics`. Raw `Size` / `Point` / `float` for length is now a documented anti-pattern." Add a one-line entry under the appropriate section.
- [ ] T050 [P] Update `.claude/skills/beutl-filter-effect/SKILL.md` to teach the wrapper convention so new-effect scaffolds use it by default.
- [ ] T051 [P] Demonstrate the plugin contract in scripting samples: extend `tests/PackageSample/` (or the closest scripting sample) with at least one `CSharpScriptEffect` using `PixelLength` and one `GLSLScriptEffect` whose uniform is bound through the scale (satisfies SC-004).
- [ ] T052 Add a microbenchmark in `tests/Beutl.Benchmarks/FilterEffects/RenderScaleOverheadBench.cs` measuring per-frame overhead of the new scale path on a Blur + DropShadow + StrokeEffect scene; assert (manually, in PR review) ≤ 0.5% wall-clock vs. raw-overload baseline. Document the baseline number in the PR description.
- [ ] T053 Run `/beutl-format` (verify mode) and address any formatter findings (no stylistic-only edits per Constitution V).
- [ ] T054 Run `/beutl-build` and `/beutl-test` on the full solution; address any failures.
- [ ] T055 Run `/beutl-coverage Beutl.Engine` and confirm coverage on `src/Beutl.Engine/Graphics/FilterEffects/` and `src/Beutl.Engine/Graphics/Rendering/` does not regress versus the pre-feature baseline.
- [ ] T056 Run `/beutl-pre-pr` to surface anything `claude-code-review.yml` would flag locally; fix or document each finding.
- [ ] T057 Open the PR with `@beutl-design-reviewer` mentioned in the body so the design-priorities audit (orthogonality, library-user flexibility, no compat shims) runs against the new public surface (`PixelLength`, `PixelExtent`, `PixelOffset`, `RenderScale`, `IRenderer.ReferenceFrame`, `GraphicsContext2D.PushReferenceFrame`, `FilterEffectContext` snapshot props).
- [ ] T058 Update `docs/specs/003-resolution-independent-effects/checklists/requirements.md` Notes section with a one-line summary of the shipped behavior and a forward pointer to the `Pen.Thickness` follow-up (research R6) and the proxy-preview UX follow-up (research R1).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup, T001)**: starts immediately.
- **Phase 2 (Foundational, T002–T022)**: depends on T001. Blocks every user story.
- **Phase 3 (US1, T023–T039 + T038a)**: depends on Phase 2. All per-effect tasks T023–T037 can run in parallel; T038 sweeps any audit additions; T038a (3D-with-2D) is independent of the others; T039 is the harness file that the per-effect tasks add `[TestCase]` rows to (light dependency: harness file must exist when first `[TestCase]` lands — write T039 early in the parallel batch, then add rows).
- **Phase 4 (US2, T040–T045)**: depends on Phase 2 (it needs the new types + serialization). Can run concurrently with Phase 3 once T002–T010 are done — T041 needs a baseline captured from `main` so plan that capture before merging Phase 3.
- **Phase 5 (US3, T046–T047)**: depends on Phase 3 fixtures (reuses them).
- **Phase 6 (Polish, T048–T058)**: depends on all previous phases.

### Within Phase 2

- T002 (`RenderScale`) is the only true linchpin — T003/T004/T005 can run in parallel after T002.
- T006 (animators) depends on T003/T004/T005.
- T007 (`IRenderer.ReferenceFrame`) depends on T002 (uses `RenderScale.FromFrames`).
- T008 (`GraphicsContext2D.PushReferenceFrame`) depends on T002 + T007.
- T009 (`FilterEffectContext` snapshot) depends on T008.
- T010 (wrapper-aware overloads) depends on T003–T005 + T009.
- T011 / T012 (nested-scene push) depend on T008.
- T013–T018 tests follow their respective sources; T019 (sub-pixel/zero/NaN) depends on T002–T005.
- T020 / T021 / T022 (test infra) can run in parallel with everything else in Phase 2 as long as T009 / T022 (`FilterEffectContext` snapshot, harness) is ready before per-effect tests in Phase 3.

### Within Each User Story

- US1: each effect migration (T023–T037) is independent of the others; only T039 (test harness file) needs to land first. T038a (3D-with-2D) is independent of T023–T037 and can run in parallel.
- US2: T040 + T042 are infrastructure; T041 (baseline capture) gates T043; T044 / T045 are independent of T043.
- US3: T046 + T047 are mostly independent (T047 adds a `[TestCase]` to T046's file).

### Parallel Opportunities

- T003, T004, T005 — three wrapper structs (different files).
- T013, T014, T015, T016, T017, T019 — six test files (different files).
- T020, T021 — SSIM + bicubic helpers (different files).
- T023 – T037 — fifteen effect migrations, all different files.
- T038a — 3D-with-2D test, independent of every Phase 3 task.
- T042 — corpus curation can run alongside infrastructure (T040) and harness (T043).
- T048, T049, T050, T051 — four polish/docs/sample tasks (different files).

---

## Parallel Example: Phase 2 wrapper structs and their tests

```bash
# After T002 lands, fan out:
Task: "Add PixelLength struct in src/Beutl.Engine/Graphics/PixelLength.cs"           # T003
Task: "Add PixelExtent struct in src/Beutl.Engine/Graphics/PixelExtent.cs"           # T004
Task: "Add PixelOffset struct in src/Beutl.Engine/Graphics/PixelOffset.cs"           # T005

# And in parallel, write the test files:
Task: "PixelLengthTests in tests/Beutl.UnitTests/Engine/Graphics/PixelLengthTests.cs"   # T014
Task: "PixelExtentTests in tests/Beutl.UnitTests/Engine/Graphics/PixelExtentTests.cs"   # T015
Task: "PixelOffsetTests in tests/Beutl.UnitTests/Engine/Graphics/PixelOffsetTests.cs"   # T016
Task: "Sub-pixel/zero/NaN tests in tests/Beutl.UnitTests/Engine/Graphics/SubPixelAndZeroTests.cs"  # T019
```

## Parallel Example: Phase 3 effect migrations + 3D coverage

```bash
# After Phase 2 + T039 (harness) are in place:
Task: "Migrate Blur.Sigma to PixelExtent"                 # T023
Task: "Migrate DropShadow to PixelOffset/PixelExtent"     # T024
Task: "Migrate InnerShadow to PixelOffset/PixelExtent"    # T025
Task: "Migrate StrokeEffect.Offset to PixelOffset"        # T026
Task: "Migrate Erode radius to PixelLength"               # T027
Task: "Migrate Dilate radius to PixelLength"              # T028
# … T029–T037 in parallel …
Task: "3D-with-2D resolution-equivalence test"            # T038a
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: T001 (audit).
2. Complete Phase 2: T002–T022 (foundational types + plumbing + edge-case tests + test infra).
3. Complete Phase 3: T023–T039 + T038a (effect migrations + ResolutionEquivalenceTests + 3D coverage).
4. **STOP and VALIDATE**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` is green; eyeball verification per `quickstart.md` § 5 confirms parity.
5. Ship as a `feat: make pixel-absolute filter effects resolution-independent` PR. The mechanism is correct even though no proxy-preview UX consumes it yet — this is intentional (see `research.md` § R1).

### Incremental Delivery (recommended)

1. Phase 1 + Phase 2 → foundation merge-ready (the new types and plumbing are publicly visible but no built-in effect uses them yet; benign).
2. Phase 3 → MVP ships with all built-in effects migrated.
3. Phase 4 → backward-compat proof: `LegacyRenderingTests` go green; safe to declare backward-compatible.
4. Phase 5 → cross-resolution authoring story.
5. Phase 6 → docs, plugin samples, benchmark, design-review.

### Parallel Team Strategy

With two engineers:

1. Engineer A drives Phase 2 source (T002, T006, T007, T008, T009, T010, T011, T012).
2. Engineer B drives Phase 2 tests + infra (T013–T022) in parallel with A.
3. Once Phase 2 lands, both fan out across T023–T037 + T038a (Phase 3) and US2 infrastructure (T040–T042) concurrently.
4. Engineer A captures baselines (T041) from `main` while Engineer B closes out effect migrations.
5. Phase 5 / Phase 6 are short and can be done by either engineer.

---

## Notes

- `[P]` tasks live in different files and have no incomplete dependencies on each other.
- Every test task names the file it adds, so editors can land them concurrently.
- **TDD discipline**: for every (implementation, test) pair, write the test first, confirm it fails, then implement — per Constitution III.
- `Pen.Thickness` resolution-independence is **out of scope** for this feature (research R6) — T026 leaves a `TODO` referencing this spec and the partial-coverage gap is documented in the PR description.
- Proxy-preview *UX* is **out of scope** (research R1) — this feature ships the plumbing only.
- Commit after each task or per logical group (foundational types, foundational plumbing, per-effect, regression infra, polish). Use Conventional Commits per `AGENTS.md` (`feat:`, `test:`, `docs:`, etc.); the umbrella PR title is `feat: make pixel-absolute filter effects resolution-independent`.
- Stop at the checkpoint after Phase 3 to validate the MVP independently before continuing.
- Avoid cross-task file conflicts: when adding `[TestCase]` rows to `ResolutionEquivalenceTests.cs`, rebase frequently to minimize merge churn.
