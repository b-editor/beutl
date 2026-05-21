---

description: "Task list for Resolution-Independent Pixel-Absolute Effects"
---

# Tasks: Resolution-Independent Pixel-Absolute Effects

**Input**: Design documents from `docs/specs/003-resolution-independent-effects/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/effect-helper-scaling.md`, `contracts/render-scale.md`, `quickstart.md`

**Tests**: Tests are REQUIRED (Constitution III, FR-011). Every implementation task is paired with a test task that MUST be written first and fail before the implementation lands.

**Organization**: Tasks are grouped by user story. US1 + US2 are both P1; US3 is P2.

> **Design pivot (after T001 audit)**: Earlier drafts of `tasks.md` (committed history `1b7e32fe1` and prior) contained ~62 tasks built around three new wrapper structs (`PixelLength` / `PixelExtent` / `PixelOffset`), 3 animators, 3 property editors, and per-effect property-type migrations across 13 effects. **That entire approach has been replaced.** The current design applies scaling inside the existing `FilterEffectContext` helper methods, with `*Raw` twins as an opt-out — no new wrapper types, no per-effect property migration, no property-editor work. The task list below is renumbered (T001..T036) to reflect the simpler design; see `research.md` § R2 for the rationale.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3) — Setup / Foundational / Polish have no story label
- Include exact file paths in descriptions

## Path Conventions

Engine code under `src/Beutl.Engine/`; project-system under `src/Beutl.ProjectSystem/`; tests under `tests/Beutl.UnitTests/Engine/` (with 3D-specific tests under `tests/Beutl.Graphics3DTests/`).

---

## Phase 1: Setup

- [X] T001 Walk `src/Beutl.Engine/Graphics/FilterEffects/` end-to-end and reconcile the in-scope effect list in `data-model.md` against the actual code. **Done** — see `data-model.md` § "In-scope built-in effects (no source migration; helper contract suffices)". Net: 13 effects benefit automatically; 0 source modifications under FilterEffects/; under the new design no property-editor work is required.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new `RenderScale` value type + reference-frame plumbing + the modified `FilterEffectContext` (scaled helpers + `*Raw` twins) + test infrastructure. Every Phase 3+ task depends on these.

**⚠️ CRITICAL**: No user-story work begins until Phase 2 is complete.

### Core types and plumbing

- [ ] T002 Add `RenderScale` struct in `src/Beutl.Engine/Graphics/Rendering/RenderScale.cs` per `contracts/render-scale.md` (constructor validation — `ScaleX > 0`, `ScaleY > 0`, both finite; `Identity`; `FromFrames(renderTarget, referenceFrame)` **with uniform-scale enforcement (throw `ArgumentException` when `|sx − sy| > 1e-3 * max(sx, sy)`)**; `ApplyX / Y / Uniform`; `Apply(Size)`; `Apply(Point)`; `IEquatable<RenderScale>`).
- [ ] T003 Add `ReferenceFrame` to `IRenderer` (with a default-interface implementation returning `FrameSize`) in `src/Beutl.Engine/Graphics/Rendering/IRenderer.cs`. Add `Renderer(int width, int height, PixelSize referenceFrame)` overload in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`; keep existing constructor with `ReferenceFrame == FrameSize`.
- [ ] T004 Add `(ReferenceFrame, RenderScale)` stack + `PushReferenceFrame(PixelSize)` to `src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`, initialized from the constructing `Renderer`'s pair. Expose `ReferenceFrame` and `RenderScale` as read-only properties returning the top of the stack.
- [ ] T005 Snapshot `(ReferenceFrame, RenderScale)` into `FilterEffectContext` at construction in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs`; expose as `RenderScale` / `ReferenceFrame` read-only properties. Update `FilterEffectContext(Rect bounds)` and `Clone()` to carry the values forward (snapshot is immutable for the lifetime of the context).
- [ ] T006 **Modify every length-taking helper on `FilterEffectContext`** (`src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs`) so each multiplies its length-typed argument by `this.RenderScale` before forwarding to the existing Skia / `EffectActivator` path. In-scope helpers (per `contracts/effect-helper-scaling.md`): `Blur(Size)`, `DropShadow(Point, Size, Color)`, `DropShadowOnly(Point, Size, Color)`, `InnerShadow(Point, Size, Color)`, `InnerShadowOnly(Point, Size, Color)`, `Erode(float, float)`, `Dilate(float, float)`. **Signatures do not change** — call sites in `Blur.cs` / `DropShadow.cs` / `InnerShadow.cs` / `Erode.cs` / `Dilate.cs` etc. stay byte-identical. Each helper also guards against `NaN` (`ArgumentException`) and against negative lengths where negative is nonsensical (`ArgumentOutOfRangeException`).
- [ ] T007 In the same file, **add a `*Raw` twin** for every helper modified in T006: `BlurRaw(Size)`, `DropShadowRaw(Point, Size, Color)`, `DropShadowOnlyRaw(Point, Size, Color)`, `InnerShadowRaw(Point, Size, Color)`, `InnerShadowOnlyRaw(Point, Size, Color)`, `ErodeRaw(float, float)`, `DilateRaw(float, float)`. `*Raw` variants forward verbatim without multiplying by `RenderScale`. They share the same `NaN` guard but skip the negative-length guard (Raw is opt-out and means "I know what I'm doing"). XML doc on every `*Raw` member explains the opt-out semantic and references `contracts/effect-helper-scaling.md`.
- [ ] T008 In `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`, wrap the inner draw of `Render` in `using (context.PushReferenceFrame(r.ReferencedScene.FrameSize))` so nested scenes resolve against their own frame (FR-010).
- [ ] T009 In `src/Beutl.Engine/Graphics/FilterEffects/LayerEffect.cs` (and any other container that materializes a sub-raster), push the inner reference frame before activating the sub-pipeline. Document the rule in inline XML doc on `GraphicsContext2D.PushReferenceFrame`.

### Foundational tests (TDD — write before the implementation in T002–T009)

- [ ] T010 [P] Tests for `RenderScale` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderScaleTests.cs`: `Identity`, `FromFrames` happy path, `Apply*` per axis, equality, NaN / zero / negative rejection in constructor, and **`FromFrames` rejects non-uniform per-axis ratios** (e.g. `FromFrames(480×270, 1920×1081)` throws `ArgumentException`) while accepting off-by-one rounding within the 1e-3 tolerance.
- [ ] T011 [P] Reference-frame propagation tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ReferenceFramePropagationTests.cs`: `PushReferenceFrame` stack discipline (push/pop nesting), `FilterEffectContext` snapshot does not observe later pushes, `SceneDrawable` nested-scene rule (FR-010) by constructing a sub-scene of a different size and asserting an effect inside sees the inner frame.
- [ ] T012 [P] **`FilterEffectContext` scaling tests** in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/FilterEffectContextScalingTests.cs`. For each scaled helper (`Blur`, `DropShadow(Only)`, `InnerShadow(Only)`, `Erode`, `Dilate`):
  - With `RenderScale.Identity`, the helper produces the same Skia filter as the pre-feature implementation (bit-identical SHA of the filter blob, or — pragmatically — same rendered output on a small fixture).
  - With a 0.25 / 4.0 non-identity scale, the helper produces a Skia filter whose effective length argument equals `input * scale`.
  - The `*Raw` twin always produces the pre-feature filter regardless of `RenderScale` (proves the bypass).
  - Zero passes through exactly (`0 * scale == 0`).
  - Sub-pixel positive (`0 < scaled < 1`) is forwarded, not clamped to zero.
  - `NaN` input throws `ArgumentException`.
  - Negative length on scaled `Blur` / `Erode` / `Dilate` throws `ArgumentOutOfRangeException`; the corresponding `*Raw` variant does NOT (Raw is opt-out).
  - Negative length on `DropShadow.Position` (offset, not length) passes through for both scaled and raw paths.

### Test infrastructure (shared by Phases 3 / 4 / 5)

- [ ] T013 [P] Add SSIM helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/SsimHelper.cs` (`float Compute(SKBitmap a, SKBitmap b)`). Self-contained reference implementation, no model dependency.
- [ ] T014 [P] Add bicubic upscale helper in `tests/Beutl.UnitTests/Engine/Graphics/Testing/BicubicResampler.cs` (`SKBitmap UpscaleTo(SKBitmap src, SKSizeI target)`).
- [ ] T015 Add `ResolutionTestHarness` in `tests/Beutl.UnitTests/Engine/Graphics/Testing/ResolutionTestHarness.cs` exposing `Render(Scene scene, PixelSize renderSize)` that constructs a `Renderer` with the given size and `referenceFrame = scene.FrameSize`. Include a fixture loader for JSON scene files under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/`.

**Checkpoint**: Foundation is ready. The new scaling is functional but unused (today every renderer is `RenderScale.Identity`, so behavior is byte-identical to before).

---

## Phase 3: User Story 1 - Proxy preview visually matches export (Priority: P1) 🎯 MVP

**Goal**: Demonstrate via SSIM that every in-scope built-in effect produces visually equivalent output at proxy (1/4) and at export resolution — **without modifying any effect's `.cs` file**. The scaling work landed in Phase 2 (T006); this phase only adds test fixtures + a parameterized harness to prove it.

**Independent Test**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"`. Every `[TestCase]` (one per in-scope effect — 13 effects per the T001 audit; the 3 `DisplacementMapTransform` subclasses count separately, so 15 fixtures total) renders at full export size and at 1/4 size via `ResolutionTestHarness`, upscales the proxy with `BicubicResampler`, and asserts SSIM ≥ 0.97.

- [ ] T016 [US1] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` with a parameterized test method `Effect_ProxyMatchesExport_WithinSSIMTolerance(string fixture)` per the pattern in `quickstart.md` § 2.
- [ ] T017 [P] [US1] Add fixture JSON for each in-scope built-in effect under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/` and add a `[TestCase(...)]` to T016 for each:
  - `blur-soft.json` (Blur, Sigma ≈ 20 px)
  - `dropshadow-default.json` (DropShadow, Position ≈ (10,10), Sigma ≈ (15,15))
  - `innershadow-default.json` (InnerShadow, mirror of dropshadow)
  - `stroke-offset.json` (StrokeEffect, Offset ≈ (4,4); thickness is raw-pixel — known partial-coverage caveat per research R6)
  - `erode-default.json` (Erode, R ≈ 3)
  - `dilate-default.json` (Dilate, R ≈ 3)
  - `flatshadow-default.json` (FlatShadow, Length ≈ 12, Angle ≈ 45)
  - `colorshift-default.json` (ColorShift, channel offsets ≈ (3, 0))
  - `displacementmap-translate-default.json`, `displacementmap-scale-default.json`, `displacementmap-rotation-default.json`
  - `mosaic-default.json` (MosaicEffect, TileSize ≈ (10,10))
  - `shake-default.json` (ShakeEffect, StrengthX/Y ≈ 5)
  - `split-default.json` (SplitEffect, spacing ≈ 4)
  - `clipping-default.json` (Clipping, edges ≈ 8)
- [ ] T018 [P] [US1] 3D-with-2D-filter resolution-equivalence test in `tests/Beutl.Graphics3DTests/FilterEffects/Render3DWithFilterResolutionTests.cs`: render a minimal 3D scene whose output has `Blur` and `DropShadow` applied, at full export size and at 1/4 size; upscale and assert SSIM ≥ 0.97. Confirms the 2D filter path correctly reads the outer `GraphicsContext2D`'s `RenderScale` when its source is a 3D framebuffer.

**Checkpoint**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` is green for all 13 in-scope effects. The MVP (US1) is done — a proxy-preview workflow (not built here) would now produce correct visuals.

---

## Phase 4: User Story 2 - Existing projects keep their current appearance (Priority: P1)

**Goal**: Prove that pre-feature project files render at export resolution byte-identically to the previous build, and that no project-file migration step is introduced.

**Independent Test**: `dotnet test --filter "LegacyRenderingTests|LegacyRoundTripTests"`. All cases must pass.

- [ ] T019 [US2] Add legacy fixture loader in `tests/Beutl.UnitTests/Engine/Graphics/Testing/LegacyCorpusLoader.cs` that walks `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/` and yields `(projectFile, baselinePng)` pairs.
- [ ] T020 [US2] Capture baseline PNGs by checking out `main` (pre-feature), rendering each curated legacy project at export resolution via a one-shot helper, and committing the resulting PNGs alongside their project files. Document the capture procedure in `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/README.md`.
- [ ] T021 [P] [US2] Curate the legacy corpus (≥ 1 project per in-scope effect, ideally identical to the Phase 3 fixtures so a single project can serve both purposes) under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`.
- [ ] T022 [US2] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRenderingTests.cs` that uses `LegacyCorpusLoader` to assert `SsimHelper.Compute(newRender, baseline) >= 0.97f` at the saved export resolution for every fixture.
- [ ] T023 [US2] Add `LegacyRoundTripTests.cs` in `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/` that loads a legacy fixture, serializes it back via `Beutl.Serialization`, and asserts the resulting JSON is byte-equal to the original (modulo insignificant whitespace) — proves there is no silent rewrite and confirms the unchanged on-disk shape promised by FR-003.

**Checkpoint**: `dotnet test --filter "LegacyRenderingTests|LegacyRoundTripTests"` is green. US1 and US2 are both deliverable.

> No separate `LegacyDeserializationTests` is needed under the new design: since no property type is renamed, the existing `Beutl.Serialization` round-trips are sufficient. T023 is the proof.

---

## Phase 5: User Story 3 - Authoring units predictable across project sizes (Priority: P2)

**Goal**: Show that changing a project's export resolution (e.g. 1920×1080 → 3840×2160) preserves the relative visual strength of every effect without re-tuning parameters.

**Independent Test**: `dotnet test --filter CrossResolutionTests`.

- [ ] T024 [US3] Create `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/CrossResolutionTests.cs`. Reuse `ResolutionTestHarness` and `SsimHelper`. Parameterize over the same fixtures US1 uses; for each, render at `scene.FrameSize` and at `2 * scene.FrameSize` (with `referenceFrame = 2 * scene.FrameSize` on the larger one to mimic "user changed the export size"). Assert SSIM ≥ 0.97 between the upscaled small render and the larger render.
- [ ] T025 [P] [US3] Add a focused fixture `cross-resolution-multi-effect.json` combining Blur, DropShadow, and StrokeEffect on a single shape, demonstrating proportional scaling end-to-end. Add as a `[TestCase]` in `CrossResolutionTests.cs`.

**Checkpoint**: All three user stories are independently validated.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T026 [P] Add plugin-author migration guide `docs/extensibility/resolution-independent-effects.md` based on `contracts/effect-helper-scaling.md` § "Plugin author migration". Headline: "do nothing; here is `*Raw` if you need raw-raster behavior". Link from `docs/extensibility/README.md` if one exists; otherwise create one.
- [ ] T027 [P] Update `docs/ai-workflow/coding-guidelines-for-ai.md` to mention: "new built-in or plugin effects that take length-typed arguments via `FilterEffectContext.Blur` / `DropShadow` / `Erode` / `Dilate` etc. automatically receive resolution-independent scaling. Use the `*Raw` twin only when you specifically need raw-raster pixel semantics."
- [ ] T028 [P] Update `.claude/skills/beutl-filter-effect/SKILL.md` to teach the helper contract so new-effect scaffolds describe the scaling behavior in their generated XML doc.
- [ ] T029 [P] Demonstrate the helper contract in scripting samples: extend `tests/PackageSample/` with at least one `CSharpScriptEffect` that calls scaled helpers (and one that calls `*Raw` for the opt-out demonstration). For `GLSLScriptEffect`, file a follow-up note (shader-uniform binding is a separate change). Satisfies SC-004.
- [ ] T030 Add a microbenchmark in `tests/Beutl.Benchmarks/FilterEffects/RenderScaleOverheadBench.cs` measuring per-frame overhead of the new scale path on a Blur + DropShadow scene; assert (manually, in PR review) ≤ 0.5% wall-clock vs. raw-overload baseline. Document the baseline number in the PR description.
- [ ] T031 Run `/beutl-format` (verify mode) and address any formatter findings (no stylistic-only edits per Constitution V).
- [ ] T032 Run `/beutl-build` and `/beutl-test` on the full solution; address any failures.
- [ ] T033 Run `/beutl-coverage Beutl.Engine` and confirm coverage on `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs` and `src/Beutl.Engine/Graphics/Rendering/` does not regress versus the pre-feature baseline.
- [ ] T034 Run `/beutl-pre-pr` to surface anything `claude-code-review.yml` would flag locally; fix or document each finding.
- [ ] T035 Open the PR with `@beutl-design-reviewer` mentioned in the body so the design-priorities audit (orthogonality, library-user flexibility, no compat shims) runs against the new public surface (`RenderScale`, `IRenderer.ReferenceFrame`, `GraphicsContext2D.PushReferenceFrame`, `FilterEffectContext` snapshot props, and the `*Raw` helper twins).
- [ ] T036 Update `docs/specs/003-resolution-independent-effects/checklists/requirements.md` Notes section with a one-line summary of the shipped behavior and forward pointers to the `Pen.Thickness` follow-up (research R6), the `Beutl.Graphics.Transformation.*` follow-up (audit), and the proxy-preview UX follow-up (research R1).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup, T001)**: ✓ DONE.
- **Phase 2 (Foundational, T002–T015)**: depends on T001. Blocks every user story.
- **Phase 3 (US1, T016–T018)**: depends on Phase 2. T017 and T018 can run in parallel; T016 (harness) is light-weight and lands first.
- **Phase 4 (US2, T019–T023)**: depends on Phase 2. T020 (baseline capture) requires the pre-feature `main` build; capture it before Phase 2 lands on `main`.
- **Phase 5 (US3, T024–T025)**: depends on Phase 3 fixtures.
- **Phase 6 (Polish, T026–T036)**: depends on all previous phases.

### Within Phase 2

- T002 (`RenderScale`) is the linchpin — everything else in Phase 2 depends on it.
- T003 → T004 → T005 chain: each builds on the previous.
- T006 + T007 share the same file (`FilterEffectContext.cs`) and must land together; T006 is the semantic change to existing helpers, T007 is the additive `*Raw` twins.
- T008 / T009 (push) depend on T004.
- T010 / T011 / T012 (tests) follow their respective sources but should be **written first** per TDD; ensure failing baselines before implementing T002 / T004–T005 / T006–T007 respectively.
- T013 / T014 / T015 (test infra) can land any time in Phase 2 but T015 must be ready before Phase 3 begins.

### Parallel Opportunities

- T010, T011, T012, T013, T014 — five test/infra files (different files); can all run in parallel once their dependencies are unblocked.
- T017, T018 — fixture authoring + 3D test (different files); parallel.
- T021 — corpus curation runs alongside infrastructure (T019, T020).
- T026, T027, T028, T029 — four polish/docs/sample tasks (different files); parallel.

---

## Parallel Example: Phase 2 tests-first TDD

```bash
# Write failing tests first:
Task: "RenderScaleTests in tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderScaleTests.cs"                                # T010
Task: "ReferenceFramePropagationTests in tests/Beutl.UnitTests/Engine/Graphics/Rendering/ReferenceFramePropagationTests.cs"   # T011
Task: "FilterEffectContextScalingTests in tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/FilterEffectContextScalingTests.cs"  # T012

# Confirm red.

# Then implement:
Task: "Add RenderScale struct"                                                # T002
Task: "Add IRenderer.ReferenceFrame + Renderer overload"                      # T003
Task: "Add GraphicsContext2D.PushReferenceFrame stack"                        # T004
Task: "Snapshot RenderScale into FilterEffectContext"                         # T005
Task: "Modify FilterEffectContext scaled helpers"                             # T006
Task: "Add *Raw twin helpers on FilterEffectContext"                          # T007
```

## Parallel Example: Phase 3 fixture authoring

```bash
# After T016 (harness file) is in place:
Task: "Add Phase 3 fixtures + [TestCase] rows for all 13 effects"             # T017
Task: "3D-with-2D resolution-equivalence test"                                # T018
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. ✓ Phase 1 (T001 audit).
2. Phase 2: T002–T015 (foundational types + plumbing + modified `FilterEffectContext` + tests + test infra).
3. Phase 3: T016–T018 (`ResolutionEquivalenceTests` harness + fixtures + 3D coverage).
4. **STOP and VALIDATE**: `dotnet test --filter "ResolutionEquivalenceTests|Render3DWithFilterResolutionTests"` is green; eyeball verification per `quickstart.md` § 5 confirms parity.
5. Ship as a `feat: make filter-effect helpers resolution-independent` PR. (Note: the title intentionally says "filter-effect helpers" rather than "filter effects" — the change is in the helpers, not in the effects.)

### Incremental Delivery (recommended)

1. Phase 1 + Phase 2 → foundation merge-ready (new types and plumbing are publicly visible; `RenderScale.Identity` everywhere means no observable behavior change).
2. Phase 3 → MVP ships; future proxy-preview UX would now produce correct visuals.
3. Phase 4 → backward-compat proof: `LegacyRenderingTests` go green; safe to declare backward-compatible.
4. Phase 5 → cross-resolution authoring story.
5. Phase 6 → docs, plugin samples, benchmark, design-review.

### Parallel Team Strategy

With two engineers:

1. Engineer A drives Phase 2 source (T002–T009).
2. Engineer B drives Phase 2 tests + infra (T010–T015) in parallel — and importantly writes T010–T012 first to enforce TDD.
3. Once Phase 2 lands, both fan out across Phase 3 (T016–T018) and Phase 4 infrastructure (T019–T021) concurrently.
4. Engineer A captures baselines (T020) from `main` while Engineer B closes out Phase 3.
5. Phase 5 / Phase 6 are short and can be done by either engineer.

---

## Notes

- `[P]` tasks live in different files and have no incomplete dependencies on each other.
- **TDD discipline (Constitution III)**: for every (implementation, test) pair in Phase 2, write the test first, confirm it fails, then implement.
- The single biggest change in this PR is **T006 + T007** — the semantic change to `FilterEffectContext` helpers plus the additive `*Raw` twins. `beutl-design-reviewer` should focus its attention there.
- `Pen.Thickness` resolution-independence is **out of scope** (research R6) — `StrokeEffect.Offset` benefits automatically via the scaled helper but thickness stays raw-pixel until a follow-up.
- Proxy-preview *UX* is **out of scope** (research R1) — this feature ships the plumbing only.
- Commit after each task or per logical group (foundational struct, foundational plumbing, FilterEffectContext modifications, test infra, regression corpus, polish). Use Conventional Commits per `AGENTS.md` (`feat:`, `test:`, `docs:`, etc.); the umbrella PR title is `feat: make filter-effect helpers resolution-independent`.
- Stop at the checkpoint after Phase 3 to validate the MVP independently before continuing.
