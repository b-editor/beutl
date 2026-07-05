# Feature Specification: Declarative Effect Graph with GPU Pass Fusion

**Feature Branch**: `yuto-trd/integrate-gpu-pass`

**Created**: 2026-07-05

**Status**: Draft

**Input**: User description: "GPU パス融合に向けた画像エフェクトパイプラインの再設計 — 命令型の FilterEffectContext / CustomFilterEffectContext API を、実行計画へコンパイルされる宣言的なエフェクトグラフに置き換える(破壊的 API 変更を許容)。座標を変更しないピクセルエフェクトの単一シェーダーパスへの融合、レンダーターゲットのプール、executor による GPU 同期の一元管理、グラフ構造とアニメーションパラメーターの分離によるパイプラインキャッシュ、ROI 伝播、shader/compute/geometry/split/composite ノードプリミティブによる拡張性維持。" (Project #9 item 199376856)

## Overview

Today Beutl's 2D filter-effect pipeline is **imperative**: each `FilterEffect.ApplyTo` records operations into a `FilterEffectContext` op list, and `FilterEffectActivator` replays that list. Consecutive Skia image/color filters accumulate cheaply into one `SKImageFilter` chain, but the moment a **custom effect** appears (any SKSL/GLSL shader effect or CPU-composite effect — roughly 25 of the 41 built-in effects), the activator **flushes**: the accumulated chain is baked into a freshly allocated full-frame RGBA16F render target, the custom effect allocates *another* target for its output, and snapshots and GPU synchronization points are inserted around each step. A chain like *Blur → Gamma → Invert → Threshold → LUT* therefore costs one target allocation and one full-frame pass **per custom effect**, plus repeated Skia↔Vulkan transitions, even though *Gamma → Invert → Threshold → LUT* are all coordinate-invariant per-pixel operations that could execute as a **single fused shader pass** over one input.

The op list is also rebuilt and re-executed **every frame**, even when only an animated parameter (a gamma value, a color) changed and the effect structure is identical — there is no compiled, reusable representation of the chain.

This feature replaces the imperative recording APIs with a **declarative effect graph**: effects *describe* their processing as graph nodes (shader, compute, geometry, split, composite), and a compiler turns the graph into an **execution plan** — a minimal sequence of passes with an explicit resource plan. An executor runs the plan, owning all render-target allocation (from a pool), GPU barriers, flushes, and backend transitions centrally. The graph's **structure** is separated from its **animated parameters**, so a structurally unchanged graph reuses its compiled plan across frames and only updates uniforms. A region-of-interest (ROI) contract propagates required input bounds through the graph so passes render only what downstream consumers need.

Breaking public-API changes are explicitly in scope: the imperative `FilterEffectContext` / `CustomFilterEffectContext` / `FilterEffectActivator` surface and the mutable `EffectTarget` model are removed once all built-in effects are migrated.

## Scope

**In scope (this feature):**

- A declarative effect-graph model that all 2D filter effects describe themselves in, with node primitives for **shader** (fullscreen pixel pass), **compute**, **geometry** (bounds/coordinate transformation), **split** (fan-out), and **composite** (fan-in) work.
- A graph **compiler** producing an execution plan: fused passes, pass ordering, and a resource plan that bounds intermediate allocations.
- **Pass fusion** for adjacent coordinate-invariant per-pixel nodes (the ~16 pure color ops such as Gamma, Invert, Threshold, Brightness, Saturate, HueRotate, HighContrast, ColorGrading, Curves, Negaposi, ChromaKey, ColorKey, LUT, and color-matrix-based filters), including fusing them into one shader program with combined uniforms.
- A **render-target pool** for effect intermediates (ping-pong color targets and depth targets), replacing per-effect fresh allocation.
- An **executor** that centralizes GPU synchronization: Skia surface flushes, Vulkan barriers, and Skia↔Vulkan backend transitions happen where the plan says, not inside individual effects.
- **Structure/parameter separation and plan caching**: the compiled plan is keyed on graph structure; per-frame animation updates only uniform values.
- **ROI propagation**: a `GetRequiredInputBounds`-style contract on every node so the executor renders only the regions downstream passes actually sample.
- **Migration of all built-in effects** (Skia-filter effects, SKSL effects, GLSL/Vulkan effects, CPU-composite effects, split/composite effects, scripted effects) and of `NodeGraphFilterEffect` to the new model.
- **Measurement first**: counters for GPU pass count, texture allocations, full-frame materializations, and flush count, plus representative benchmarks, added *before* behavior changes so every step is quantified.
- **Removal** of `FilterEffectActivator`, `CustomFilterEffectContext`, `FilterEffectContext` (imperative surface), and the mutable `EffectTarget` API, with call sites and plugin-facing documentation migrated in the same change (`refactor!` / `BREAKING CHANGE:`).
- Preservation of the 003 resolution-independent scale model: `OutputScale`, per-op `EffectiveScale`, `ResolveWorkingScale`, `MaxWorkingScale`, and the per-buffer dimension clamp keep their semantics in the new pipeline.

**Out of scope (deliberately):**

- The 3D scene pipeline (`Scene3DRenderNode` internals), audio pipeline, and transitions between clips.
- New user-facing effects or changes to any effect's visual parameters — this is a pipeline redesign, not an effect feature.
- Node-graph *editor UI* changes; `Beutl.NodeGraph`'s UI keeps working against the migrated `NodeGraphFilterEffect`.
- Decoder/media-source changes (proxy workflow) and render-node-tree (non-effect) compositing changes beyond what effect migration requires.
- Multi-GPU or cross-device execution.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Color-grading chains execute as one GPU pass with identical output (Priority: P1)

A video editor stacks coordinate-invariant color effects on a clip — for example *Gamma → Invert → Threshold → ColorGrading → LUT*. Today each of those five effects costs a full-frame render target, a shader dispatch, and surrounding synchronization. After this feature, the chain compiles into a **single fused shader pass** reading one input and writing one output, and the rendered frame is visually identical to today's result. Preview playback and export of effect-heavy scenes get faster without the user changing anything.

**Why this priority**: This is the core deliverable — the pass-fusion payoff that motivates the redesign — and simultaneously the regression anchor: "fused output matches the current renderer" guards every other change.

**Independent Test**: Render a scene with a chain of N ≥ 3 coordinate-invariant color effects; assert via the pipeline counters that exactly one GPU pass and at most one intermediate target were used for the chain, and assert the output matches the pre-redesign render within the golden-image thresholds.

**Acceptance Scenarios**:

1. **Given** a drawable with a chain of coordinate-invariant color effects, **When** the frame is rendered, **Then** the chain executes as one fused shader pass (pass counter = 1 for the chain) and the output meets the golden-image parity thresholds against the pre-redesign renderer.
2. **Given** a mixed chain such as *Blur → Gamma → Invert → DropShadow → LUT*, **When** rendered, **Then** only the coordinate-changing effects (Blur, DropShadow) break fusion; *Gamma → Invert* fuses into one pass and *LUT* runs as its own pass, with pass and allocation counts strictly lower than the per-effect baseline.
3. **Given** any single migrated effect applied alone, **When** rendered at output scale 1.0, **Then** the result meets the existing golden thresholds (SSIM ≥ 0.99, MAE ≤ 0.02 in linear light) against the pre-redesign render.

---

### User Story 2 - Animated parameters do not rebuild the pipeline (Priority: P1)

An editor animates effect parameters over time — a gamma curve easing in, a LUT intensity ramp. The effect *structure* (which effects, in which order, with which connections) does not change between frames. After this feature, the compiled execution plan is built once and reused across frames; per-frame work is limited to writing updated uniform values. When the user *does* change the structure (adds, removes, reorders, or enables/disables an effect), the plan recompiles exactly once.

**Why this priority**: Per-frame plan rebuilds would erase the fusion win during playback — caching is what makes the compiled model pay off frame-over-frame. It also underpins scrubbing responsiveness.

**Independent Test**: Render 100 consecutive frames of a scene whose effect parameters animate but whose structure is constant; assert via counters that plan compilation happened exactly once and subsequent frames performed uniform updates only. Then mutate the structure and assert exactly one recompilation.

**Acceptance Scenarios**:

1. **Given** a structurally constant, parameter-animated effect graph, **When** 100 frames render, **Then** the plan-compilation counter reads 1 and every frame after the first performs no graph compilation or shader-program creation.
2. **Given** a cached plan, **When** the user inserts or removes an effect in the chain, **Then** the plan recompiles exactly once and subsequent frames reuse the new plan.
3. **Given** a cached plan, **When** a parameter animates to any value (including extremes), **Then** the rendered output is identical to compiling the graph fresh at that parameter value.

---

### User Story 3 - Intermediate memory is bounded by the plan, not the effect count (Priority: P2)

A motion designer builds a heavy composite: ten effects on a 4K clip, several of them custom shader effects. Today that costs on the order of one full-frame RGBA16F allocation per custom effect, every frame. After this feature, the compiler's resource plan assigns pooled ping-pong targets, so the number of live intermediate targets during a frame is determined by the plan's peak concurrency (typically 2 per branch), and steady-state playback allocates no new GPU memory at all — targets are recycled from the pool.

**Why this priority**: Allocation churn is the second-largest cost after pass count, and pooling is what makes long-timeline playback and 4K editing stable; but it delivers value only after the plan model (US1) exists.

**Independent Test**: Render a chain of 10 effects; assert via counters that live intermediate targets never exceed the plan's stated peak (independent of the count 10) and that after a warm-up frame, subsequent identical frames perform zero new target allocations.

**Acceptance Scenarios**:

1. **Given** a chain of N custom effects (N ≥ 5), **When** one frame renders, **Then** the peak number of simultaneously live intermediate targets is bounded by the compiled resource plan and does not grow linearly with N.
2. **Given** steady-state playback of a structurally constant scene, **When** frames 2..K render, **Then** the target-allocation counter does not increase (all intermediates come from the pool).
3. **Given** a scene change that shrinks the required target sizes, **When** rendering continues, **Then** pooled targets are reused or evicted by the pool's policy without unbounded growth of GPU memory.

---

### User Story 4 - Plugin and script authors write declarative effects with an ROI contract (Priority: P2)

A plugin author (or a user of the in-app SKSL/GLSL/C# script effects) creates a custom effect. Instead of imperatively allocating targets and drawing into them, they describe their effect as one or more graph nodes — a shader node with uniforms, a compute node, a geometry node that declares its bounds transformation, or split/composite nodes — and declare, per node, which input region is required to produce a requested output region. Their effect then participates in fusion (when eligible), pooling, ROI-limited rendering, and plan caching automatically, and can express things the built-ins did not anticipate (multi-pass ping-pong, masks, multiple inputs).

**Why this priority**: Extensibility is a stated design priority of the project; the redesign must not close the door that `CustomFilterEffectContext` opened, and every *built-in* effect migration exercises this same authoring surface — but it can only be validated once the graph model exists.

**Independent Test**: Implement a representative effect of each node kind (pixel shader, multi-pass compute, geometry/bounds transform, split, composite) against the new public authoring surface only, and assert each renders correctly, reports correct required-input bounds, and — for the coordinate-invariant shader node — fuses with adjacent color nodes.

**Acceptance Scenarios**:

1. **Given** a third-party effect authored as a coordinate-invariant shader node, **When** placed between two built-in color effects, **Then** all three fuse into a single pass.
2. **Given** a node whose required-input-bounds contract declares an inflated region (e.g. a convolution radius), **When** a cropped output region is requested, **Then** upstream passes render only the declared required region rather than the full frame.
3. **Given** the existing in-app script effects (SKSL, GLSL, C# script), **When** a project using them is opened after the redesign, **Then** they render with output parity — via native graph nodes for the shader scripts, and via a defined compatibility node for the imperative C# script surface.

---

### User Story 5 - Pipeline efficiency is measurable before and after (Priority: P3)

An engine developer (or CI) can quantify the pipeline: counters report GPU pass count, intermediate-target allocations, full-frame materializations, and flush/synchronization count per frame, and a benchmark suite renders representative scenes (color-grading chain, mixed Skia/custom chain, split/composite tree, 4K source) and reports these counters plus frame time. The counters land **first**, so every subsequent step of the redesign has a quantified baseline and the completion criteria are verifiable numbers, not impressions.

**Why this priority**: Measurement does not change user-visible behavior by itself, but the rollout order (counters first) de-risks every other story; it is P3 in value yet first in sequence.

**Independent Test**: Run the benchmark suite on the pre-redesign pipeline and assert it produces a per-scene report of the four counters plus timing; the numbers for the baseline scenes match the known per-effect cost model (e.g. pass count ≈ custom-effect count).

**Acceptance Scenarios**:

1. **Given** the instrumented pipeline, **When** any scene renders in a test or benchmark context, **Then** pass count, target allocations, materializations, and flush count are queryable per frame with negligible overhead when disabled.
2. **Given** the benchmark suite, **When** run before and after the redesign on the same scenes, **Then** it produces comparable reports demonstrating the SC-00x reductions.

---

### Edge Cases

- **Render-time bounds**: some effects today defer bounds computation (`Rect.Invalid` + render-time items). The graph must represent nodes whose output bounds are only resolvable at execution time, and ROI propagation must degrade safely (fall back to full input bounds) for them.
- **Fan-out/fan-in**: `SplitEffect` / `PartsSplitEffect` produce multiple targets that later composite. Fusion must not merge across split boundaries, and the resource plan must account for parallel branches.
- **GPU unavailability**: on contexts without Vulkan/3D support (e.g. SwiftShader CI, headless tests), plans must still execute correctly — fused color passes run through the Skia runtime-shader path or an unfused fallback; output parity gates still apply.
- **Allocation failure**: current semantics — preview drops the target and continues, delivery/export raises an error — must be preserved when the pool cannot satisfy a request.
- **Mixed working scales (003 interplay)**: a fused pass spans effects that today each re-resolved their working scale; the fused pass must use the same resolved scale the unfused chain would have produced, including the 16 384 px per-axis buffer clamp.
- **Parameter-dependent structure**: an effect whose *node shape* depends on a parameter value (e.g. iteration count driving multi-pass ping-pong) causes a legitimate recompile when that parameter crosses a structural threshold; the cache key must include such structural inputs so stale plans are never reused.
- **Uniform limits**: fusing many effects into one program can exceed backend uniform/instruction budgets; the compiler must split an over-long fusion group into multiple passes rather than fail.
- **Zero-size / empty targets**: chains that reduce bounds to empty must short-circuit without allocating or dispatching.
- **C# script effects**: `CSharpScriptEffect` exposes an imperative surface to user scripts; it needs a compatibility node that materializes its inputs, with documented (unfused) cost.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Filter effects MUST describe their processing declaratively as effect-graph nodes; effects MUST NOT execute rendering work or allocate render targets during graph construction.
- **FR-002**: The system MUST provide node primitives covering at least: pixel **shader** pass, **compute** pass, **geometry** (bounds/coordinate transformation, including Skia image-filter application), **split** (one input → multiple outputs), and **composite** (multiple inputs → one output). The primitive set MUST be extensible by plugin authors without modifying the engine.
- **FR-003**: A compiler MUST translate an effect graph into an execution plan consisting of ordered passes and an explicit resource plan (which intermediate targets exist, their sizes/formats, and their lifetimes).
- **FR-004**: The compiler MUST fuse adjacent coordinate-invariant per-pixel nodes into a single shader pass with combined uniforms. All built-in pure color effects (Gamma, Invert, Threshold, Brightness, Saturate, HueRotate, HighContrast, Lighting, LumaColor, ColorGrading, Curves, Negaposi, ChromaKey, ColorKey, LUT, and color-matrix-based filters) MUST be fusion-eligible.
- **FR-005**: Fusion MUST stop at nodes that change coordinates or bounds (blur, morphology, transforms, displacement, split, composite) and at backend boundaries; the compiler MUST split fusion groups that would exceed backend shader/uniform budgets into multiple valid passes.
- **FR-006**: Intermediate render targets MUST be acquired from a pool and returned for reuse (including ping-pong color targets and depth targets); per-frame steady-state rendering of a structurally constant scene MUST NOT allocate new targets after warm-up.
- **FR-007**: The number of simultaneously live intermediate targets during a frame MUST be determined by the compiled resource plan (peak concurrency of the pass schedule), not by the number of effects in the chain.
- **FR-008**: A single executor MUST own all GPU synchronization for effect rendering: Skia surface flushes, Vulkan barriers, and Skia↔Vulkan backend transitions occur only at plan-defined pass boundaries; individual effect nodes MUST NOT trigger their own flushes or snapshots.
- **FR-009**: Graph structure and animated parameters MUST be separated: the compiled plan (pass schedule, shader programs, resource plan) is keyed on structure, and per-frame parameter changes update only uniform/parameter blocks. Shader programs MUST be cached and reused across frames and across identical fusion groups.
- **FR-010**: A structurally unchanged graph MUST NOT recompile its plan between frames; a structural change (including parameter changes that cross a declared structural threshold) MUST invalidate exactly the affected plan and trigger exactly one recompilation.
- **FR-011**: Every node MUST declare its bounds contract: output bounds as a function of input bounds, and required input bounds as a function of the requested output region (ROI). The executor MUST render upstream passes only for the union of required regions, falling back to full bounds for nodes that declare render-time-resolved bounds.
- **FR-012**: The new pipeline MUST preserve the 003 resolution-independent scale semantics: per-op `EffectiveScale` propagation, `ResolveWorkingScale` at effect boundaries, the `MaxWorkingScale` global bound, and the per-buffer 16 384 px dimension clamp MUST produce the same resolved densities as the pre-redesign pipeline for the same inputs.
- **FR-013**: All built-in `FilterEffect` subclasses, the in-app script effects (SKSL, GLSL, C# script), and `NodeGraphFilterEffect` MUST be migrated to the declarative model in this feature; the C# script effect MAY use a compatibility materialization node, documented as fusion-ineligible.
- **FR-014**: Rendering MUST remain correct on contexts without Vulkan/3D support: every plan MUST have an execution path via the Skia-only backend (fused where the runtime-shader path allows, unfused otherwise), and CI (software rendering) MUST pass.
- **FR-015**: Pool exhaustion / allocation failure MUST preserve current semantics: preview rendering degrades by dropping the affected target; delivery (export) rendering fails with an explicit error.
- **FR-016**: The imperative public surface — `FilterEffectContext`'s recording API, `CustomFilterEffectContext`, `FilterEffectActivator`, `SKImageFilterBuilder` as a public authoring type, and the mutable `EffectTarget`/`EffectTargets` model — MUST be removed once migration completes, in the same feature, with `refactor!`/`BREAKING CHANGE:` documentation of the migration path for plugin authors. No `[Obsolete]` shims or duplicate "v2" types remain.
- **FR-017**: The pipeline MUST expose counters — GPU pass count, intermediate-target allocations, full-frame materializations, and flush/synchronization count per frame — queryable from tests and benchmarks, with negligible overhead when not observed. Counters MUST land before any behavioral change.
- **FR-018**: A benchmark suite MUST cover representative scenes (pure color chain, mixed Skia/custom chain, split/composite tree, high-resolution source) and report the FR-017 counters plus frame timing, runnable on both the pre- and post-redesign pipeline during the transition.
- **FR-019**: Visual output parity MUST be verified by regression tests: every migrated effect and representative chains meet the existing golden-image thresholds (SSIM ≥ 0.99, MAE ≤ 0.02, linear light) against pre-redesign reference renders at output scale 1.0, and the existing golden test suites continue to pass.
- **FR-020**: The rollout MUST be incremental and behavior-gated in the order: (1) counters/benchmarks, (2) target pool behind unchanged behavior, (3) declarative shader nodes + color-effect migration, (4) fusion + program cache, (5) spatial/split/composite migration, (6) imperative-API removal — each step keeping the full test suite green.

### Key Entities

- **Effect Graph**: the declarative description an effect chain produces — nodes plus connections, with per-node bounds/ROI contracts and parameter bindings. Replaces the recorded op list.
- **Node Primitive**: one of shader / compute / geometry / split / composite; the unit of description. Plugin-extensible.
- **Execution Plan**: the compiled artifact — an ordered pass schedule, fused shader programs, and a resource plan. Cached by structural key.
- **Pass**: one GPU dispatch (fused shader, compute, Skia filter application, or composite blit) with declared inputs, output, and required synchronization.
- **Resource Plan / Target Pool**: the compiler's declaration of intermediate targets (size, format, lifetime) and the runtime pool that satisfies it with recycled RGBA16F color / depth targets.
- **Executor**: runs a plan against a graphics context; the single owner of flushes, barriers, and backend transitions.
- **Parameter Block**: the per-frame animated values (uniforms) bound to a cached plan; the only thing that changes on a structurally stable frame.
- **Pipeline Counters**: the observability surface (passes, allocations, materializations, flushes) used by tests and benchmarks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A chain of N ≥ 3 coordinate-invariant color effects renders in exactly 1 GPU pass with at most 1 intermediate target, where the baseline uses ≥ N passes and ≥ N targets (verified by counters in an automated test).
- **SC-002**: Across 100 structurally constant animated frames, plan compilation occurs exactly once and shader-program creation occurs zero times after frame 1.
- **SC-003**: For a structurally constant scene, steady-state frames perform zero new render-target allocations (pool hit rate 100% after warm-up), and peak live intermediates for a 10-effect chain are no higher than for a 3-effect chain with the same shape.
- **SC-004**: Every migrated built-in effect and the representative mixed chains meet the golden-image parity thresholds (SSIM ≥ 0.99, MAE ≤ 0.02, linear light) against pre-redesign references at output scale 1.0; the full existing engine test suite (including the 003 golden suites) passes throughout the rollout.
- **SC-005**: On the benchmark color-grading chain scene, total GPU passes, intermediate allocations, and flush count each drop by at least 60% versus the pre-redesign baseline, and median frame render time improves measurably (target ≥ 20%).
- **SC-006**: A plugin-style effect authored purely against the new public surface can express each node primitive and (when coordinate-invariant) participates in fusion, demonstrated by tests that reference only public API.
- **SC-007**: After the removal step, no references to the imperative pipeline types remain in the repository, and the breaking change ships with a documented migration path for effect authors.

## Assumptions

- **Breaking changes are pre-approved**: the project item explicitly allows breaking API changes; per repository policy this ships as `refactor!`/`feat!` with a `BREAKING CHANGE:` footer and same-change call-site migration — no compatibility shims.
- **The 003 resolution-independent model is the baseline**: 003 is merged into `main`; this feature preserves its semantics (FR-012) and its golden-image harness is the parity instrument (FR-019). Byte-identity is *not* claimed for migrated shader effects — the established SSIM/MAE thresholds are the gate, since fused shaders may differ in floating-point rounding.
- **The dual GPU stack stays**: Skia (RGBA16F, linear-light surfaces, optionally Vulkan-texture-backed) remains the compositor and Vulkan (Silk.NET) the low-level backend; the redesign reorganizes how work is scheduled across them, not which stacks exist.
- **Fused pass technology is an implementation choice** deferred to `/speckit-plan`: fusion may compile to SKSL runtime shaders, GLSL/SPIR-V pipelines, or both depending on context capabilities, as long as FR-014 (Skia-only fallback) holds.
- **Plugin ecosystem**: effect plugins subclass `FilterEffect` in `Beutl.Engine`; there is no out-of-tree deprecation-window request on record, so the imperative surface is removed without a window (documented in the PR/release notes).
- **`RenderNode`-level caching stays**: the existing `RenderNodeCache` (structural diffing at the render-node layer) is complementary; plan caching operates inside the effect boundary and must not regress it.
- **Working branch**: work proceeds on `yuto-trd/integrate-gpu-pass` (currently identical to `main`); the spec directory number 004 was fixed by the maintainer.
