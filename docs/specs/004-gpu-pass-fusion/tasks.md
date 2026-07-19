---

description: "Dependency-ordered implementation tasks for renderer-wide GPU pass fusion"
---

# Tasks: Renderer-Wide GPU Pass Fusion

**Input**: Design documents from `docs/specs/004-gpu-pass-fusion/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `quickstart.md`, and all files in `contracts/`

**Tests**: Required. Add each behavior or contract test before its production implementation and confirm that it fails for the intended missing behavior. Baseline characterization tasks are the exception: they must pass against baseline SHA `43a38e665d9bf52548161a3917e748bd1457ff55` before any scheduling change.

**Organization**: Tasks are grouped by user story. US1, US2, and US3 are all P1, but their implementation order is US3 → US2 → US1 because the renderer-wide fusion proof requires the recorder migration and canonical Shader/Geometry descriptions first. US4 and US5 can proceed in parallel after US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with adjacent marked tasks because it owns different files and has no incomplete dependency.
- **[Story]**: Maps the task to one user story from `spec.md`.
- Every task names the exact file or files it changes.

## Phase 1: Setup (Shared Test and Evidence Infrastructure)

**Purpose**: Establish the non-friend API gate, deterministic visual-evidence utilities, and benchmark workload scaffolding before production behavior changes.

- [ ] T001 Create the non-friend NUnit project with only public project references and no `InternalsVisibleTo` dependency in `tests/Beutl.PublicApiContractTests/Beutl.PublicApiContractTests.csproj` and `tests/Beutl.PublicApiContractTests/PublicApiContractTestBase.cs`
- [ ] T002 Register `tests/Beutl.PublicApiContractTests/Beutl.PublicApiContractTests.csproj` in `Beutl.slnx`
- [ ] T003 [P] Add immutable linear-premultiplied RGBA16F read/write, SHA-256, SSIM, linear RGB MAE, alpha MAE, edge-band local-MAE, and per-channel maximum-error helpers in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/Rgba16fGoldenStore.cs` and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/ImageMetrics.cs`
- [ ] T004 [P] Add fixed-seed scene definitions reusable by baseline and feature benchmarks in `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarkScenes.cs` and `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarkConfig.cs`

---

## Phase 2: Foundational Evidence and Request Primitives (Blocking Prerequisites)

**Purpose**: Freeze target-main behavior and add shared value, ownership, request, and diagnostic primitives without changing scheduling decisions.

**⚠️ CRITICAL**: Every task in this phase must be completed and the baseline evidence frozen before any renderer scheduling change. No user-story implementation starts until this phase passes.

- [ ] T005 Define the starting-SHA-only baseline generator as an out-of-tree patch plus pinned-worktree driver for the primary chain, analytic/antialiased thin-line coverage and other boundary controls, multiple roots, target ordering, cache, nested/query, scale/ROI, fallback, 3D, and preview/delivery allocation failures in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch` and `docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh`; also add `docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh` to run the pinned baseline and feature worktrees and hard-fail before comparison on any missing or mismatched fingerprint field; do not leave generator source in any compiled `src/**/*.cs` or `tests/**/*.cs` tree
- [ ] T006 Run `docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh` against SHA `43a38e665d9bf52548161a3917e748bd1457ff55`, apply the patch only in its temporary pinned worktree, and store immutable RGBA16F references plus scene parameters, artifact/generator/paired-runner hashes, counters, failures, and the exact OS/architecture/backend/device/driver/graphics/runtime fingerprint in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline/manifest.json` and `docs/specs/004-gpu-pass-fusion/evidence/target-baseline/*.rgba16f`
- [ ] T007 [P] Add environment-independent manifest/blob/patch/script/runner hash-integrity and per-workload non-vacuity tests plus the reusable normal-CI same-process parity harness using the internal request `FusionMode`, with a fixed device-independent per-channel AA edge maximum error of `0.02` for normal CI and any tighter paired bound sourced only from the exact matching manifest; never select a foreign device blob as an unconditional CI oracle in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionBaselineTests.cs` and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionNonVacuityTests.cs`
- [ ] T008 [P] Add tests for immutable snapshots, validation, `Latest`/`LatestFrame`, reset behavior, gap-free events, and observer-failure isolation in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderPipelineDiagnosticsTests.cs`
- [ ] T009 Implement the internal diagnostic enums, events, immutable snapshot, state, and validating factory without public telemetry surface in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderPipelineDiagnostics.cs`
- [ ] T010 [P] Add failing unit tests for `RenderValueCardinality`, `TargetRegion`, `RenderBoundsContract`, and feature-003 scale-helper validation in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderContractPrimitiveTests.cs`
- [ ] T011 Implement `RenderValueCardinality`, `TargetRegion`, `RenderBoundsContract`, and the relocated scale helpers in `src/Beutl.Engine/Graphics/Rendering/RenderValueCardinality.cs`, `src/Beutl.Engine/Graphics/Rendering/Operations/TargetRegion.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderBoundsContract.cs`, and `src/Beutl.Engine/Graphics/Rendering/RenderScaleUtilities.cs`
- [ ] T012 [P] Add failing ownership tests for owned/borrowed resource registration, duplicate/conflicting registrations, key/version coalescing, null-key isolation, and exact-once discharge in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RenderResourceOwnershipTests.cs`
- [ ] T013 Implement `RenderResource<T>`, `RenderResourceIdentity`, and `RenderRuntimeIdentity` with request-family registration and ownership states in `src/Beutl.Engine/Graphics/Rendering/Operations/RenderResource.cs`
- [ ] T014 [P] Add failing request-model tests for option sanitization, internal fusion-mode inheritance/plan identity, lifecycle transitions, fragment/value ID uniqueness, authored order, provenance, and cache-candidate recording in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderRequestModelTests.cs`
- [ ] T015 [P] Add failing request-owner tests for strict LIFO cleanup, continued cleanup after an individual fault, first-primary/secondary-failure aggregation, and exact discharge versus cache-transfer ownership in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RenderRequestOwnerTests.cs`
- [ ] T016 [P] Add baseline-neutrality tests proving instrumentation enabled versus disabled preserves rendered output, allocation behavior, failure behavior, and cache decisions in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/BaselineDiagnosticsNeutralityTests.cs`
- [ ] T017 Implement immutable request options including internal production-enabled/friend-test-selectable `FusionMode`, request lifecycle state, fragment/value IDs, provenance, cache candidates, and the ordered graph container in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestOptions.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequest.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RecordedRenderGraph.cs`
- [ ] T018 Implement the shared request owner, reverse-order best-effort cleanup, primary/cleanup failure aggregation, and cache-transfer discharge in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestOwner.cs`
- [ ] T019 Wire decision-neutral baseline request/counter observation into the existing frame and pull paths and satisfy every output/allocation/failure/cache assertion from `BaselineDiagnosticsNeutralityTests` in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderPipelineDiagnostics.cs`
- [ ] T020 Capture the baseline test command, source SHA, clean repository state, exact environment fingerprint, raw benchmark output reference, immutable manifest hash, and generator patch/script hashes in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline.md`

**Checkpoint**: Baseline characterization passes, non-vacuity controls exceed their recorded margins, missing references fail, and the evidence-only changes are reviewable independently of renderer behavior.

---

## Phase 3: User Story 3 — Render-Node Authors Record Through One Context (Priority: P1)

**Goal**: Replace executable/disposable operations with transaction-scoped recording, migrate every in-tree author and consumer, and retain correct rendering through conservative compatibility islands with fusion disabled.

**Independent Test**: From the non-friend project, implement every required node shape; verify order, target scope, metadata, cardinality, contribution, eligibility, ownership, and high-level renderer results; then prove every `Process` call performs zero GPU/media/allocation/readback/nested-execution work.

### Tests for User Story 3 — write and observe failures first

- [ ] T021 [P] [US3] Add a source census over compiled `src/**/*.cs` and `tests/**/*.cs` only that fixes the baseline at 29 production and 7 test `Process` overrides and rejects returning overrides, `RenderNodeOperation`, operation factories, `Pull`/`PullToRoot`, list rasterization, `SetOperations`, operation-backed `EffectTarget`, isolated nested processors, independent cache pulls, unclassified raw callbacks, and references to the four removed `RenderNodeContext` scale helpers while explicitly excluding the historical evidence patch in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch`, in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderPipelineMigrationCensusTests.cs`
- [ ] T022 [P] [US3] Add non-friend tests for no-output, pass-through, source/materialized input, opacity/mask/blend, opaque map/combine/expand, contribution, fan-out rejection, custom scale, `RenderScaleUtilities`, Own/Borrow, all applicable cardinalities, and all non-Shader/Geometry `CanBeUsedAsValueInput` rows in `tests/Beutl.PublicApiContractTests/RenderNodeAuthoringContractTests.cs`
- [ ] T023 [P] [US3] Add non-friend tests for guarded/raw target commands, target/input readback declarations, capture plus `ContributeValues`, target scopes, symbolic `TargetLayerScope`, finite `Layer`, target domains, painter order, and the declared resampling of a public output-derived-density `TargetCapture` inside a denser finite scope; prove its `Custom` resolver receives empty `InputSupplies` and only `OutputBounds`, `OutputScale`, and `MaxWorkingScale` in `tests/Beutl.PublicApiContractTests/TargetAuthoringContractTests.cs`
- [ ] T024 [P] [US3] Add non-friend tests for `RenderNodeRenderer` option sanitization, render/measure/hit-test, command/capture measurement flags, separate output/query bounds, shifted raster bounds, ordinary empty rasterization, bitmap ownership, target factory ownership/validation, and disposal in `tests/Beutl.PublicApiContractTests/RenderNodeRendererContractTests.cs`
- [ ] T025 [P] [US3] Add transaction tests for atomic publication, rollback, monotonic cache disablement, nested facade remapping, direct/indirect/separate-target recursion, resource cleanup, and retained context/handle rejection in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/NodeRecordingTransactionTests.cs`
- [ ] T026 [P] [US3] Add recording probes covering GPU context, target factory, snapshots, media reads/decodes, nested renderers, flush/synchronization, readback, and all migrated node shapes in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingSideEffectTests.cs`
- [ ] T027 [P] [US3] Add target-token ordering tests for `SnapshotBackdrop -> Clear -> DrawBackdrop` at the root and inside Blend, transform, and filter scopes, requiring exactly one capture, no implicit capture contribution, Clear between capture and draw, and the later draw to consume exactly the captured snapshot in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/BackdropOrderingTests.cs`
- [ ] T028 [P] [US3] Add HeadlessUI tests proving selected-drawable `Measure().OutputBounds` agrees with the paired `Rasterize` result for shifted and empty output and that the caller owns/disposes the rasterization in `tests/Beutl.HeadlessUITests/SelectedDrawableRenderTests.cs`
- [ ] T029 [P] [US3] Add HeadlessUI tests for DrawableBrush thumbnail rendering, update propagation, and disposal, using the existing GPU gate only where the real thumbnail backend requires it, in `tests/Beutl.HeadlessUITests/DrawableBrushThumbnailTests.cs`

### Implementation for User Story 3

- [ ] T030 [US3] Change `RenderNode.Process` to `void`, introduce sealed non-executable handles, make contexts engine-created/sealed, and implement explicit publication/cardinality/contribution/eligibility in `src/Beutl.Engine/Graphics/Rendering/RenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`, and `src/Beutl.Engine/Graphics/Rendering/RenderFragmentHandle.cs`
- [ ] T031 [US3] Implement checkpointed node transactions, owner validation, child facade remapping, atomic commit/rollback, handle invalidation, and cache-disable rollback in `src/Beutl.Engine/Graphics/Rendering/Planning/NodeRecordingTransaction.cs` and `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`
- [ ] T032 [P] [US3] Implement guarded opaque source/map/combine/expand and materialized-input descriptions with explicit topology, bounds, hit-test, scale, resource, and runtime identities in `src/Beutl.Engine/Graphics/Rendering/Operations/OpaqueRenderDescription.cs` and `src/Beutl.Engine/Graphics/Rendering/Operations/MaterializedInputDescription.cs`
- [ ] T033 [P] [US3] Implement target command, capture, guarded scope, raw scope/command, finite Layer, and symbolic TargetLayerScope descriptions in `src/Beutl.Engine/Graphics/Rendering/Operations/TargetCommandDescription.cs`, `src/Beutl.Engine/Graphics/Rendering/Operations/TargetCaptureDescription.cs`, and `src/Beutl.Engine/Graphics/Rendering/Operations/TargetScopeDescription.cs`
- [ ] T034 [US3] Implement active-token-guarded `RenderExecutionInput`, callback canvas capability checks, output/session lifetimes, declared readback, no-flush close, and composition-global shifted-origin mapping in `src/Beutl.Engine/Graphics/Rendering/RenderExecutionInput.cs`, `src/Beutl.Engine/Graphics/Rendering/Operations/RenderCallbackCanvas.cs`, and `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`
- [ ] T035 [US3] Implement same-request `RecordNode`/`RecordSubtree`, request-family active-node cycle detection, child publication remapping, and nested request declarations in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs`
- [ ] T036 [US3] Implement ordered fragment/value recording and scope-local target-token lowering for root, finite Layer, non-empty/empty TargetLayerScope, target commands, captures, and typed/raw scopes in `src/Beutl.Engine/Graphics/Rendering/Planning/RecordedRenderGraph.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs`
- [ ] T037 [US3] Implement the fusion-disabled compatibility compiler/executor and disposable high-level render/rasterize/measure/hit-test facade in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestCompiler.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/CompiledRenderRequest.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, and `src/Beutl.Engine/Graphics/Rendering/RenderNodeRasterization.cs`
- [ ] T038 [P] [US3] Migrate pass-through, drop, transform/clip, opacity/blend, and Layer/Push nodes to typed context recording in `src/Beutl.Engine/Graphics/Rendering/ContainerRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/MemoryNode.cs`, `src/Beutl.Engine/Graphics/Rendering/TransformRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RectClipRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/GeometryClipRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/OpacityRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/BlendModeRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/LayerRenderNode.cs`, and `src/Beutl.Engine/Graphics/Rendering/PushRenderNode.cs`
- [ ] T039 [P] [US3] Migrate geometry, shape, text, image, video, and both drawable-group source overrides to deferred typed/opaque recording in `src/Beutl.Engine/Graphics/Rendering/GeometryRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RectangleRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/EllipseRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/TextRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/ImageSourceRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/VideoSourceRenderNode.cs`, and `src/Beutl.Engine/Graphics/DrawableGroup.cs`
- [ ] T040 [P] [US3] Migrate clear, snapshot/draw backdrop, and opacity-mask nodes to ordered command/capture/scope records; satisfy every root and nested `BackdropOrderingTests` sequence; add built-in typed backdrop binding; snapshot/lower known brush masks and nested DrawableBrush work declaratively; and classify unknown/custom brush or backdrop hooks as raw external work in `src/Beutl.Engine/Graphics/Rendering/ClearRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/SnapshotBackdropRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/DrawBackdropRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/OpacityMaskRenderNode.cs`, and `src/Beutl.Engine/Graphics/BrushConstructor.cs`
- [ ] T041 [P] [US3] Migrate filter, referenced-child, operation-wrapper, NodeGraph output, and ProjectSystem scene bridges to request-local recording without retaining handles in `src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/ReferencesChildRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/OperationWrapperRenderNode.cs`, `src/Beutl.NodeGraph/NodeGraphFilterEffectRenderNode.cs`, and `src/Beutl.ProjectSystem/ProjectSystem/SceneDrawable.cs`
- [ ] T042 [P] [US3] Migrate audio visualizer, particle, and 3D overrides to declared raw/opaque/backend records without execution in `Process` in `src/Beutl.Engine/Graphics/AudioVisualizers/AudioVisualizerRenderNode.cs`, `src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs`, and `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs`
- [ ] T043 [P] [US3] Migrate only the seven test-local `Process` overrides to the void recording contract in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/NodeCacheScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeProcessorExceptionSafetyTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RendererExceptionSafetyTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs`, and `tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs`; the remaining old-API test consumers are owned by the following migration tasks
- [ ] T044 [P] [US3] Migrate the nine render-node authoring suites from executable operations to recording/rasterization contracts in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ClearRenderNodeTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ContainerRenderNodeTest.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/EllipseRenderNodeTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/FilterEffectRenderNodeTest.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/GeometryRenderNodeTest.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ImageSourceRenderNodeTest.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RectClipRenderNodeTest.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RectangleRenderNodeTest.cs`, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/VideoSourceRenderNodeTest.cs`
- [ ] T045 [P] [US3] Migrate the ten execution, hit-test, scale, and cross-project suites from old pull/operation APIs while preserving their existing assertions in `tests/Beutl.UnitTests/Engine/Graphics/Backend/BackdropScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/HitTestParityTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/MaxWorkingScaleSanitizationTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ParticleRenderNodeScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ResolutionScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/WorkingScaleClampConsistencyTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics3D/Scene3DRenderNodeScaleTests.cs`, `tests/Beutl.UnitTests/Engine/TextBlockTests.cs`, `tests/Beutl.UnitTests/Graphics/ProxyVideoLogicalSizeTests.cs`, and `tests/Beutl.UnitTests/ProjectSystem/SceneDrawableScaleTests.cs`
- [ ] T046 [US3] Migrate `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GoldenImageHarness.cs` from `PullToRoot` and list results to one caller-owned `RenderNodeRasterization`, preserving union/raster logical origin and output density while leaving all 18 existing golden consumers unchanged
- [ ] T047 [US3] Migrate or retire every production scale-helper caller with a member/type rename only and preserve all feature-003 formulas and clamping behavior in `src/Beutl.Editor/Models/SaveFrameScale.cs`, `src/Beutl.Engine/Graphics/BrushConstructor.cs`, `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`, `src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs`, `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs`, `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs`, `src/Beutl.Engine/Graphics/Particles/ParticleRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`, `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`, `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs`, `src/Beutl.Engine/Graphics3D/Textures/DrawableTextureSource.cs`, `src/Beutl/Helpers/ExportSupersampling.cs`, `src/Beutl/ViewModels/Dialogs/SaveFrameDialogViewModel.cs`, and `src/Beutl/ViewModels/Tools/OutputViewModel.cs`
- [ ] T048 [US3] Migrate every test scale-helper caller to `RenderScaleUtilities` while preserving feature-003 assertions verbatim in `tests/Beutl.UnitTests/Editor/ExportSupersamplingTests.cs`, `tests/Beutl.UnitTests/Editor/SaveFrameScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/CustomTargetClampConsistencyTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/MaxWorkingScaleSanitizationTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ParticleRenderNodeScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ResolutionScaleTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceEffectiveScaleFlowTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/WorkingScaleClampConsistencyTests.cs`, and `tests/Beutl.UnitTests/NodeGraph/NodeGraphFilterEffectRenderNodeTests.cs`
- [ ] T049 [US3] Migrate Engine pull/raster/cache/thumbnail/texture/canvas consumers to `RenderNodeRenderer` or same-request recording in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs`, `src/Beutl.Engine/Graphics/Rendering/Cache/RenderNodeCacheHelper.cs`, `src/Beutl.Engine/Graphics/SourceVideo.Thumbnails.cs`, `src/Beutl.Engine/Graphics3D/Textures/DrawableTextureSource.cs`, and `src/Beutl.Engine/Graphics/ImmediateCanvas.cs`
- [ ] T050 [P] [US3] Migrate NodeGraph, AgentToolkit, and application query/preview/player consumers to high-level render/measure/hit-test/rasterize ownership; make `PlayerViewModel` use `Measure().OutputBounds` for its selected-drawable measure/raster pair, make `MeasureNode` and `QueryTools` use `Measure().QueryBounds`, and update `QueryTools` runtime `MeasurementNote`/coordinate strings to describe contributing query fragments rather than operations in `src/Beutl.NodeGraph/Nodes/Utilities/MeasureNode.cs`, `src/Beutl.NodeGraph/Nodes/Utilities/PreviewNode.cs`, `src/Beutl.AgentToolkit/Tools/QueryTools.cs`, `src/Beutl/Helpers/AvaloniaTypeConverter.cs`, and `src/Beutl/ViewModels/PlayerViewModel.cs`
- [ ] T051 [US3] Remove executable `RenderNodeOperation` and public `RenderNodeProcessor`, delete operation retention and operation-backed `EffectTarget` members, remove every scale-helper shim after all explicit caller migrations, and update `EffectiveScale` provenance XML documentation from operations to recorded fragments/values in `src/Beutl.Engine/Graphics/Rendering/RenderNodeOperation.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeProcessor.cs`, `src/Beutl.Engine/Graphics/Rendering/OperationWrapperRenderNode.cs`, `src/Beutl.Engine/Graphics/FilterEffects/EffectTarget.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`, and `src/Beutl.Engine/Graphics/Rendering/EffectiveScale.cs`
- [ ] T052 [US3] Run the migration census, public authoring contracts, transaction/recording-side-effect/backdrop-ordering suites, every starting-SHA direct old-API test scan hit plus the explicitly named adjacent authoring suites, the standalone golden harness with its 18 unchanged consumers, and both HeadlessUI consumer suites from `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RenderPipelineMigrationCensusTests.cs`, `tests/Beutl.PublicApiContractTests/RenderNodeAuthoringContractTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/NodeRecordingTransactionTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/RecordingSideEffectTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/BackdropOrderingTests.cs`, `tests/Beutl.HeadlessUITests/SelectedDrawableRenderTests.cs`, and `tests/Beutl.HeadlessUITests/DrawableBrushThumbnailTests.cs`

**Checkpoint**: Every production/test override and direct consumer uses one recording context; compatibility rendering matches the frozen baseline with fusion disabled; no executable-operation escape or recording-time side effect remains.

---

## Phase 4: User Story 2 — Existing Effect Authors Keep Their Workflow and Can Opt In (Priority: P1)

**Goal**: Preserve `FilterEffect.ApplyTo` and all existing ordered context items while adding shared deferred Shader and Geometry descriptions to both authoring contexts.

**Independent Test**: Compile and render an unchanged plugin-style effect in the non-friend assembly, then author Shader and Geometry effects using public API only and verify order, bounds, deferred execution, rollback, resource ownership, and unfused planner participation.

### Tests for User Story 2 — write and observe failures first

- [ ] T053 [P] [US2] Add a non-friend unchanged `ApplyTo` source/render compatibility test covering existing color, Skia, transform, group, and legacy custom item order in `tests/Beutl.PublicApiContractTests/FilterEffectCompatibilityContractTests.cs`
- [ ] T054 [P] [US2] Add non-friend CurrentPixel and WholeSource Shader authoring, uniform/resource binding, bounds, ordering, input eligibility/rejection, eligible Shader → Opacity → Shader propagation, explicit analytic/antialiased coverage-boundary behavior, and unfused fallback tests in `tests/Beutl.PublicApiContractTests/ShaderAuthoringContractTests.cs`
- [ ] T055 [P] [US2] Add non-friend Geometry authoring tests for input eligibility/rejection, zero-or-one mapping, bounds/hit-test contracts, declared resources/readback, shrink/discard, and retained-facade rejection in `tests/Beutl.PublicApiContractTests/GeometryAuthoringContractTests.cs`
- [ ] T056 [P] [US2] Add `FilterEffectContext` tests for synchronous bounds updates, mixed legacy/new ordering, invalid/throwing append rollback, nested group rollback, clone/child semantics, and exact-once resource cleanup in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Recording/FilterEffectRecordingTransactionTests.cs`
- [ ] T057 [P] [US2] Add Shader lexer/validator/binding tests for restricted CurrentPixel grammar, coordinates, declarations, names/types, canonical unmanaged values, request-unique binders, resource spaces, full equality after hash collision, and the rule that coordinate independence alone does not prove premultiplied-coverage homogeneity in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ShaderDescriptionTests.cs`
- [ ] T058 [P] [US2] Add Geometry session tests for transparent initialization, canonical shifted device bounds, composition-global mapping, one-shot canvas/input/readback use, capability violations, shrink/discard, and no close-induced flush in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/GeometrySessionTests.cs`

### Implementation for User Story 2

- [ ] T059 [P] [US2] Implement normalized SkSL source storage, restricted CurrentPixel validation, post-upstream-coverage CurrentPixel semantics, WholeSource form, bounds/color contracts, and full structural comparison with no public coverage-homogeneity assertion flag in `src/Beutl.Engine/Graphics/FilterEffects/SkslSource.cs` and `src/Beutl.Engine/Graphics/FilterEffects/ShaderDescription.cs`
- [ ] T060 [P] [US2] Implement canonical direct uniform values, custom uniform/resource binders, coordinate spaces, structural/runtime identities, scoped writers, and execution context in `src/Beutl.Engine/Graphics/FilterEffects/ShaderBindings.cs`
- [ ] T061 [P] [US2] Implement deferred Geometry description, mandatory bounds/hit-test/readback/resource declarations, callback-scoped session, and zero-or-one output control in `src/Beutl.Engine/Graphics/FilterEffects/GeometryDescription.cs` and `src/Beutl.Engine/Graphics/FilterEffects/GeometrySession.cs`
- [ ] T062 [US2] Add atomic ordered `Shader`, `Geometry`, `Own`, and `Borrow` recording while preserving every existing `ApplyTo` member and synchronous `Bounds` semantics in `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectContext.cs` and `src/Beutl.Engine/Graphics/FilterEffects/FilterEffect.cs`
- [ ] T063 [US2] Lower existing color/Skia/transform items when equivalence is proven and lower new Shader/Geometry plus retained custom work into typed or `LegacyCustomEffect` islands in `src/Beutl.Engine/Graphics/FilterEffects/FEImpl.cs`, `src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs`, and `src/Beutl.Engine/Graphics/Rendering/FilterEffectRenderNode.cs`
- [ ] T064 [US2] Implement the ordinary unfused 2D Shader execution and runtime binding path, including explicit validation/program/binder failures and deferred native child creation, in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs` and `src/Beutl.Engine/Graphics/FilterEffects/ShaderBindings.cs`
- [ ] T065 [US2] Implement the Geometry execution island with standard working density, transparent output initialization, optional input readback, shrink/discard validation, and exact cleanup in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs` and `src/Beutl.Engine/Graphics/FilterEffects/GeometrySession.cs`
- [ ] T066 [US2] Restrict `EffectTarget` to execution-time materialized targets while preserving legacy `CustomFilterEffectContext` behavior and marking its uninspectable work external in `src/Beutl.Engine/Graphics/FilterEffects/EffectTarget.cs`, `src/Beutl.Engine/Graphics/FilterEffects/EffectTargets.cs`, and `src/Beutl.Engine/Graphics/FilterEffects/CustomFilterEffectContext.cs`
- [ ] T067 [US2] Run the unchanged ApplyTo, Shader, Geometry, bounds/rollback, and legacy custom-effect suites in `tests/Beutl.PublicApiContractTests/FilterEffectCompatibilityContractTests.cs`, `tests/Beutl.PublicApiContractTests/ShaderAuthoringContractTests.cs`, `tests/Beutl.PublicApiContractTests/GeometryAuthoringContractTests.cs`, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/FilterEffectCrashSafetyTests.cs`

**Checkpoint**: Ordinary effect source remains compatible; Shader and Geometry are public deferred opt-ins on the old `ApplyTo` lifecycle; no effect callback performs rendering during recording.

---

## Phase 5: User Story 1 — Faster Complete 2D Rendering Without Visual Changes (Priority: P1) 🎯 MVP

**Goal**: Record all target-surface roots before execution and fuse a distinct CurrentPixel Shader → Opacity render node → CurrentPixel Shader chain into one compatible GPU pass without visual regression.

**Independent Test**: Keep the three stages as distinct nodes, compare the fusion-disabled result to the frozen baseline, then require one island, exactly one planned/executed GPU pass, at most one intermediate, no per-stage synchronization, a warmed program-cache hit, and all visual/non-vacuity thresholds with fusion enabled.

### Tests for User Story 1 — write and observe failures first

- [ ] T068 [P] [US1] Add complete-target request tests proving every tree is updated and all top-level roots, root clear, target commands, captures, and painter ordering are recorded before any planner-controlled 2D execution; mark hardware execution cases `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RendererWideRecordingTests.cs`
- [ ] T069 [P] [US1] Add the distinct-node Gamma CurrentPixel Shader → OpacityRenderNode → Invert CurrentPixel Shader golden from a deterministic materialized semitransparent source, comparing internal `FusionMode.Disabled` and `Enabled` in the same process/device, with non-vacuity, eligibility, exactly-one-pass, intermediate, synchronization, warmed-program, plan-identity isolation, and `[Category("GpuPassFusionGpu")]` assertions in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/CrossNodeShaderFusionTests.cs`
- [ ] T070 [P] [US1] Add exact same-process internal `FusionMode.Disabled`-versus-`Enabled` split and parity tests for analytic/antialiased coverage, WholeSource, Geometry, opaque callback, readback, destination-dependent Blend, dynamic expansion, external/materialized input, cache boundary, 3D/backend transition, and backend Shader-limit barriers; use `return color * color.a;` after an antialiased thin stroke for the edge-focused/max-error coverage control, prove the exact materialization boundary, and mark hardware cases `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/FusionBoundaryTests.cs`
- [ ] T071 [P] [US1] Add token-aware merge tests for identifier isolation, functions/constants/arrays, binding layout, stage/uniform/sampler/child/source limits, deterministic splits, hash collisions, stage order, and coverage-homogeneity metadata; mark hardware execution cases `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/SkslSnippetMergerTests.cs`

### Implementation for User Story 1

- [ ] T072 [US1] Change production frame sequencing to build every tree, record one ordered request for the target surface, execute once, and commit bounds/render counts/cache state only after success in `src/Beutl.Engine/Graphics/Rendering/Renderer.cs` and `src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`
- [ ] T073 [US1] Resolve request-wide root provenance, ordered publications, separate output/query metadata, and scope-token dependencies before planning in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RecordedRenderGraph.cs`
- [ ] T074 [US1] Partition maximal dependency-consistent islands with explicit boundary reasons, honor internal `FusionMode.Disabled` without changing semantic lowering, and preserve value/target order, scope, contribution, cardinality, cache, backend, and synchronization contracts in `src/Beutl.Engine/Graphics/Rendering/Planning/ExecutionIslandPlanner.cs`
- [ ] T075 [P] [US1] Implement lexer/token-aware CurrentPixel snippet composition, symbol renaming, binding-layout merge, and deterministic backend-budget splitting in `src/Beutl.Engine/Graphics/FilterEffects/SkslSnippetMerger.cs`
- [ ] T076 [US1] Compile eligible CurrentPixel and invariant-opacity stages into `CompiledShaderRun` records while keeping analytic/antialiased coverage-producing source boundaries, WholeSource, Geometry, opaque, readback, target, cache, dynamic, external, and backend work as barriers unless an engine-known stage is mechanically proven premultiplied-coverage-homogeneous in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestCompiler.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/CompiledRenderRequest.cs`
- [ ] T077 [US1] Implement full-key program lookup, merged program creation, re-entrant leases, runtime binding reset, and warmed hit accounting in `src/Beutl.Engine/Graphics/Rendering/Planning/ProgramCache.cs`
- [ ] T078 [US1] Execute compiled islands once in dependency/painter order, bind final runtime values after plan selection, and avoid implicit per-stage materialization/flush in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T079 [US1] Canonicalize eligible `OpacityRenderNode` output into the CurrentPixel run only when value eligibility, scope-token equivalence, color/alpha behavior, premultiplied-coverage homogeneity, and ordering are engine-proven in `src/Beutl.Engine/Graphics/Rendering/OpacityRenderNode.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/ExecutionIslandPlanner.cs`
- [ ] T080 [US1] Emit exact recorded/boundary/planned/executed/fused/intermediate/synchronization/program events and counters for the primary and barrier requests in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderPipelineDiagnostics.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestCompiler.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T081 [US1] Run the primary fusion, complete-request ordering, merger, and boundary suites in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/CrossNodeShaderFusionTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RendererWideRecordingTests.cs`, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/FusionBoundaryTests.cs`

**Checkpoint (MVP)**: Renderer-wide recording is active, the distinct cross-node chain renders in exactly one pass with parity, and every required barrier splits deterministically.

---

## Phase 6: User Story 4 — Animation and Render Caching Remain Efficient and Correct (Priority: P2)

**Goal**: Reuse structural plans, programs, output-cache values, and exact-size intermediates across stable requests while invalidating every pixel-affecting change safely.

**Independent Test**: Render 100 parameter-only frames, then one structural change; verify one structural compilation, no program creation after frame 1, one affected replacement compilation, correct cache/pool counters, and parity with a fresh uncached render.

### Tests for User Story 4 — write and observe failures first

- [ ] T082 [P] [US4] Add 100-frame parameter animation, bounds-only runtime change, structural toggle, direct uniform, custom binder/runtime identity, and full-key collision tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/StructuralAndProgramCacheTests.cs`
- [ ] T083 [P] [US4] Add parent/child hit selection, parent supersession, command/raw/target-dependent bypass, coverage/density/format/purpose/device invalidation, and static-prefix/animated-tail tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheResolutionTests.cs`
- [ ] T084 [P] [US4] Add stable/changing-size pool, 3-stage versus 10-stage peak live, fan-out last use, LRU/byte/idle eviction, generation, stale/double release, and context recreation tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderTargetPoolTests.cs`
- [ ] T085 [P] [US4] Add renderer-disposal and cache-publication ownership tests proving accepted factory targets, plans, and programs are released while root/cache/factory/raster results remain borrowed or independently owned in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RenderNodeRendererLifetimeTests.cs`

### Implementation for User Story 4

- [ ] T086 [US4] Implement complete structural identity including internal fusion mode, parameter-independent plan reuse, full comparison after hash bucketing, and one affected replacement on mismatch in `src/Beutl.Engine/Graphics/Rendering/Planning/StructuralPlanCache.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestCompiler.cs`
- [ ] T087 [US4] Resolve render-cache candidates only after graph/region discovery, preserve provenance and token edges, select parent/child boundaries, and stage successful miss captures in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderCacheResolver.cs`
- [ ] T088 [P] [US4] Add program-cache byte/LRU/device eviction, re-entrant lease safety, runtime reset, and full source/signature equality to `src/Beutl.Engine/Graphics/Rendering/Planning/ProgramCache.cs`
- [ ] T089 [P] [US4] Implement renderer-owned exact-size RGBA16F buckets, factory validation, byte/LRU/idle/context eviction, generation tags, and exact lease states in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderTargetPool.cs`
- [ ] T090 [US4] Compute first/last-use intervals, exact target reuse, fan-out lifetimes, peak-live accounting, and cache-transfer discharge in `src/Beutl.Engine/Graphics/Rendering/Planning/ResourcePlan.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T091 [US4] Publish cache captures atomically after complete request success and transfer accepted payload ownership from the request/pool to the existing cache lifecycle in `src/Beutl.Engine/Graphics/Rendering/Cache/RenderNodeCache.cs`, `src/Beutl.Engine/Graphics/Rendering/Cache/RenderNodeCacheHelper.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T092 [US4] Run the animation, cache selection, pool lifetime, static-prefix parity, and renderer-disposal suites in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/StructuralAndProgramCacheTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Cache/RenderCacheResolutionTests.cs`, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderTargetPoolTests.cs`

**Checkpoint**: Stable warmed frames allocate no new targets or programs, structural/runtime identities invalidate at the correct layer, and cache ownership transfers reconcile exactly.

---

## Phase 7: User Story 5 — Scales, Regions, Fallbacks, and Boundaries Stay Correct (Priority: P2)

**Goal**: Complete post-record bounds/ROI analysis, preserve feature-003 density behavior, execute every public Shader on the supported fallback backend, and isolate 3D as one explicit materialized boundary.

**Independent Test**: Compare bounds, densities, schedules, and images across multiple scales, shifted/outside/empty regions, target-domain forms, 3D input, and the supported non-preferred backend using the frozen baseline.

### Tests for User Story 5 — write and observe failures first

- [ ] T093 [P] [US5] Add shifted/outside/empty/full ROI, forward growth/shrink, full-input fallback, invalid mapping, fan-out union, target-read apron, and density-stability tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalyzerTests.cs`
- [ ] T094 [P] [US5] Add root and finite-Layer `[A, Clear, B]`, symbolic/empty TargetLayerScope, transformed Full resolution, missing target domain, output/query bounds, shifted raster, and empty raster tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/TargetScopeLoweringTests.cs`
- [ ] T095 [P] [US5] Add multi-density vector/bitmap/text, maximum working scale, 16,384-axis clamp, late device binding, shifted guarded callback canvas, and antialiased thin-stroke edge golden tests; compare public declared-density `TargetCapture` resampling inside denser finite Layer/TargetLayerScope targets with engine-internal backdrop late binding and verify the resulting density/cache identity; mark hardware cases `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionScaleRegionTests.cs`
- [ ] T096 [P] [US5] Add ordinary no-preferred-GPU Shader fallback tests that never self-skip plus hardware-gated 3D boundary/pass-shape tests marked `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ShaderFallbackTests.cs` and `tests/Beutl.Graphics3DTests/GpuPassFusion3DBoundaryTests.cs`
- [ ] T097 [P] [US5] Add regression coverage for all existing feature-003 density/golden requirements and characterized preview/delivery allocation outcomes; mark hardware cases `[Category("GpuPassFusionGpu")]` in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionFeature003RegressionTests.cs`

### Implementation for User Story 5

- [ ] T098 [US5] Implement forward metadata and reverse required-region propagation with explicit Full/Empty/finite states, unioned fan-out, conservative full-input fallback, target-read expansion, and invalid-map failures in `src/Beutl.Engine/Graphics/Rendering/Planning/RegionAnalyzer.cs`
- [ ] T099 [US5] Resolve symbolic Full only after enclosing transform/clip/root/TargetLayerScope/finite-Layer domains are known, preserve empty order-only scopes, and reject target-less unresolved Full in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RecordedRenderGraph.cs`
- [ ] T100 [US5] Keep `RootOutputExtent`, `QueryBounds`, `TargetDomain`, `RequestedRegion`, final commit crop, measurement, hit testing, and rasterization bounds independent in `src/Beutl.Engine/Graphics/Rendering/Planning/RegionAnalyzer.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, and `src/Beutl.Engine/Graphics/Rendering/RenderNodeRasterization.cs`
- [ ] T101 [US5] Apply feature-003 working-density resolution and complete-bounds clamping during recording, preserve eligible stages until materialization while resolving vector/text/path coverage before arbitrary public CurrentPixel work, and bind cropped device values without recomputing density in `src/Beutl.Engine/Graphics/Rendering/RenderScaleUtilities.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T102 [US5] Record 3D metadata without execution, render full declared 3D bounds into one RGBA16F value, count transition/synchronization, block inward 2D ROI, and allow eligible downstream 2D work after the boundary in `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T103 [US5] Implement an unfused supported ordinary-2D path for every valid public Shader when preferred fusion/backend capability is unavailable while preserving explicit invalid-source/program failures in `src/Beutl.Engine/Graphics/Rendering/Planning/ExecutionIslandPlanner.cs` and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T104 [US5] Run region, target-scope, scale/golden, fallback, feature-003, and 3D boundary suites in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RegionAnalyzerTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/TargetScopeLoweringTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionScaleRegionTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Fusion/ShaderFallbackTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/GpuPassFusionFeature003RegressionTests.cs`, and `tests/Beutl.Graphics3DTests/GpuPassFusion3DBoundaryTests.cs`

**Checkpoint**: ROI and density remain correct for shifted/empty/full requests, fallback rendering works without a preferred GPU, and 3D is an explicit one-value boundary rather than part of the 2D optimizer.

---

## Phase 8: User Story 6 — Maintainers Can Prove Whole-Request Improvement and Safety (Priority: P3)

**Goal**: Reconcile every fragment and resource on success/failure and produce provenance-locked visual/performance evidence using persistent production-equivalent lifetimes.

**Independent Test**: Run deterministic correctness, failure, and paired baseline/feature benchmarks; reconcile all terminal outcomes and acquisitions; verify primary exceptions and cleanup behavior; require the primary warmed post/pre 95% confidence interval to lie below 1.0.

### Tests for User Story 6 — write and observe failures first

- [ ] T105 [P] [US6] Add complete-request outcome, classification, acquire/discharge, external-root, nested-request, opaque-external, `Latest`/`LatestFrame`, success/failure phase, and event-order reconciliation tests in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderPipelineReconciliationTests.cs`
- [ ] T106 [P] [US6] Add recording/ApplyTo/resource-transfer, Own/Borrow conflict, recursion, bounds/ROI, and cache lookup/substitution/staging/publication failure injection in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RecordingAndPlanningFailureTests.cs`
- [ ] T107 [P] [US6] Add materialization, target acquisition, Shader validation/merge/program/binding/provider, and program/pool disposal failure injection in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/ShaderAndAllocationFailureTests.cs`
- [ ] T108 [P] [US6] Add Geometry/opaque/target/input readback, canvas open/close, dispose/snapshot/nested-draw/SaveLayer/undeclared-resource/hidden-allocation/hidden-flush, dynamic output, and retained-facade failure injection in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/DeferredCallbackFailureTests.cs`
- [ ] T109 [P] [US6] Add nested request, 3D/backend transition, target command/capture/scope, raw callback, cache-transfer, cleanup-fault, primary-exception, and no-partial-publication failure injection in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/NestedTargetAndCleanupFailureTests.cs`

### Implementation and Evidence for User Story 6

- [ ] T110 [US6] Finalize gap-free diagnostic emission and validating reconciliation so every committed fragment has exactly one terminal outcome and every request acquire is discharged after cleanup or cache transfer in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderPipelineDiagnostics.cs`
- [ ] T111 [US6] Make recorder, analyzer, cache resolver, compiler, and executor preserve the first primary exception, mark dependent outcomes, reject partial output/cache publication, continue cleanup, and classify secondary failures in `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestRecorder.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RegionAnalyzer.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderCacheResolver.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestCompiler.cs`, and `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs`
- [ ] T112 [US6] Implement persistent-lifetime BenchmarkDotNet cases for no effect, one Shader, primary cross-node chain, hard barrier, long chain, parameter animation, structural toggle, static prefix, mixed spatial/color, small object, and multiple target-dependent roots in `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs`
- [ ] T113 [US6] Include output verification and request-wide counters in benchmark setup/results while keeping renderer/node/cache/pool lifetimes production-equivalent in `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarkConfig.cs` and `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs`
- [ ] T114 [US6] Add a paired baseline/feature runner that records SHAs, exact OS/architecture/backend/device/driver/graphics/runtime fingerprint, commands, raw BenchmarkDotNet output, controls, counters, and bootstrap confidence intervals and fails hard rather than skipping when the two runs' fingerprints differ in `docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh` and `tests/Beutl.Benchmarks/Rendering/PairedBenchmarkAnalyzer.cs`
- [ ] T115 [US6] Run `docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh` and the same-fingerprint paired benchmark, then record generator patch/script/paired-runner hashes, raw-result hashes, parity metrics including the paired exact-fingerprint AA edge bound, non-vacuity margins, controls, barrier tolerances, counters, and the primary 95% confidence interval; separately record the normal-CI same-process fusion-disabled/enabled A/B result and its fixed per-channel AA edge maximum-error bound of `0.02` in `docs/specs/004-gpu-pass-fusion/evidence/acceptance-report.md`
- [ ] T116 [US6] Run the reconciliation and complete failure matrix suites in `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Planning/RenderPipelineReconciliationTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/RecordingAndPlanningFailureTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/ShaderAndAllocationFailureTests.cs`, `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/DeferredCallbackFailureTests.cs`, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Failure/NestedTargetAndCleanupFailureTests.cs`

**Checkpoint**: Visual, performance, diagnostic, ownership, and failure claims are reproducible from committed evidence and all counters reconcile for successful, metadata-only, cached, skipped, opaque-external, nested, and failed requests.

---

## Phase 9: Polish and Cross-Cutting Validation

**Purpose**: Update author guidance, audit the breaking public surface, and run repository-wide gates.

- [ ] T117 [P] Update filter-effect authoring guidance for `ApplyTo`, Shader, Geometry, opaque fallbacks, and the removed executable API in `.claude/skills/beutl-filter-effect/SKILL.md` and `docs/ai-workflow/resolution-independent-rendering.md`
- [ ] T118 [P] Add or complete XML documentation and nullable contracts for the new public authoring surface and update `EffectiveScale` provenance language from executable operations to recorded fragments/values in `src/Beutl.Engine/Graphics/Rendering/RenderNodeContext.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderFragmentHandle.cs`, `src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, `src/Beutl.Engine/Graphics/Rendering/EffectiveScale.cs`, `src/Beutl.Engine/Graphics/FilterEffects/ShaderDescription.cs`, and `src/Beutl.Engine/Graphics/FilterEffects/GeometryDescription.cs`
- [ ] T119 Run `dotnet format Beutl.slnx --verify-no-changes` against `Beutl.slnx` and resolve every reported source-file formatting finding
- [ ] T120 Run dual-target `dotnet build Beutl.slnx` and resolve build/public-contract failures in `Beutl.slnx` and `tests/Beutl.PublicApiContractTests/Beutl.PublicApiContractTests.csproj`
- [ ] T121 Run `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings` and resolve all regressions in `Beutl.slnx` and `coverlet.runsettings`
- [ ] T122 Run the ordinary fallback command on every host and the two hardware-required commands on a capable host: `dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~ShaderFallbackTests"`, `BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj -f net10.0 --filter "(TestCategory=GpuPassFusionGpu|FullyQualifiedName~GpuGoldenSuiteCanaryTests)"`, and `BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj -f net10.0 --filter "TestCategory=GpuPassFusionGpu"`; the ordinary fallback command must never self-skip
- [ ] T123 Run the final persistent-lifetime benchmark gate and verify the committed raw results against `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` and `docs/specs/004-gpu-pass-fusion/evidence/acceptance-report.md`
- [ ] T124 Run public-design, GPL/MIT, XAML, NUnit, and source-generator impact reviews against the complete diff using `.claude/agents/beutl-design-reviewer.md` and `.claude/agents/beutl-reviewer.md`, then resolve every in-scope finding in the affected source/test files
- [ ] T125 Verify the final migration notes and required `refactor(engine)!`/`BREAKING CHANGE:` wording for `Beutl.Engine`, `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream custom-node authors in `docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md`
- [ ] T126 Re-run the requirement-traceability audit against `docs/specs/004-gpu-pass-fusion/spec.md` and the matrix below, require every `FR-001` through `FR-044` (including `FR-010a` and `FR-010b`) and every `SC-001` through `SC-013` to map to at least one concrete task ID, and fail completion on any unknown, duplicate, stale, or unmapped requirement identifier in `docs/specs/004-gpu-pass-fusion/tasks.md`

## Requirement Traceability

Every normative requirement and success criterion has a concrete test, implementation, evidence, or final-gate owner. T126 validates this matrix against `spec.md`; ranges are inclusive.

| Requirement | Concrete task owners |
|---|---|
| FR-001 | T068, T072 |
| FR-002 | T022, T032, T038–T042, T049 |
| FR-003 | T054, T063, T069, T074, T076 |
| FR-004 | T073, T074, T083, T087, T098 |
| FR-005 | T042, T070, T096, T102 |
| FR-006 | T014, T017, T035, T072, T083 |
| FR-007 | T021, T030, T043, T051 |
| FR-008 | T022, T030, T051, T118 |
| FR-009 | T022, T025, T030, T031, T036 |
| FR-010 | T022, T023, T032–T036 |
| FR-010a | T023, T027, T033, T036, T040, T094, T099 |
| FR-010b | T022, T023, T030, T054, T079 |
| FR-011 | T022, T032, T038–T042, T059, T061, T062 |
| FR-012 | T025, T026, T031, T040, T056, T062, T108 |
| FR-013 | T012, T013, T015, T018, T022, T025, T031, T085, T109 |
| FR-014 | T025, T035, T041, T105, T109 |
| FR-015 | T022, T025, T030, T031, T083 |
| FR-016 | T021, T043–T052, T100, T118, T125 |
| FR-017 | T030, T041, T047, T117, T124, T125 |
| FR-018 | T053, T056, T062, T066, T067 |
| FR-019 | T001, T002, T054, T055, T059–T062 |
| FR-020 | T026, T056, T062, T064, T065 |
| FR-021 | T054, T057, T059, T060, T064, T069, T070, T076 |
| FR-022 | T055, T058, T061, T065, T070, T093, T098 |
| FR-023 | T053, T056, T063, T066, T070, T108 |
| FR-024 | T012, T013, T056, T060, T061, T065, T106, T108 |
| FR-025 | T022, T054, T055, T059–T062 |
| FR-026 | T054, T069, T074, T076, T079, T081 |
| FR-027 | T057, T070, T074, T076, T079 |
| FR-028 | T071, T075, T076, T081 |
| FR-029 | T027, T068, T070, T073, T074, T078, T094, T100 |
| FR-030 | T010, T011, T023, T024, T028, T045, T047, T048, T050, T095, T097, T101, T118 |
| FR-031 | T022, T054, T055, T093, T098 |
| FR-032 | T022, T023, T024, T033, T036, T094, T100 |
| FR-033 | T014, T017, T057, T071, T082, T083, T086–T088 |
| FR-034 | T077, T082, T086, T088, T092 |
| FR-035 | T015, T018, T058, T078, T084, T089–T091, T105, T107–T111 |
| FR-036 | T069, T084, T089, T090, T092 |
| FR-037 | T015, T018, T085, T107–T111 |
| FR-038 | T025, T035, T105, T109, T111 |
| FR-039 | T005–T007, T020, T097, T115 |
| FR-040 | T054, T064, T096, T103, T104, T122 |
| FR-041 | T008, T009, T016, T019, T080, T105, T110 |
| FR-042 | T019, T068, T080, T105, T110, T113 |
| FR-043 | T003–T007, T020, T112–T115 |
| FR-044 | T021–T029, T043–T058, T068–T071, T082–T085, T093–T097, T105–T109, T121, T122, T126 |
| SC-001 | T054, T069, T076, T079, T081 |
| SC-002 | T070, T076, T081, T095 |
| SC-003 | T001, T002, T053–T067 |
| SC-004 | T021–T052 |
| SC-005 | T077, T082, T086, T092 |
| SC-006 | T084, T089, T090, T092 |
| SC-007 | T003, T006, T007, T069, T070, T095–T097, T104, T115, T121, T122 |
| SC-008 | T004, T112–T115, T123 |
| SC-009 | T012, T015, T018, T085, T105–T111, T116 |
| SC-010 | T008, T009, T105, T110, T116 |
| SC-011 | T007, T052, T067, T097, T104, T120–T122 |
| SC-012 | T083, T087, T091, T092 |
| SC-013 | T007, T069, T070, T115 |
| Constitution quality gates | T119, T124 |
| Traceability meta-gate | T126 |

---

## Dependencies and Execution Order

### Phase Dependencies

- **Phase 1 — Setup**: Starts immediately.
- **Phase 2 — Foundational evidence and primitives**: Depends on Phase 1 and blocks every user story. The baseline must be generated by the evidence-only patch/script from SHA `43a38e665d9bf52548161a3917e748bd1457ff55` before scheduling changes; no generator source may remain in a compiled project.
- **Phase 3 — US3 recording/migration (P1)**: Depends on Phase 2. It creates the only public recorder and the conservative compatibility executor.
- **Phase 4 — US2 FilterEffect opt-in (P1)**: Depends on US3 context/resource/executor contracts. T059–T061 may proceed in parallel after T030–T034.
- **Phase 5 — US1 renderer-wide fusion (P1)**: Depends on completed US3 migration and US2 canonical descriptions. This is the first product MVP checkpoint.
- **Phase 6 — US4 cache/animation (P2)**: Depends on US1's request-wide planner/compiler/executor.
- **Phase 7 — US5 scale/ROI/fallback/3D (P2)**: Depends on US1 and can run in parallel with US4 except where both touch `RenderRequestExecutor.cs` or diagnostics.
- **Phase 8 — US6 proof/safety (P3)**: Depends on US4 and US5. Story-local failure tests remain test-first in their phases; this phase closes the full matrix and evidence.
- **Phase 9 — Polish**: Depends on all selected stories and acceptance evidence.

### User Story Dependencies

- **US3 (P1)**: Independently demonstrable after Foundation with fusion disabled; blocks US2 and US1 because the old executable lifecycle is removed without a shim.
- **US2 (P1)**: Independently demonstrable after US3 with unfused Shader/Geometry execution; blocks US1's canonical CurrentPixel fusion proof.
- **US1 (P1)**: Depends on US3 and US2; independently proves renderer-wide fusion and is the MVP outcome.
- **US4 (P2)**: Depends on US1; independently proves repeated-request plan/program/cache/pool efficiency and invalidation.
- **US5 (P2)**: Depends on US1; independently proves region/scale/fallback/3D correctness and can be developed alongside US4.
- **US6 (P3)**: Integrates the completed stories into reproducible whole-request evidence and safety gates.

### Within Each Story

- Add the story's tests first and confirm they fail for the missing behavior before editing production files.
- Keep baseline characterization green; never regenerate a missing golden from the implementation under test.
- Implement immutable descriptions/IR before planners, planners before execution, and execution before integration.
- Emit diagnostic events/counters in the same task as each scheduling/resource behavior; do not add instrumentation after the fact.
- Complete each story's checkpoint before treating downstream stories as unblocked.

## Parallel Opportunities

- T003 and T004 can run in parallel after the test project exists.
- T007, T008, T010, T012, and T014–T016 own separate foundational test files and can run in parallel.
- T021–T029 are independent test-first workstreams for US3.
- After T030–T037, migration batches T038–T046 plus consumer tasks T049 and T050 can run in parallel where their named files are disjoint; T047 and T048 are explicit scale-helper sweeps and must coordinate with any overlapping migration batch.
- T053–T058 can run in parallel; T059–T061 can then run in parallel before context lowering/integration.
- T068–T071 can run in parallel before the US1 planner/compiler work.
- T082–T085 can run in parallel; T088 and T089 can run in parallel after their tests.
- US4 and US5 can be assigned to separate developers after US1, coordinating only edits to `src/Beutl.Engine/Graphics/Rendering/Planning/RenderRequestExecutor.cs` and `RenderPipelineDiagnostics.cs`.
- T105–T109 are independent failure/reconciliation test files and can run in parallel.
- T117 and T118 can run in parallel after the public API is final.

## Parallel Example: US3 Migration

```text
Task T038: migrate pure/scope compositor nodes
Task T039: migrate source nodes
Task T040: migrate target/capture/mask nodes
Task T041: migrate nested/bridge nodes
Task T042: migrate opaque/backend nodes
Task T043: migrate test-local Process overrides only
Task T044: migrate render-node authoring tests
Task T045: migrate execution/scale/cross-project tests
Task T046: migrate the standalone golden harness
Task T047: migrate production scale-helper callers
Task T048: migrate test scale-helper callers
```

## Parallel Example: P2 Stories

```text
Developer A: T082–T092 (US4 cache, program, pool, ownership)
Developer B: T093–T104 (US5 ROI, scale, fallback, 3D)
Coordinate: RenderRequestExecutor.cs and RenderPipelineDiagnostics.cs
```

## Implementation Strategy

### MVP First

1. Complete Setup and freeze target-baseline evidence.
2. Complete Foundation.
3. Complete US3's breaking recorder migration with fusion disabled.
4. Complete US2's canonical Shader/Geometry opt-in with unfused execution.
5. Complete US1 and stop at the one-pass cross-node fusion checkpoint.
6. Validate US1 independently against the frozen visual and request-counter baseline before continuing.

### Incremental Delivery

1. **Evidence/Foundation** → auditable baseline and shared ownership primitives.
2. **US3** → one recording lifecycle and conservative parity.
3. **US2** → source-compatible effects plus public Shader/Geometry opt-in.
4. **US1** → renderer-wide one-pass fusion MVP.
5. **US4 + US5** → persistent efficiency and full correctness boundaries in parallel.
6. **US6** → reproducible safety and performance proof.

## Notes

- A `[P]` marker means the task owns different files and all of its prerequisites are already complete; tasks that share `RenderRequestExecutor.cs` or diagnostics must still coordinate or run serially.
- The diagnostic outcome denominator is committed fragments only; value, command, capture, scope, and Layer counters are overlapping classifications.
- Baseline blobs are immutable and fail integrity checks when missing. A paired starting-SHA comparison requires an exact environment fingerprint and fails hard on mismatch; normal CI instead compares fusion-disabled and enabled execution in the same process/device and never silently selects a foreign blob.
- Ordinary fallback and public-contract tests must run without a GPU; only GPU execution-shape assertions may self-skip.
- Commit evidence separately before behavior changes, and commit the public migration with the breaking footer in `contracts/breaking-changes.md`.
