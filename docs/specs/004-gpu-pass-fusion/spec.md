# Feature Specification: Renderer-Wide GPU Pass Fusion

**Feature Branch**: `speckit/004-gpu-pass-fusion`

**Created**: 2026-07-19

**Status**: Draft

**Input**: User description: "Restart feature 004 from current main in a new branch and worktree. Make GPU pass fusion a renderer-wide capability rather than a filter-effect-only subsystem. Keep `FilterEffect.ApplyTo(FilterEffectContext, Resource)`, and add the useful Shader and Geometry recording concepts from `EffectGraphBuilder` to the existing `FilterEffectContext`. Use the abandoned branch only as a source of parts and evidence."

## Overview

Beutl currently executes top-level drawables, filter effects, and ordinary 2D render-node work through boundaries that prevent one optimizer from recognizing compatible GPU work across the complete target-surface request. This feature establishes one planning view over the complete ordered 2D request, then partitions that request into cache-aware execution islands. Compatible work may therefore fuse across effect, opacity, and other participating render-node boundaries instead of being confined to one filter-effect graph.

The existing filter-effect authoring lifecycle remains the canonical public entry point. Effect authors continue to override `FilterEffect.ApplyTo(FilterEffectContext, Resource)`. `FilterEffectContext` gains Shader and Geometry recording capabilities so authors can describe optimizable work without adopting a replacement lifecycle or waiting for a repository-wide effect migration.

The previous feature-004 branch is historical evidence and an extraction source only. Its effect-local planner, replacement authoring lifecycle, and repository-wide imperative-API removal are not the foundation of this feature.

## Scope

### In Scope

- Planning the complete 2D preview, delivery, nested-draw, and auxiliary render request before choosing cache or materialization boundaries.
- Representing every encountered 2D operation as either an operation with declared semantics or an explicit opaque boundary.
- Forming cache-aware execution islands after complete-request dependencies are known.
- Fusing compatible GPU work across filter-effect and ordinary render-node boundaries, with opacity as the first required non-effect proof.
- Retaining the existing `FilterEffect.ApplyTo` and `FilterEffectContext` authoring model while adding Shader and Geometry recording capabilities to that context.
- Preserving legacy filter-effect operations through semantic lowering where sound and conservative opaque execution otherwise.
- Preserving the current per-child effect-group and render-cache granularity while allowing compatible uncached work to optimize across those boundaries.
- Correct bounds, requested-region, scale, cache, synchronization, resource-lifetime, failure, and diagnostic behavior for the planned 2D request.
- Establishing provenance-checked visual and performance baselines from the new branch's starting commit before judging the redesign.

### Out of Scope

- Inspecting or fusing work inside the 3D renderer. A 3D result is an explicit opaque/backend boundary in a 2D request.
- Replacing `ApplyTo` with `Describe`, removing `FilterEffectContext`, or requiring all existing effects and scripts to migrate to a new authoring lifecycle.
- Requiring new public Compute, Split, or Composite effect primitives as a prerequisite for renderer-wide fusion.
- Adding a second public planning vocabulary for arbitrary custom render nodes; unknown custom render nodes remain correctness-preserving opaque boundaries.
- Normalizing allocation-failure behavior that differs among existing preview and delivery paths; this feature preserves the behavior of its current-main baseline.
- Changes to audio processing, media decoding, project persistence, editor UI, or unrelated resource-lifecycle systems.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Faster complete 2D rendering without visual changes (Priority: P1)

An editor user previews or exports a composition containing compatible effects and ordinary 2D operations. Beutl recognizes compatible work across the original render-node boundaries, executes fewer GPU passes and intermediate materializations, and produces the same image.

**Why this priority**: Cross-boundary optimization is the reason for restarting the feature. Effect-only fusion would repeat the architectural limitation of the abandoned attempt.

**Independent Test**: Build a scene with two independently authored coordinate-invariant Shader effects separated by an opacity render node. Verify that the nodes remain distinct, the complete-request schedule contains one compatible GPU pass with at most one intermediate target, and the rendered image meets the parity threshold.

**Acceptance Scenarios**:

1. **Given** two eligible Shader effects separated by invariant opacity in distinct render nodes, **When** the complete 2D request is planned and rendered, **Then** all three stages execute in one GPU pass with at most one intermediate target and parity is preserved.
2. **Given** the same effects separated by an operation whose equivalence has not been proven, **When** the request is planned, **Then** the schedule splits at that explicit boundary and the image remains correct.
3. **Given** compatible stages on opposite sides of a filter-effect render-node boundary, **When** no semantic or cache dependency requires materialization there, **Then** that historical boundary alone does not prevent fusion.
4. **Given** multiple top-level drawables contributing to one target surface, **When** the request is planned, **Then** their complete ordered contributions are visible before any planner-controlled 2D GPU work executes.

---

### User Story 2 - Existing effect authors keep their workflow and can opt in (Priority: P1)

A plugin, script, or built-in effect author continues to implement `ApplyTo` against `FilterEffectContext`. Existing calls keep their behavior, while the author may record Shader or Geometry work in the same ordered context to make new work visible to the renderer-wide planner.

**Why this priority**: Preserving the authoring model avoids another repository-wide migration and lets renderer-wide planning be proven independently of effect conversion.

**Independent Test**: Compile and render an existing plugin-style effect in a non-friend test assembly without source changes, then author separate Shader and Geometry examples using only public API and verify their recorded ordering, deferred execution, bounds, and planner participation.

**Acceptance Scenarios**:

1. **Given** an existing effect that overrides `ApplyTo` and uses current `FilterEffectContext` operations, **When** it is compiled and rendered after this feature, **Then** it requires no lifecycle migration and preserves its baseline behavior.
2. **Given** a public-API consumer that records a coordinate-invariant Shader operation, **When** the operation is placed between other eligible stages, **Then** it can participate in cross-node fusion without accessing engine internals.
3. **Given** a public-API consumer that records Geometry with declared bounds and readback needs, **When** `ApplyTo` runs, **Then** recording performs no drawing, allocation, flush, or readback, and the geometry callback executes later under engine-owned lifetime rules.
4. **Given** legacy, Shader, and Geometry operations recorded in one context, **When** the effect renders, **Then** their authored order is preserved and unsupported legacy work becomes an explicit barrier rather than being reordered or dropped.
5. **Given** an effect whose `Resource` supplies a custom render node, **When** it renders after this feature, **Then** the customization point is still honored and unknown custom work executes as an opaque boundary.
6. **Given** a Shader description whose source or bindings violate the selected current-pixel contract, **When** it is validated, **Then** it is rejected with an explicit diagnostic and is never silently fused or rendered as identity.

---

### User Story 3 - Animation and render caching remain efficient and correct (Priority: P2)

An editor user animates effect parameters or replays cached content. Beutl reuses structural plans, programs, and warmed intermediate resources while still invalidating the correct output when structure, bounds, scale, or cache identity changes.

**Why this priority**: A fused first frame is not useful if ordinary animation recompiles the pipeline or if global visibility breaks render-cache behavior.

**Independent Test**: Render 100 frames of a structurally constant scene with animated parameters, then make one structural change. Count plan compilations, program creation, target allocation, cache use, and output invalidation.

**Acceptance Scenarios**:

1. **Given** a structurally unchanged scene with animated Shader parameters, **When** 100 frames render, **Then** its structural plan is compiled once and no new program is created after the first frame.
2. **Given** one declared structural change, **When** the next frame renders, **Then** exactly the affected plan is invalidated and recompiled once.
3. **Given** a valid materialized render-cache entry within a globally visible request, **When** the request is partitioned, **Then** the cache forms a correct execution-island boundary and its reusable output is not needlessly recomputed.
4. **Given** stable bounds and structure after target-pool warm-up, **When** subsequent frames render, **Then** no new intermediate target is created.
5. **Given** a cached static prefix followed by an animated compatible tail, **When** post-warm-up frames render, **Then** the prefix records a cache hit and zero executed prefix passes while the tail updates correctly.

---

### User Story 4 - Scales, regions, fallbacks, and boundaries stay correct (Priority: P2)

An editor user changes preview quality, exports at full quality, renders a cropped region, includes 3D content, or runs without the preferred GPU backend. The planner preserves the established scale and bounds contracts and uses safe boundaries or fallback execution where fusion is not valid.

**Why this priority**: Incorrect density, requested-region propagation, or fallback behavior can silently corrupt output even when the common full-frame GPU case looks correct.

**Independent Test**: Render representative scenes at multiple output and working scales, with shifted and empty requested regions, with a 3D-produced input, and on the supported non-preferred backend. Compare bounds, densities, schedules, and images to the fresh baseline.

**Acceptance Scenarios**:

1. **Given** mixed-density inputs and a non-default output scale, **When** the request renders, **Then** the resolved working density, maximum-density ceiling, and per-buffer dimension clamp match feature 003.
2. **Given** a cropped requested output region, **When** a typed operation has a backward bounds contract, **Then** only its required upstream region is requested; an operation without such a contract conservatively requests its full input.
3. **Given** a 3D result consumed by the 2D request, **When** the request is planned, **Then** the 3D result is represented as an explicit opaque/backend boundary and no 2D fusion crosses it.
4. **Given** a supported environment without the preferred GPU path, **When** the same composition including a new Shader operation renders, **Then** the Shader executes through its required unfused path and meets visual parity.

---

### User Story 5 - Maintainers can prove whole-request improvement and safety (Priority: P3)

A renderer maintainer compares the redesign with its current-main baseline using request-wide diagnostics, deterministic scenes, failure injection, and production-representative benchmarks. The evidence accounts for ordinary render-node work as well as effects.

**Why this priority**: Effect-local counters or one-off microbenchmarks can report a win while hiding materializations and synchronization elsewhere in the same request.

**Independent Test**: Run deterministic correctness, failure, and benchmark scenes before and after the behavioral change using persistent production-like renderer state. Reconcile every planned pass and intermediate with complete-request counters.

**Acceptance Scenarios**:

1. **Given** a complete 2D request containing effects, opacity, cache boundaries, and opaque work, **When** diagnostics are captured, **Then** every planned pass, intermediate creation, materialization, synchronization, compilation, and cache event is attributed once at request scope.
2. **Given** an injected failure after one or more resources are acquired, **When** rendering aborts, **Then** all owned resources are reclaimable, cleanup continues after a cleanup fault, and the primary render failure is preserved.
3. **Given** a benchmark comparison, **When** results are reported, **Then** both versions use the same scene, output, warm-up, persistent renderer lifetime, measurement method, and starting-commit provenance.

### Edge Cases

- Empty, zero-area, invalid, non-finite, or dynamically shrinking bounds must not accidentally turn a required operation into an identity result or trigger an invalid allocation.
- A requested output region may be shifted relative to the source, extend outside it, or become empty after clipping; forward output bounds and backward required-input bounds must remain in the correct coordinate space.
- A coordinate-dependent Shader may be offered through the restricted current-pixel form. Validation must reject source or bindings that violate that form; an arbitrary or unverifiable Shader is non-fusible and author declaration alone never permits fusion.
- A compatible Shader run may exceed a backend's shader, sampler, child, or uniform budget; it must split into valid ordered passes without changing output.
- A legacy custom effect, custom render node, unsafe blend/composite, dynamic fan-out, explicit readback, externally owned target, or backend transition may reveal too little information to optimize; it must remain executable as an explicit barrier.
- A backdrop, snapshot, destination-dependent blend, or mask may depend on the exact framebuffer state established by earlier top-level drawables; planning must preserve those read/write dependencies and painter order.
- A geometry operation may require CPU readback, return no output, throw, or attempt to retain a borrowed session or input beyond its callback; synchronization and lifetime behavior must be deterministic.
- `ApplyTo` may throw after recording some work, or an author may try to retain the engine-owned context after it returns; partial recording must not enter the request and use-after-recording must fail deterministically.
- Parameter animation may alter values, bounds, requested regions, or a declared structural choice. Only structural changes may recompile the structural plan, while every change must update output and cache identity correctly.
- Render-cache hits, misses, scale changes, and invalidation may occur inside the same globally planned request; cached materialization must neither conceal required dependencies nor retain deferred frame resources.
- Nested draws, brushes, hit testing, boundary recalculation, cache warm-up, and other auxiliary pulls may occur during or beside frame rendering; their runtime state and counters must not contaminate or reuse an incompatible frame plan.
- A small high-density input may raise the working density of a much larger boundary under feature 003. Fusion must preserve the same density decision and dimension clamp rather than silently substituting the output scale.
- Resource acquisition, recording transfer, plan construction, program creation, execution, or disposal may fail. Every owner must release what it acquired on all completed and exceptional paths while preserving the first primary failure.
- Shader source validation or program creation may fail. The request must surface an explicit render failure, publish no partial cache result, and never substitute an identity operation.
- A preferred GPU backend or device may be unavailable. Correct fallback execution and self-skipping hardware-gated tests must remain possible.

## Requirements *(mandatory)*

### Functional Requirements

#### Complete-request planning and boundaries

- **FR-001**: The system MUST give one planner semantic visibility over the complete ordered 2D request for a target surface, including all contributing top-level drawables, before it executes planner-controlled 2D GPU work or chooses optimization, cache, materialization, or execution-island boundaries.
- **FR-002**: Every 2D contribution encountered by that planner—including clears; geometry, text, image, and video draws; transforms; clips; opacity; masks; blend and layer scopes; filter operations; backdrop and snapshot dependencies; cached results; and custom work—MUST be represented either by declared semantics sufficient for safe reasoning, a cached materialized input, or an explicit opaque operation that preserves existing execution behavior and blocks unsupported optimization.
- **FR-003**: A filter-effect render-node boundary, an effect-group child boundary, or another historical implementation boundary MUST NOT by itself prevent fusion when the operations on both sides are compatible and no cache or semantic dependency requires materialization.
- **FR-004**: The planner MUST discover request dependencies before substituting valid cached subtrees, then partition the request into cache-aware execution islands. A cache hit MUST become a materialized island input whose internal operations do not execute or fuse into changing work, while render-cache reuse, invalidation, scale identity, and output lifetime remain correct.
- **FR-005**: A 3D-produced surface consumed by the 2D request MUST be an explicit opaque/backend boundary with declared bounds, effective density, ordering, invalidation, and synchronization metadata. This feature MUST NOT inspect, reorder, or fuse operations inside the 3D renderer, and a downstream 2D requested region MUST NOT be forwarded into unknown 3D internals.
- **FR-006**: Frame rendering, nested drawing, and auxiliary pulls such as hit testing, boundary recalculation, and cache warm-up MUST use an explicitly identified render purpose so incompatible runtime state, cache decisions, and diagnostics are not shared accidentally.

#### Filter-effect authoring contract

- **FR-007**: `FilterEffect.ApplyTo(FilterEffectContext, Resource)` MUST remain the sole abstract filter-effect authoring entry point, and existing public `FilterEffectContext` operations and the `FilterEffect.Resource.CreateRenderNode()` customization point MUST remain available and preserve their current-main behavior. No source migration to a replacement lifecycle is required; an unknown custom render node executes as an opaque boundary.
- **FR-008**: `FilterEffectContext` MUST expose public Shader and Geometry recording capabilities that are usable by an out-of-tree, non-friend assembly without engine-internal access.
- **FR-009**: Calling Shader or Geometry while `ApplyTo` is recording MUST NOT draw, allocate a render target, compile a program, access a GPU device, flush, synchronize, or perform readback. Recording MUST be transactional per effect: if `ApplyTo` fails, none of that invocation's partial recording may enter the request. Execution MUST occur only after the complete recording has transferred to renderer-owned state, and retaining the engine-owned context after `ApplyTo` MUST NOT be supported.
- **FR-010**: A recorded Shader operation MUST declare enough information to distinguish restricted current-pixel work from coordinate-dependent or whole-source work, bind runtime parameters and child inputs, define output and required-input bounds, and identify structure independently of animated values. The public Shader contract MUST define input and output working color space, alpha representation, coordinate origin and units, execution-time bounds and density, and child/sampler coordinate and density behavior. Fusion eligibility MUST be established by a restricted current-pixel form whose source and bindings the engine validates; an arbitrary shader or author assertion alone MUST remain non-fusible. Final bounds, density, and device-size-dependent values MUST be bound at execution time rather than assumed final during `ApplyTo`.
- **FR-011**: A recorded Geometry operation MUST describe single-input/single-output 2D drawing, declare forward output bounds, backward required-input bounds or a conservative full-input requirement, whether CPU readback is required, and any structural identity. Its callback MUST execute later through an engine-owned session whose inputs and drawing surface are borrowed only for the callback duration. Readback MUST form an explicit synchronization barrier, and fan-out or dynamic output topology MUST remain on the opaque compatibility path in this feature.
- **FR-012**: Existing Skia filter, color filter, and custom-effect recordings MUST coexist in authored order with Shader and Geometry recordings. They MUST lower to declared semantics where equivalence is sound and otherwise execute through an explicit opaque compatibility boundary.
- **FR-013**: Ownership of resources captured by a recorded Shader or Geometry operation MUST transfer exactly once from the disposable recording context to renderer-owned execution state, including clone, child-context, multi-input, lowering-failure, and execution-failure paths.
- **FR-014**: The exact overload set and descriptor type names MAY be finalized during planning, but the public contract MUST provide one canonical composable description for each Shader and Geometry operation and MAY provide convenience overloads only when they preserve the same semantics and lifetime rules.

#### Fusion, visual correctness, bounds, and scale

- **FR-015**: The planner MUST fuse maximal compatible runs of coordinate-invariant Shader stages and participating invariant 2D operations across render-node boundaries. The first required non-effect participant is opacity.
- **FR-016**: Fusion MUST occur only when output equivalence is established. Coordinate-changing work without a proven fold, explicit readback, unsafe blend or composite behavior, dynamic fan-out, externally owned targets, backend transitions, 3D results, and opaque operations MUST split the run predictably.
- **FR-017**: A compatible run that exceeds a backend capability or resource limit MUST be split deterministically into valid ordered passes without dropping stages, changing bounds, or changing visual output.
- **FR-018**: Planned execution MUST preserve authored operation order, painter order across top-level drawables, destination-read dependencies, blend and premultiplied-alpha behavior, working color semantics, hit testing, output bounds, clipping, and source-to-output coordinate mapping.
- **FR-019**: Planned execution MUST preserve feature 003's `OutputScale`, per-operation `EffectiveScale`, supply-driven working scale, `MaxWorkingScale`, scale-1 rounding behavior, and per-buffer dimension clamp for the same inputs.
- **FR-020**: Every non-invariant semantic operation MUST provide sound forward output bounds and backward required-input bounds. When a tighter required-input region cannot be proven, the planner MUST request the complete declared input rather than under-render.
- **FR-021**: Empty output, zero-area output, an intentionally dropped output, and allocation failure MUST remain distinguishable outcomes. None may be silently converted to an identity pass merely because no intermediate was produced.

#### Plans, caching, resources, and failure behavior

- **FR-022**: Structural plan identity MUST exclude runtime-only parameter values and resource contents while including every choice that changes operation order, bindings, bounds behavior, fusion legality, or execution shape. Output-cache identity MUST additionally include parameter/resource versions, bounds, scale, format, and device identity wherever they affect pixels. Identity comparison MUST remain correct under hash collisions.
- **FR-023**: Parameter-only animation of a structurally stable request MUST invalidate affected rendered output while reusing its structural plan and compatible programs. A structural change, device recreation, or incompatible cache transition MUST invalidate exactly the affected cached plans and programs and cause one replacement compilation on the next applicable request.
- **FR-024**: One request-scoped execution owner MUST account for intermediate target acquisition, release, synchronization, backend transition, and failure cleanup within and between execution islands. Synchronization MUST occur only for declared dependencies, backend transitions, or explicit readbacks; a same-backend compatible run MUST NOT introduce hidden per-stage flushes. Components that acquire a resource MUST release it on every success and failure path, and a failed or partial island output MUST NOT be published to a render cache.
- **FR-025**: For a stable workload after warm-up, new intermediate-target creation MUST be zero. Peak live intermediate ownership MUST follow the planned dependency schedule rather than grow linearly with the number of compatible stages.
- **FR-026**: Cleanup MUST continue after an individual cleanup failure and MUST preserve the first primary render or planning exception. Pool and GPU-object release MUST occur on the valid rendering lifetime and thread.
- **FR-027**: Each nested 2D target-surface render MUST receive the same complete-request planning semantics as its own request and MUST inherit the parent request's allocator policy, diagnostics, render purpose, requested bounds, scale policy, and cache policy unless an explicit boundary declares a different value.
- **FR-028**: Allocation-failure outcomes for existing paths MUST match the freshly recorded current-main baseline. Any later normalization between preview and delivery requires a separate explicit behavioral decision and is not part of this feature.
- **FR-029**: Ordinary 2D rendering MUST preserve current-main behavior on supported environments without the preferred GPU backend. Every public Shader description MUST have an unfused execution path on every supported ordinary 2D backend, so the request does not fail solely because fusion is unavailable. Invalid Shader source, invalid bindings, or program-creation failure MUST surface as an explicit render failure, publish no partial output to a cache, and MUST NOT silently become an identity operation. GPU-specific fused execution-shape and performance validation MAY remain hardware-gated.

#### Evidence and observability

- **FR-030**: Request-wide diagnostics MUST report, at minimum, planned and executed GPU passes, fused stage count, execution-island count, intermediate-target creation and reuse, pool misses, peak live intermediates, full-frame materializations, synchronization/backend transitions, structural plan compilations, program creations, cache-island hits and misses, opaque 3D boundaries, and failures. Root or presentation targets owned outside the planner MUST be classified separately.
- **FR-031**: Diagnostic totals MUST cover the complete 2D request, including ordinary render-node, compatibility, cache, and nested work; effect-only counters MUST NOT be used to claim renderer-wide improvement.
- **FR-032**: Before behavioral changes, deterministic scenes, provenance metadata, visual references, request-wide counter baselines, and production-representative benchmarks MUST be captured from this branch's starting commit or independently verified as byte-identical to that starting behavior for each accepted scene. Donor-branch evidence MAY seed the suite, but timings and request-wide counters MUST be remeasured with the production-equivalent lifetime on the new baseline.
- **FR-033**: Automated coverage MUST include public non-friend authoring tests, cross-render-node fusion and barrier tests, parity at multiple scales and requested regions, cache and animation tests, fallback tests, and injected lifetime/failure tests, while retaining all applicable existing feature-003 and engine regression tests.

### Key Entities

- **2D Render Request**: One preview, delivery, nested-draw, or auxiliary request with a declared purpose, output scale, requested region, root output, and complete dependency graph.
- **Semantic Operation**: Ordered 2D work whose bounds, inputs, output behavior, scale behavior, side effects, and fusion properties are known well enough for safe planning.
- **Opaque Boundary**: Existing or external work that remains executable but does not expose sufficient semantics for optimization; it forces materialization or schedule separation where needed.
- **Execution Island**: A dependency-consistent part of the globally visible request that can be planned and executed with one cache, lifetime, and backend policy without hiding dependencies from the request-level planner.
- **Shader Description**: A recorded stage with structural identity, runtime parameters, child inputs, coordinate behavior, bounds behavior, and owned resources.
- **Geometry Description**: Deferred drawing or geometry work with structural identity, bounds behavior, readback declaration, borrowed callback inputs, and engine-owned output lifetime.
- **Bounds Contract**: The pair of a forward output-bounds mapping and a backward required-input mapping, with a conservative complete-input alternative.
- **Structural Plan**: A reusable ordered schedule, fusion partition, execution-island partition, and resource-lifetime shape independent of per-frame parameter values.
- **Runtime Parameter Set**: The current frame's values, resources, bounds, and regions bound to a compatible structural plan without changing its identity.
- **Resource Plan**: The ownership intervals, formats, sizes, reuse opportunities, synchronization points, and release responsibilities for materialized intermediates in a request.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A deterministic scene containing two eligible Shader effects separated by opacity, represented by distinct render nodes, executes in exactly one GPU pass with at most one intermediate target and meets the visual parity thresholds. A filter-effect-group-only chain does not satisfy this criterion.
- **SC-002**: Each required hard-boundary scene splits at the expected boundary with zero illegal cross-boundary fusions, and its output meets the visual parity thresholds.
- **SC-003**: An existing plugin-style effect using `ApplyTo` compiles without source changes and matches its baseline render; a non-friend assembly can author both Shader and Geometry operations using only public API, and its invariant Shader participates in the SC-001 fusion shape.
- **SC-004**: Across 100 parameter-only animated frames, the structural plan compiles exactly once and compatible program creation is zero after frame 1. One declared structural change causes exactly one affected plan recompilation.
- **SC-005**: Stable frames after warm-up create zero new intermediate targets, and peak live intermediates for a 10-stage compatible linear chain are no greater than for a 3-stage chain with the same bounds and dependency shape.
- **SC-006**: Representative effects and mixed render-node chains achieve SSIM at least 0.99, linear-RGB mean absolute error at most 0.02, and alpha mean absolute error at most 0.02 against provenance-verified current-main references at output scale 1.0; multi-scale, shifted-region, empty-region, and fallback cases meet their freshly recorded baseline tolerances, and the existing feature-003 golden suite remains green.
- **SC-007**: On the deterministic cross-boundary color workload, the warmed paired benchmark's 95% confidence interval for the post-feature/pre-feature median frame-time ratio is entirely below 1.0 on the same test system and production-equivalent lifetime. Required control and barrier workloads show no regression beyond the measurement tolerance established and recorded with the baseline; historical donor-branch timing percentages are not acceptance targets.
- **SC-008**: In every injected failure phase, zero planner-owned targets, programs, recorded resources, sessions, or deferred inputs remain unreleased after request teardown; a secondary cleanup fault does not replace the primary failure.
- **SC-009**: Complete-request diagnostics reconcile 100% of planned operations and materializations to exactly one executed, cached, skipped, or failed outcome, while externally owned root and presentation resources are reported separately.
- **SC-010**: All applicable engine, public API contract, no-preferred-GPU, and feature-003 regression tests pass with no change to the freshly recorded allocation-failure behavior.
- **SC-011**: After warming a static-prefix/animated-tail scene, each of 100 animated frames records one reusable prefix-cache hit, zero executed prefix passes, zero prefix plan recompilations, and output matching a fresh uncached render.
- **SC-012**: Every visual-parity workload is demonstrably non-vacuous: disabling its operation under test changes linear RGB or alpha by more than the applicable parity tolerance plus a recorded margin.

## Assumptions

- Feature 003's scale and density contracts on the new branch's current-main starting commit are normative. This feature composes with them rather than redefining them.
- Existing premultiplied-alpha and working-color behavior is part of visual compatibility even if the implementation used to produce it through different pass boundaries.
- Complete-request visibility does not imply one monolithic executable plan. Cache, backend, readback, and opaque boundaries may form multiple execution islands after dependencies have been inspected.
- An explicit opaque fallback is an acceptable representation for an operation that has not yet exposed safe optimization semantics; silently assuming semantics is not acceptable.
- The public capability names Shader and Geometry are fixed by product direction. Exact descriptor and convenience-overload shapes remain a planning decision and must be reviewed as an extensibility surface before implementation.
- Current-main behavior is the compatibility baseline. The abandoned feature-004 branch supplies algorithms, tests, fixtures, and failure knowledge only; no result from it is accepted without provenance review and remeasurement.
- The first cross-node proof uses opacity because it is a common invariant non-effect operation. Additional operations participate only when their equivalence, bounds, and lifetime contracts are explicit.
- Existing code that reads `FilterEffectContext.WorkingScale` during `ApplyTo` retains its current effect-boundary meaning. New Shader and Geometry descriptions bind the final execution density and device-space values later so cache partitioning and buffer clamps cannot make recorded values stale.
- Legacy `CustomEffect` remains the opaque escape hatch for fan-out, dynamic topology, or other capabilities outside the narrow single-input/single-output Geometry contract. It is not a second optimizable geometry pipeline.

### Dependencies

- The existing `FilterEffect`, `FilterEffectContext`, render-node, render-cache, and feature-003 scale contracts on the new branch's starting commit.
- Hardware-gated GPU verification for execution-shape and performance criteria, plus supported fallback tests for environments without the preferred backend.
- A deterministic reference-render and benchmark harness that can run both the unmodified starting commit and the redesigned implementation with equivalent persistent renderer lifetimes.
- Public API contract coverage from a non-friend assembly so plugin-author capabilities are validated independently of engine internals.
