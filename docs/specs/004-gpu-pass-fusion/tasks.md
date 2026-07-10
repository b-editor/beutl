# Tasks: Declarative Effect Graph with GPU Pass Fusion

**Input**: Design documents from `docs/specs/004-gpu-pass-fusion/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included — the spec mandates them (FR-017/FR-019, constitution III, contracts/observability.md O2/O4 are CI-enforced gates).

**Organization**: Grouped by user story. **Story phase order follows the binding rollout order (FR-020 / research D10), not priority order**: US5 (counters, step 1) → US3 (pool, step 2) → US1 (graph + fusion, steps 3–4a) → US2 (caches, step 4b) → US4 (full migration + authoring, step 5) → removal/polish (step 6). Each story phase still ends independently testable; each rollout step ships as a PR-sized increment with build + full tests green.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 from spec.md
- Paths are repo-relative; new engine code per plan.md's structure decision

---

## Phase 1: Setup

**Purpose**: Directories and shared fixtures every story uses

- [x] T001 Create shared effect-pipeline scene fixtures (ColorChain, MixedChain, SplitTree, HeavySource builders with fixed seeds, 1080p/4K variants) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/SceneFixtures.cs
- [x] T002 [P] Create docs/specs/004-gpu-pass-fusion/notes/baseline.md scaffold (baseline table placeholders, SC-005 re-pin section per research D11)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Frozen pre-redesign references — every story's parity gate compares against these

**⚠️ CRITICAL**: generate these from the *unmodified* pipeline before any behavior change lands

- [x] T003 Add reference-freezing support to the golden harness (render + store per-effect and per-chain reference images with existing harness conventions) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenImageHarness.cs
- [x] T004 Generate and commit frozen pre-redesign reference renders for every effect in the research §0 census (42 incl. NodeGraphFilterEffect; meta/delegating effects via representative compositions) and the O3 chains at output scale 1.0 under tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/References/004-baseline/ (contracts/observability.md O4)

**Checkpoint**: references frozen — story phases may begin

---

## Phase 3: User Story 5 — Pipeline efficiency is measurable (Priority: P3, rollout step 1) 🚦 FIRST BY DESIGN

**Goal**: Counters + benchmarks on the *legacy* pipeline; measured baseline recorded before any behavior change

**Independent Test**: benchmark suite runs on the pre-redesign pipeline and reports the four counters + frame time per scene; counter values match the legacy cost model (≥ 1 materialization bake + ≥ 1 pass per custom item — adjacent custom items each pay the flush bake *plus* their own pass; research §0)

- [x] T005 [US5] Implement PipelineDiagnostics (GpuPasses, TargetAllocations, PoolAcquires, PoolMisses, FullFrameMaterializations, FlushSyncs, PlanCompilations, ProgramCreations; Snapshot()/Reset(); plain long increments) in src/Beutl.Engine/Graphics/Rendering/PipelineDiagnostics.cs
- [x] T006 [US5] Expose per-renderer diagnostics (renderer/processor owns an instance; test-visible accessor) in src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs and the renderer types that own it
- [x] T007 [US5] Wire counters into the legacy pipeline per contracts/observability.md O1: Flush→FullFrameMaterializations+GpuPasses in src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs; effect-path RenderTarget.Create→TargetAllocations and sync pairs→FlushSyncs in src/Beutl.Engine/Graphics/Rendering/RenderTarget.cs; CustomFilterEffectContext.CreateTarget→TargetAllocations in src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs
- [x] T008 [US5] Add baseline counter tests on ColorChain/MixedChain fixtures asserting the legacy cost model with materializations and effect passes counted separately (≥ 1 FullFrameMaterialization per custom item incl. the adjacent-custom double cost; GpuPasses covers both bake draws and effect passes; negligible-overhead smoke) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs
- [x] T009 [P] [US5] Add BenchmarkDotNet suite (four O3 scenes, counter snapshot + median frame time report, excluded from dotnet test) in tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs
- [x] T010 [US5] Run the benchmark suite on the legacy pipeline, record baseline numbers, and re-pin SC-005 targets in docs/specs/004-gpu-pass-fusion/notes/baseline.md (update spec SC-005 if headroom differs materially — research D11)

**Checkpoint**: baseline measured — SC-005 has real numbers; PR for rollout step 1

---

## Phase 4: User Story 3 — Intermediate memory bounded, targets pooled (Priority: P2, rollout step 2)

**Goal**: Render-target pool behind unchanged behavior; steady-state allocations drop to zero

**Independent Test**: golden suite unchanged (behavior-identical); frames 2..K of a static-structure scene show zero new target allocations

- [x] T011 [US3] Implement RenderTargetPool (exact-size (w,h,format) buckets for RGBA16F + Depth32Float, Acquire/Release/Trim, N≈8-frame idle eviction + byte soft-cap LRU, cleared-on-acquire, render-thread affinity, normalized preview-drop/export-throw failure semantics, PoolAcquires/PoolMisses counters) with lease ownership inside RenderTarget: a pool-aware last-release deallocator replacing SKSurfaceCounter's dispose-on-zero, plus a generation tag so no stale shallow copy can observe a reissued target, in src/Beutl.Engine/Graphics/Rendering/RenderTargetPool.cs and src/Beutl.Engine/Graphics/Rendering/RenderTarget.cs
- [x] T012 [US3] Add pool unit tests (hit/miss, eviction by idle frames and byte cap, clear-on-acquire, leak assertion, generation-tag reissue safety under live shallow copies, normalized FR-015 failure semantics under forced allocation failure) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/RenderTargetPoolTests.cs
- [x] T013 [US3] Route legacy intermediate allocation through the pool with lease ownership: FilterEffectActivator.Flush and CustomFilterEffectContext.CreateTarget acquire from the pool, and return-to-pool hooks into RenderTarget's final ref-count release (SKSurfaceCounter) — never an individual EffectTarget.Dispose while shallow copies live — in src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs, src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs, and src/Beutl.Engine/Graphics/Rendering/RenderTarget.cs
- [x] T014 [US3] Add steady-state counter test (static-structure scene, frames 2..K ⇒ TargetAllocations delta == 0) and verify the full golden suite is unchanged, in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs

**Checkpoint**: pool live, behavior identical — PR for rollout step 2

---

## Phase 5: User Story 1 — Color chains execute as one GPU pass (Priority: P1, rollout steps 3–4a) 🎯 MVP CORE

**Goal**: Declarative graph model + compiler + executor; the 15 coordinate-invariant color-effect classes (plus color-matrix builder conveniences) migrate; fusion produces one draw per invariant run with parity

**Independent Test**: N ≥ 3 invariant color chain ⇒ GpuPasses == 1, intermediates ≤ 1, golden parity vs frozen references; mixed chain counts strictly below baseline

### Graph model (public authoring surface)

- [x] T015 [P] [US1] Implement BoundsContract (TransformBounds/GetRequiredInputBounds/IsRenderTimeResolved, identity-by-construction for invariant nodes) in src/Beutl.Engine/Graphics/FilterEffects/Nodes/BoundsContract.cs
- [x] T016 [P] [US1] Implement ShaderNodeDescriptor (snippet `half4 apply(half4 c)` + whole-source forms, IsCoordinateInvariant, uniform/sampler/child bindings) and ColorFilterNodeDescriptor in src/Beutl.Engine/Graphics/FilterEffects/Nodes/ShaderNodeDescriptor.cs and src/Beutl.Engine/Graphics/FilterEffects/Nodes/ColorFilterNodeDescriptor.cs
- [x] T017 [P] [US1] Implement SkiaFilterNodeDescriptor (SKImageFilter factory + bounds functions) in src/Beutl.Engine/Graphics/FilterEffects/Nodes/SkiaFilterNodeDescriptor.cs
- [x] T018 [US1] Implement EffectGraphBuilder (append primitives, bounds threading, convenience methods matching today's vocabulary, describe-time validation, Build()) and EffectGraph DAG in src/Beutl.Engine/Graphics/FilterEffects/EffectGraphBuilder.cs and src/Beutl.Engine/Graphics/Rendering/EffectGraph/EffectGraph.cs
- [x] T019 [US1] Add virtual FilterEffect.Describe(EffectGraphBuilder, Resource) with a transition bridge that wraps an unmigrated effect's legacy ApplyTo item list as a single OpaqueLegacyPass **executed via the retained legacy activator machinery (internal-only)** — so custom/geometry-style effects run correctly before GeometryPass/ComputePass exist (T037/T040); bridge + retained machinery are in-feature scaffolding, deleted in Phase 8, not a shipped shim — in src/Beutl.Engine/Graphics/FilterEffects/FilterEffect.cs

### Compiler + executor (internal)

- [x] T020 [US1] Implement StructuralKey hashing (kinds/topology/source identity/structural ints; uniform values excluded) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/StructuralKey.cs
- [x] T021 [US1] Implement the graph compiler and per-frame resource resolution: fusion grouping (contracts/execution-plan.md C1, budget split at 16 stages), Skia filter grouping (C2), ResourcePlan structural shape (format + FirstUse/LastUse intervals), and the per-frame resolution pass — backward ROI with render-time fallback, concrete sizes from current bounds with the monotonic working-scale carry (C3.2), runtime empty-ROI skip — in src/Beutl.Engine/Graphics/Rendering/EffectGraph/EffectGraphCompiler.cs
- [x] T022 [US1] Implement CompiledPlan/CompiledPass records (FusedShaderPass composition recipe, SkiaFilterPass, CompositePass, Backend + SyncBefore flags) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/CompiledPlan.cs
- [x] T023 [US1] Implement PlanExecutor for Skia passes: fused shader composition execution (image shader → WithColorFilter wraps → SKRuntimeEffect child nesting, one draw per fused pass; premultiplied linear-light representation preserved between stages), SkiaFilterPass draws, OpaqueLegacyPass execution via the retained activator (T019 bridge), pool acquire/release by resource plan, sync only on SyncBefore, counter emission per C8, in src/Beutl.Engine/Graphics/Rendering/EffectGraph/PlanExecutor.cs
- [x] T024 [US1] Implement SKSL snippet merging codegen (fe{N}_ uniform prefixing, chained apply main; order-preserving) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/SkslSnippetMerger.cs
- [x] T025 [US1] Rewire FilterEffectRenderNode.Process to resolve working scale exactly as today (FR-012), then describe → compile → execute via the new path, with the T019 bridge covering unmigrated effects, in src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs

### Color-effect migration (the fusable 16)

- [x] T026 [P] [US1] Migrate color-matrix/color-filter effects to ColorFilterNode: Saturate, HueRotate, Brightness, HighContrast, Lighting, LumaColor (BlendEffect is brush-based and migrates in T043) in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)
- [x] T027 [P] [US1] Migrate SKSL snippet effects: Gamma, Invert, Threshold, Negaposi, ChromaKey, ColorKey in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)
- [x] T028 [P] [US1] Migrate sampler-bearing snippet effects: ColorGrading, Curves, LutEffect (LUT textures as sampler bindings, contents non-structural) in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)

### US1 gates

- [x] T029 [US1] Add compiler unit tests (fusion grouping maximality, group split at budget, ROI backward propagation incl. render-time fallback and runtime empty-ROI skip, resource-plan peak-live intervals, per-frame size re-resolution under parameter-driven bounds without recompiling, working-scale carry parity incl. a 16384px-clamp-triggering chain) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectGraphCompilerTests.cs
- [x] T030 [US1] Add SC-001 counter tests (N ≥ 3 invariant chain ⇒ GpuPasses == 1, intermediates ≤ 1; MixedChain strictly below recorded baseline; FlushSyncs == backend transitions) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs
- [x] T031 [US1] Add migration parity tests for the 15 color-effect classes (T026–T028) + ColorChain/MixedChain vs frozen references (SSIM ≥ 0.99 / MAE ≤ 0.02), including semitransparent-content variants exercising the premultiplied-alpha contract, in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectMigrationParityTests.cs
- [x] T032 [US1] Run beutl-source-generator-impact over the FilterEffect signature change and confirm tests/SourceGeneratorTest/ stays green (constitution VI gate; fix fallout if any)

**Checkpoint**: fused color chains at parity — PR(s) for rollout steps 3–4a

---

## Phase 6: User Story 2 — Animated parameters don't rebuild the pipeline (Priority: P1, rollout step 4b)

**Goal**: Plan cache on structural key; program cache; per-frame work = uniform writes

**Independent Test**: 100 structurally constant animated frames ⇒ PlanCompilations == 1, ProgramCreations == 0 after frame 1; structural edit ⇒ exactly one recompile

- [x] T033 [US2] Implement ParameterBlock + ParameterSlot binding (uniform/color-filter/sampler values written on cache hit without recompiling) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/ParameterBlock.cs
- [x] T034 [US2] Implement single-entry PlanCache on FilterEffectRenderNode with the exhaustive invalidation rules of contracts/execution-plan.md C5 (structural key + context identity only; bounds/sizes/working scale flow through per-frame resource resolution, never invalidation; release pooled resources on invalidate/dispose) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/PlanCache.cs and src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs
- [x] T035 [US2] Implement per-context ProgramCache (SKRuntimeEffect by source hash, merged-source cache, LRU above cap) in src/Beutl.Engine/Graphics/Rendering/EffectGraph/ProgramCache.cs
- [x] T036 [US2] Add SC-002 counter tests (100 animated frames ⇒ 1 compilation / 0 program creations after frame 1 — including a bounds-animating case such as blur sigma; insert/remove effect ⇒ exactly one recompile; parameter-extreme output equals fresh-compile output) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs

**Checkpoint**: steady-state frames are uniform-writes only — PR completing rollout step 4

---

## Phase 7: User Story 4 — Full migration + declarative authoring surface (Priority: P2, rollout step 5)

**Goal**: Every remaining built-in, script, and node-graph effect describes itself; compute/geometry/split/composite primitives complete; plugin authoring validated

**Independent Test**: one representative effect per node kind authored against public API only renders correctly, reports correct ROI, and (invariant shader) fuses between built-ins; the full 42-effect census (research §0) passes parity

### Remaining primitives

- [x] T037 [P] [US4] Implement GeometryNodeDescriptor + GeometrySession + EffectInput (executor-bracketed canvas, read-only inputs, scales; no target creation/flush) in src/Beutl.Engine/Graphics/FilterEffects/Nodes/GeometryNodeDescriptor.cs, src/Beutl.Engine/Graphics/FilterEffects/Nodes/GeometrySession.cs, src/Beutl.Engine/Graphics/FilterEffects/Nodes/EffectInput.cs
- [x] T038 [P] [US4] Implement ComputeNodeDescriptor (GLSL pass set, structural pass count, ping-pong/depth declaration, push-constant writer, declared no-Vulkan fallback) in src/Beutl.Engine/Graphics/FilterEffects/Nodes/ComputeNodeDescriptor.cs
- [x] T039 [P] [US4] Implement SplitNodeDescriptor/CompositeNodeDescriptor and compiler/executor branch scheduling (fusion never crosses; per-branch resource intervals; branch/division counts structural per C3.6; dynamic-outputs pass support per C3.5 for execution-time-resolved counts) in src/Beutl.Engine/Graphics/FilterEffects/Nodes/SplitCompositeDescriptors.cs and src/Beutl.Engine/Graphics/Rendering/EffectGraph/EffectGraphCompiler.cs
- [x] T040 [US4] Extend PlanExecutor with ComputePass (Vulkan pipeline via GLSLFilterPipeline, pooled ping-pong + depth textures, C4 sync at backend transitions) and GeometryPass session lifecycle in src/Beutl.Engine/Graphics/Rendering/EffectGraph/PlanExecutor.cs

### Effect migration (per research D7 map)

- [x] T041 [P] [US4] Migrate SkiaFilterNode effects: Blur, DropShadow(Only), Dilate, Erode, InnerShadow(Only), TransformEffect(matrix path) in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files); the MatrixConvolution/Transform context conveniences become EffectGraphBuilder conveniences (covered by T018)
- [x] T042 [P] [US4] Migrate non-invariant whole-source shader effects: MosaicEffect, ColorShift, DisplacementMapTransform in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)
- [x] T043 [P] [US4] Migrate GeometryNode effects: FlatShadow, StrokeEffect, Clipping, LayerEffect, DelayAnimationEffect, ShakeEffect, PathFollowEffect, DisplacementMapEffect, TransformEffect(custom path), BlendEffect (brush-based via BrushConstructor; lower to ColorFilterNode when the brush is structurally a solid color) in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)
- [x] T044 [P] [US4] Migrate split/composite effects: SplitEffect (division counts structural — animating them recompiles per change) and PartsSplitEffect (dynamic-outputs contract — contour-discovered target count allocated by the executor at execution time) in src/Beutl.Engine/Graphics/FilterEffects/SplitEffect.cs and src/Beutl.Engine/Graphics/FilterEffects/PartsSplitEffect.cs
- [x] T045 [US4] Migrate ComputeNode effects: PixelSortEffect (3-shader multi-pass, pooled ping-pong) and GLSLScriptEffect in src/Beutl.Engine/Graphics/FilterEffects/PixelSortEffect.cs and src/Beutl.Engine/Graphics/FilterEffects/GLSLScriptEffect.cs
- [x] T046 [US4] Migrate script/meta effects: SKSLScriptEffect (whole-source + CoordinateInvariant opt-in), CSharpScriptEffect (script globals on GeometrySession — breaking; legacy scripts fail at script compile time with a migration diagnostic, per contracts/breaking-changes.md — *superseded post-removal: globals rebuilt on EffectGraphBuilder, restoring full declarative authoring*), FilterEffectGroup (child concatenation), FilterEffectPresenter (delegating describe), FallbackFilterEffect (identity) in src/Beutl.Engine/Graphics/FilterEffects/ (their existing files)
- [x] T047 [US4] Keep NodeGraphFilterEffect as a render-node boundary on the 003 CreateRenderNode seam (its legacy ApplyTo already throws): update NodeGraphFilterEffectRenderNode so inner effects execute through the new pipeline and drop the ApplyTo override with the removal, verifying parity, in src/Beutl.NodeGraph/NodeGraphFilterEffect.cs and src/Beutl.NodeGraph/NodeGraphFilterEffectRenderNode.cs

### US4 gates

- [x] T048 [US4] Extend parity tests to the full research §0 census (42 effects) + SplitTree/HeavySource chains vs frozen references; confirm existing 003 golden suites remain green, in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectMigrationParityTests.cs
- [x] T049 [P] [US4] Add plugin-style authoring test: one effect per descriptor kind (all seven realizing the spec's five primitives — research D7 taxonomy) implemented against public API only; invariant shader node fuses between two built-ins (SC-006); declared-ROI vs sampled-region debug assertion for a convolution-style node (A3) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectAuthoringTests.cs
- [x] T050 [P] [US4] Add FR-007 peak-intermediates test (10-effect chain peak-live ≤ same-shape 3-effect chain; dynamic-outputs passes counted and leak-checked but exempt from the static bound), structural-threshold test (animating SplitEffect divisions ⇒ one recompile per topology change), allocation-failure normalization tests (forced pool failure on fused/geometry/compute paths ⇒ preview drop, delivery throw — C7), and no-Vulkan fallback tests (Skia-only context executes fused plans; ComputeNode declared fallback applies — FR-014) in tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs

**Checkpoint**: legacy path unreachable in production — PR for rollout step 5

---

## Phase 8: Removal & Polish (rollout step 6)

**Purpose**: Delete the imperative surface, verify success criteria end to end, ship the breaking change

- [x] T051 Remove FilterEffectActivator, CustomFilterEffectContext, the FilterEffectContext recording surface, public SKImageFilterBuilder, EffectTarget/EffectTargets, FEImpl.cs, and the T019 bridge; make FilterEffect.Describe abstract and delete ApplyTo, in src/Beutl.Engine/Graphics/FilterEffects/
- [x] T052 Repo-wide liveness verification: grep for every removed symbol across src/ and tests/ (zero references — SC-007), migrate any stragglers, and run the full solution build + test (dotnet build Beutl.slnx; dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings)
- [x] T053 [P] Re-run tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs, record after-numbers next to the baseline, and verify SC-005 (≥ 60% counters, ≥ 20% time or the re-pinned targets) in docs/specs/004-gpu-pass-fusion/notes/baseline.md
- [x] T054 [P] Finalize the author migration guide (before/after samples incl. a CSharpScriptEffect script) in docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md and draft the `refactor!` + `BREAKING CHANGE:` release note text
- [x] T055 Update CLAUDE.md SPECKIT block (004 status → shipped guardrails) and run dotnet format Beutl.slnx before the final PR

---

## Dependencies

- **Phase 1 → 2 → 3 (US5) → 4 (US3) → 5 (US1) → 6 (US2) → 7 (US4) → 8**: the rollout order is binding (FR-020); each checkpoint is a PR gate with build + full tests green.
- Within US1: T015–T017 [P] → T018 → T019 → T020 → T021/T022 → T023/T024 → T025 → migrations T026–T028 [P] → gates T029–T032.
- Within US4: T037–T039 [P] → T040 → migrations T041–T047 (T041–T044 [P]; T045–T047 after T040) → gates T048–T050.
- US2 (T033–T036) depends only on US1's compiler/executor; it can start once T025 lands.
- T004 (frozen references) blocks every parity task (T031, T048); T010 (baseline) blocks the SC-005 comparison (T053).

## Parallel Execution Examples

- **US5**: T009 (benchmarks) in parallel with T005–T008 (counters); join at T010.
- **US1**: T015, T016, T017 concurrently; after T025, the three migration clusters T026/T027/T028 concurrently, and T029 alongside them.
- **US4**: T037, T038, T039 concurrently; migration clusters T041–T044 concurrently once T040 lands; T049/T050 alongside T048.
- **Phase 8**: T053 and T054 concurrently after T052.

## Implementation Strategy

- **MVP** = through Phase 6 (US5 + US3 + US1 + US2): measured, pooled, fused color chains with cached plans — the headline SC-001/002/003 outcomes — while all other effects still render via the bridge. Phases 7–8 complete migration and ship the breaking removal.
- Each checkpoint ships as its own PR (steps 1 and 2 are behavior-identical and low-risk; steps 3–4 carry the parity gates; step 5 is bulk migration; step 6 is the single `refactor!` breaking PR).
- Never merge a step with a red golden suite or a counter gate regression; notes/baseline.md is the single source of truth for the SC-005 numbers.
