# Phase 0 Research: Declarative Effect Graph with GPU Pass Fusion

**Feature**: `004-gpu-pass-fusion` | **Date**: 2026-07-05 | **Spec**: [spec.md](./spec.md)

This document resolves every open technical choice the spec deferred to planning. Each decision is grounded in the current-pipeline inventory (summarized in §0) taken from the code on `main` (= this branch's base, `eb7d9d9d9`).

## 0. Current-pipeline inventory (baseline facts)

All effect-pipeline code lives in `src/Beutl.Engine/Graphics/FilterEffects/` and `src/Beutl.Engine/Graphics/Rendering/`.

- **Recording**: `FilterEffect.ApplyTo(FilterEffectContext, Resource)` appends `IFEItem`s — `FEItem_Skia<T>` (SKImageFilter factory), `FEItem_SKColorFilter<T>` (SKColorFilter factory), `FEItem_CustomEffect<T>` (imperative callback receiving `CustomFilterEffectContext`). Items appended after bounds become `Rect.Invalid` go to a separate render-time list.
- **Execution**: `FilterEffectActivator.Apply` walks the items. Consecutive Skia items accumulate into `SKImageFilterBuilder` (zero allocations). The first custom item triggers `Flush()`: the accumulated chain is baked into a **freshly allocated** RGBA16F `RenderTarget` (`RenderTarget.Create` → `SKSurface`, Vulkan-texture-backed on the render thread), then the callback runs, typically allocating *another* target (`CustomFilterEffectContext.CreateTarget`) and snapshotting the current one. Note `Flush(force: true)` bakes a new target even with no pending Skia chain, so **adjacent custom items each pay a materialization copy plus their own pass** — the legacy cost is ≥ 1 pass and ≥ 1 allocation per custom item, not exactly 1 (baseline counters must model materializations and effect passes separately). GPU sync (`_surface.Flush(true,true)` + `ITexture2D.PrepareForSampling/PrepareForRender`) happens inside `RenderTarget.PrepareForSampling()/BeginDraw()` — i.e. per materialization, uncoordinated.
- **Entry point**: `FilterEffectRenderNode.Process(RenderNodeContext)` resolves the 003 working scale (`ResolveWorkingScale` + `ClampWorkingScaleToBufferBudget`), builds a fresh `FilterEffectContext`, calls `ApplyTo`, runs the activator — **every frame**, with no compiled/cached representation.
- **Effect census**: 42 `FilterEffect` subclasses — 41 in `Beutl.Engine` (including the 3 script effects, `FilterEffectGroup`, `FallbackFilterEffect`, and the delegating `FilterEffectPresenter`) plus `NodeGraphFilterEffect` in `src/Beutl.NodeGraph/`. ~16 are coordinate-invariant pure color ops (`Gamma`, `Invert`, `Threshold`, `Brightness`, `Saturate`, `HueRotate`, `HighContrast`, `Lighting`, `LumaColor`, `ColorGrading`, `Curves`, `Negaposi`, `ChromaKey`, `ColorKey`, `LutEffect`, color-matrix ops). ~20 change coordinates/bounds. SKSL effects run via `SKSLShader.ApplyToNewTarget` (one target + snapshot each); GLSL effects (`PixelSortEffect`, `GLSLScriptEffect`) run Vulkan pipelines via `GLSLFilterPipeline` (requires `Supports3DRendering`); CPU/composite effects (`FlatShadow`, `StrokeEffect`, `Clipping`, `SplitEffect`, …) draw with `ImmediateCanvas`.
- **Pooling**: none for render targets (only a `float[20]` color-matrix scratch pool). **Counters/benchmarks**: none for the effect pipeline (`tests/Beutl.Benchmarks` has only unrelated micro-benchmarks). **Parity harness**: `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/` (SSIM/MAE over RGBA16F, `ExactSsimMin=0.99`, `ExactMaeMax=0.02`) — directly reusable.
- **Extensibility**: plugins subclass the public `FilterEffect`; the whole imperative surface (`FilterEffectContext`, `CustomFilterEffectContext`, `FilterEffectActivator`, `EffectTarget(s)`, `SKImageFilterBuilder`) is public. In-app script effects: `SKSLScriptEffect`, `GLSLScriptEffect`, `CSharpScriptEffect` (Roslyn; user scripts program against `CustomFilterEffectContext`).

---

## D1 — Authoring model: builder-recorded declarative node graph

**Decision**: Keep the *call shape* of today's API (an effect receives a context object and appends operations in order) but make the recorded items **inspectable node descriptors** instead of executable closures. `FilterEffect.ApplyTo(FilterEffectContext, Resource)` is replaced by `FilterEffect.Describe(EffectGraphBuilder, Resource)`. The builder exposes the same convenience primitives (`Blur(...)`, `ColorMatrix(...)`, `Shader(...)`, …) plus the five node primitives; it produces an `EffectGraph` (a DAG — linear chains in the common case, branching via split/composite). Nodes carry data, bounds contracts, shader sources, and uniform *bindings* — never rendering callbacks with target access (the sole exception is D8's `GeometryNode`).

**Rationale**: 41 built-in effects and an unknown number of plugin effects are written in the append-style idiom; preserving the shape makes migration mechanical (most effects change only the method signature and swap `context.CustomEffect(...)` for a typed node). Inspectability is the property the compiler needs — what today is an opaque `Action<T, CustomFilterEffectContext>` becomes data the compiler can classify, fuse, and schedule.

**Alternatives considered**:
- *Effects return a node tree* (pure functional construction): cleaner in isolation, but every effect and every `FilterEffectGroup`/`NodeGraphFilterEffect` composition site must be restructured, and the append idiom (bounds threading through a chain) would have to be reinvented by every author. Rejected for migration cost with no compiler-side benefit.
- *Keep `ApplyTo`'s name*: rejected — the semantics change from "apply now" to "describe"; a new name makes silent misuse (expecting immediate targets) a compile error rather than a runtime surprise, which is the point of a breaking release.

## D2 — Fusion execution technology: Skia shader composition, SKSL snippet merging on top

**Decision**: A fusion group (a maximal run of adjacent coordinate-invariant nodes) executes as **one Skia draw into one target**, built by *shader composition*: start from the input's image shader, wrap runs of color-filter nodes with `SKShader.WithColorFilter(...)`, and wrap runs of SKSL snippet nodes with `SKRuntimeEffect` shaders that take the accumulated shader as their `src` child. Nested runtime shaders compose into a single fragment program inside Skia, so arbitrary interleavings of SKSL nodes and color-filter nodes still collapse to one pass. As an optimization (not a correctness requirement), the compiler additionally **merges adjacent SKSL snippets into one generated SKSL program** (each snippet contributes a `half4 feN_apply(half4 c)` function with prefixed uniforms; the merged `main` chains them) to reduce shader-stage nesting depth and program count.

The GLSL/Vulkan pipeline path (`GLSLFilterPipeline`) is retained for `ComputeNode`s (PixelSort, `GLSLScriptEffect`) — those are scheduled as standalone passes, never fused.

**Rationale**:
- Skia executes the composed shader tree in one render pass / one draw call — exactly SC-001's "single GPU pass".
- It runs identically on the raster (CPU/SwiftShader) backend, satisfying FR-014 with **zero extra fallback code** — the same plan executes everywhere Skia does.
- The existing SKSL color effects already have SKSL sources with a `src` child; converting them to snippet form is a local rewrite, and their math is unchanged (parity via the golden thresholds).
- Color-matrix-based ops (`Saturate`, `HueRotate`, `Brightness`, …) stay as `SKColorFilter`s (Skia's own composed color-filter path), avoiding a risky reimplementation of Skia's color-matrix/`HighContrast`/`Lighting` semantics in SKSL.

**Alternatives considered**:
- *Fuse via a single generated GLSL/Vulkan pass*: maximal control, but requires a separate CPU fallback implementation for every fused chain (FR-014), duplicates all color math, and puts Skia-surface↔Vulkan transitions *inside* the hot path. Rejected.
- *Rewrite color-filter effects as SKSL snippets for a single merged program always*: rejected for parity risk (Skia's `SKHighContrastFilter`/lighting math is nontrivial); revisit per-effect later if profiling shows nesting depth matters.
- *Rely on Skia to fuse `SKColorFilter` chains only*: insufficient — the expensive half of the problem is the custom SKSL effects, which today each cost a target + snapshot.

## D3 — Structure/parameter separation and plan caching

**Decision**: The effect graph is (re)described **every frame** — it is cheap descriptor construction — and hashed structurally. The **structural key** covers: node kind sequence/topology, shader source identity (reference or content hash), color-filter *factory* identity, child counts, and any value the node declares as **structural** (e.g. a pass count that changes the pass schedule, a bounds-mode flag). Uniform values, matrices, colors, and texture *contents* are excluded. `FilterEffectRenderNode` holds the cached `CompiledPlan` keyed by that structural key (plus graphics-context identity — nothing else). **Bounds, ROIs, buffer sizes, and the resolved working scale are deliberately NOT part of the key**: many effects derive bounds from animated parameters (Blur inflates by `sigma × 3`, `StrokeEffect` by `Pen`/`Offset`), so sizes change on parameter-only frames. Values that change *topology* are the opposite: `SplitEffect`'s division counts, compute pass counts, and branch counts are **structural** — animating them recompiles once per change (FR-010's structural-threshold rule), never a stale-cache reuse. The compiled plan therefore caches only the *structural* artifacts (pass schedule, programs, resource-plan shape: formats + lifetime intervals + peak concurrency), and a cheap per-frame **resource resolution** pass (pure `Rect` math over the described bounds) computes concrete sizes and ROIs that feed the pool. A cache hit means the frame writes parameter blocks (uniforms, color-filter instances, sampler bindings), re-resolves sizes, and executes — no compilation, no program creation (SC-002 intact). A global **program cache** (keyed by merged-SKSL source hash) reuses `SKRuntimeEffect` instances across plans and across identical fusion groups anywhere in the scene.

**Rationale**: Describing is O(nodes) with small allocations; diffing a structural key is far simpler and more robust than a two-phase "build once, bind later" API, which would force every effect author to correctly split construction from binding (a new class of authoring bugs). The existing `EngineObject`/`Resource` capture-and-version pattern already distinguishes "parameters changed" cheaply; `FilterEffectRenderNode.Update` keeps feeding `HasChanges` for the render-graph diff, while the plan cache adds the finer distinction "changed structurally vs parametrically". Skia caches GPU programs internally, but re-creating `SKRuntimeEffect` objects re-parses SKSL — the program cache avoids that (SC-002's "shader-program creation occurs zero times after frame 1").

**Alternatives considered**:
- *Two-phase authoring API (structure once, parameters per frame)*: stronger static guarantee, but doubles the authoring surface and breaks the append idiom; animated Beutl properties arrive via `Resource` re-capture anyway, so a per-frame describe is unavoidable without deep `EngineObject` changes. Rejected.
- *Cache on the `RenderNodeCache` layer instead*: that cache stores rendered pixels for static subtrees; the plan cache addresses the orthogonal case "parameters animate, structure doesn't". Both coexist (spec assumption).
- *Hash uniform values too (full memoization)*: would make every animated frame a miss; pointless.

## D4 — Render-target pool

**Decision**: A `RenderTargetPool` owned by the shared graphics context (render-thread affine, like `RenderTarget.Create` today). Buckets are keyed by exact `(width, height, format, resource kind)` for RGBA16F render targets and generic surface-less raw textures. Acquire returns a cleared render target (raw texture contents remain undefined); release returns it to the matching bucket. **Ownership is a lease**: consumers hold ref-counted `ShallowCopy` handles (`SKSurfaceCounter`), so return-to-pool is hooked into the underlying `RenderTarget`'s *last* ref-count release — never into an individual wrapper's `Dispose` (an `EffectTarget`-style wrapper disposing its shallow copy while other copies live must not return the surface). Today the counter *disposes* the surface on last release and is private; the pool substitutes a pool-aware deallocator that returns surface+texture to the bucket atomically, with a generation tag so no stale shallow copy can ever observe a reissued target. Fullscreen GLSL compute nodes use color-only render passes because their pipeline disables depth testing and depth writes; they do not acquire an unused raw depth texture.

**Allocation-failure normalization**: the pool applies one uniform rule — preview drops the pass output and continues; delivery throws. Legacy behavior was path-dependent (`Flush` drop-or-throw vs `CustomFilterEffectContext.CreateTarget` returning an *empty* target whose `Open` throws unconditionally); reproducing that divergence buys nothing, so FR-015 declares the normalization an accepted, tested behavioral change. Eviction: targets unused for N frames (N≈8) are disposed at frame boundaries, plus a total-bytes soft cap with LRU eviction. The pool is the **only** allocation path for effect intermediates (the executor and `GeometryNode` sessions acquire through it); `RenderTarget.Create` remains for non-effect surfaces. Pool hits/misses/evictions feed the D9 counters. Allocation failure inside the pool surfaces exactly like today (preview: drop target and continue; export/delivery: throw), preserving FR-015.

**Rationale**: Exact-size bucketing is predictable and keeps shader resolution uniforms equal to target size (padding/viewport tricks would ripple into every shader's coordinate math and the 003 density bookkeeping). Steady-state scenes reuse identical sizes, which is precisely SC-003's gate. Frame-based eviction bounds memory without a background thread.

**Alternatives considered**:
- *Size-class (rounded-up) bucketing + viewport rendering*: fewer misses under animated bounds, but every pass must then distinguish "target size" from "content size", which touches all shaders, blits, snapshots, and the 003 dimension clamp. Deferred as a follow-up optimization; the pool interface (acquire by size) does not preclude it.
- *No eviction (grow-only)*: unbounded GPU memory across scene switches. Rejected.
- *Allocate a depth attachment for every fullscreen GLSL pass*: rejected — `PipelineOptions.Fullscreen` disables depth testing and depth writes, so the attachment has no observable effect and only consumes memory and attachment bookkeeping.

**Known limitation (recorded)**: bounds that change every frame (an animated blur radius inflating bounds) change bucket sizes every frame and will miss the pool; SC-003's gate is scoped to structurally-and-size-stable steady state. The size-class follow-up addresses this.

## D5 — Executor and centralized synchronization

**Decision**: A single `PlanExecutor` runs a `CompiledPlan` against the graphics context. Passes execute in schedule order; each pass declares its **backend** (Skia or Vulkan). The executor performs synchronization **only at backend transitions**: before a Vulkan pass samples a Skia-drawn target it calls the flush + `PrepareForSampling` pair once; before Skia draws a Vulkan-written texture it calls `PrepareForRender` once. Within a run of same-backend passes there are no flushes. Nodes never sync; `GeometryNode` sessions get a canvas whose lifecycle the executor brackets. Full-frame snapshots (`SKImageFilter.CreateImage`-style CPU readbacks) are eliminated from the pass path — inputs are bound as textures/image shaders.

**Rationale**: This is FR-008 verbatim; the mechanism (surface flush + texture layout transitions) already exists in `RenderTarget` — the change is *who calls it and how often*, moving from per-materialization to per-transition. Counting these calls is also how the flush counter (FR-017) is implemented.

**Alternatives considered**: an implicit lazy-sync layer inside `RenderTarget` (sync-on-first-sample) — simpler call sites but hides ordering from the plan, makes the flush counter ambiguous, and keeps the "every effect pays a sync" failure mode possible. Rejected in favor of explicit plan-driven sync.

## D6 — ROI propagation

**Decision**: Every node declares two bounds functions: `TransformBounds(input) → output` (forward, drives layout exactly as today's `transformBounds` lambdas do) and `GetRequiredInputBounds(outputROI) → inputROI` (backward). The compiler runs a backward pass from the requested output region (the render node's requested bounds) and stores a device-space ROI per pass; the resource plan sizes intermediates to the ROI, not the full frame. Defaults: coordinate-invariant nodes are identity in both directions; nodes that cannot answer (render-time bounds, `Rect.Invalid` today) return "unbounded", which the compiler resolves to the full input bounds — safe degradation per the spec edge case. ROIs are computed in logical space and converted to device size with the pass's resolved working scale, then clamped by the 003 per-axis budget (`ClampWorkingScaleToBufferBudget` semantics unchanged, FR-012).

**Working-scale carry (legacy parity)**: today `FilterEffectActivator.Flush` re-clamps per target **and mutates the activator's `WorkingScale` downward**, so ops after a clamped materialization inherit the reduced `w`; `CustomFilterEffectContext.CreateTarget` re-clamps per allocation *without* carrying. The per-frame resource resolution reproduces exactly this: passes resolve in schedule order with a monotonically non-increasing carried `w` per chain (each materializing pass's clamp propagates forward), while intra-pass allocations (ping-pong, geometry-session targets) re-clamp locally without affecting the carry. Parity tests must include a chain whose inflated bounds trigger the 16 384 px clamp.

**Rationale**: The two-function contract is the minimal complete one (forward for layout, backward for ROI); it mirrors what Skia's image-filter system does internally (`computeFastBounds` / required-subset) and what the spec's FR-011 demands. Doing ROI at compile time (not per pass at execute time) keeps the executor branch-free and lets the resource plan reflect real sizes.

**Alternatives considered**: execute-time pull-based ROI (each pass asks its input lazily) — more precise for data-dependent ROIs but makes the resource plan unknowable before execution, defeating FR-007. Rejected for v1; the contract leaves room for a node to *narrow* its declared ROI later.

## D7 — Node taxonomy and the migration map

**Decision**: **Seven concrete descriptor kinds realizing the spec's five primitives** (the canonical taxonomy for all 004 documents): shader → `ShaderNode` + `ColorFilterNode`; geometry → `SkiaFilterNode` + `GeometryNode`; compute → `ComputeNode`; split → `SplitNode`; composite → `CompositeNode`:

| Primitive | Payload | Fusion | Backend |
|---|---|---|---|
| `ShaderNode` | SKSL snippet (`half4 apply(half4 c)` + optional extra samplers/uniforms) or whole-source runtime shader; `IsCoordinateInvariant` flag | Fusable when coordinate-invariant | Skia |
| `ColorFilterNode` | `SKColorFilter` factory + data | Always fusable (coordinate-invariant by construction) | Skia |
| `SkiaFilterNode` | `SKImageFilter` factory + forward/backward bounds | Groups with adjacent `SkiaFilterNode`s into one filtered draw (today's builder behavior, now an explicit plan pass) | Skia |
| `ComputeNode` | GLSL sources, pass count, ping-pong color declaration, push-constant layout | Never fused; scheduled pass(es) | Vulkan (Skia-composited fallback: none — requires `Supports3DRendering`, else the node's declared CPU/identity fallback applies, as today) |
| `GeometryNode` | Canvas-drawing session callback + explicit bounds contract | Never fused; opaque pass | Skia |
| `SplitNode` / `CompositeNode` | Fan-out count / fan-in composite op (blend mode, offsets) | Fusion never crosses them | Skia |

Migration map for the 41 built-ins (+ scripts + node graph):

- **→ `ColorFilterNode`**: `Saturate`, `HueRotate`, `Brightness`, `HighContrast`, `Lighting`, `LumaColor`; the `ColorMatrix`-style context conveniences carry over as builder conveniences producing this node. (`BlendEffect` is **not** here — see GeometryNode: the real effect is brush-based; only the old context's *color* convenience overload appended an `SKColorFilter`.)
- **→ `ShaderNode` (snippet, coordinate-invariant, fusable)**: `Gamma`, `Invert`, `Threshold`, `ColorGrading`, `Curves`, `Negaposi`, `ChromaKey`, `ColorKey`, `LutEffect` (1D/3D, with LUT texture as sampler), `SKSLScriptEffect` (declared invariant only when the script opts in; default non-invariant whole-source shader).
- **→ `ShaderNode` (whole-source, non-invariant)**: `MosaicEffect`, `ColorShift`, `DisplacementMapTransform`.
- **→ `SkiaFilterNode`**: `Blur`, `DropShadow(Only)`, `Dilate`, `Erode`, `TransformEffect` (matrix path), `InnerShadow(Only)` (recomposed from blur+composite primitives). `MatrixConvolution` and `Transform` are context *conveniences*, not effect subclasses — they carry over as builder conveniences producing this node.
- **→ `ComputeNode`**: `PixelSortEffect` (3-shader multi-pass), `GLSLScriptEffect`.
- **→ `GeometryNode`**: `FlatShadow`, `StrokeEffect`, `Clipping`, `LayerEffect`, `DelayAnimationEffect`, `ShakeEffect`, `PathFollowEffect`, `DisplacementMapEffect` (mask composition part), `TransformEffect` (custom path), `BlendEffect` (brush-based via `BrushConstructor`; MAY lower to a `ColorFilterNode` as an optimization when the brush is structurally a solid color). *(Superseded for `CSharpScriptEffect`: originally planned as a fusion-ineligible compat session; post-removal its scripts author the full declarative vocabulary via `Builder` globals — including fusable color nodes — with `GeometrySession` available only inside `Builder.Geometry(session => ...)`.)*
- **→ `SplitNode`/`CompositeNode`**: `SplitEffect` (division counts are **structural**), `PartsSplitEffect` (split side — its output count is contour-discovered at execution time, so it uses the **dynamic-outputs** contract: the pass is marked dynamic, the executor allocates its outputs from the pool at runtime, counted and leak-checked, exempt from the static peak-live bound), `InnerShadow`/`DropShadowOnly`-style composites where applicable.
- **Meta**: `FilterEffectGroup` concatenates children's descriptions into one graph (as today, one context); `FallbackFilterEffect` describes an identity graph; `FilterEffectPresenter` delegates — it describes whatever its target's current effect describes. `NodeGraphFilterEffect` stays a **render-node boundary** on the 003 seam now exposed as `RenderNodeFactory` (its legacy `ApplyTo` already throws today): `NodeGraphFilterEffectRenderNode` keeps evaluating the node graph and processing child `RenderNode` outputs, while the `FilterEffect`s *inside* the graph migrate individually — it never flows through `EffectGraphBuilder`.

**Rationale**: Every current behavior has a home; the fusable set matches the spec's FR-004 list exactly; `GeometryNode` is the honest representation of genuinely imperative composite work (it is a *declared, bounded* escape hatch — the executor still owns its target, ROI, pooling, and sync — unlike today's free-form `CustomFilterEffectContext`).

**Alternatives considered**: forcing composite effects (stroke, flat shadow) into shader/compute form — unbounded scope creep for zero fusion benefit (they are coordinate-changing anyway). Rejected; they keep their current internals behind a declared node.

## D8 — `GeometryNode` session surface (the bounded escape hatch)

**Decision**: A `GeometryNode` declares inputs, a bounds contract, and a `Render(GeometrySession)` callback. `GeometrySession` exposes: the pass's resolved working scale and device target (acquired from the pool by the executor), `OpenCanvas()` (an `ImmediateCanvas` bracketed by the executor), and read-only texture views of its inputs. It does **not** expose target creation, flushing, or arbitrary snapshots — multi-target needs are expressed as multiple nodes. `CSharpScriptEffect`'s script globals were initially rebuilt on this session type *(later superseded: script globals moved to `Builder` — an `EffectGraphBuilder` — so scripts author declarative nodes directly and reach a `GeometrySession` only through `Builder.Geometry(session => ...)`; breaking for user scripts, migration documented per FR-016)*.

**Rationale**: Preserves full expressiveness (canvas + inputs) while moving every resource/sync decision to the executor — the property FR-008 needs. Making the escape hatch *narrow* is what allows the counters to stay truthful (a GeometryNode cannot allocate untracked targets).

**Alternatives considered**: keeping `CustomFilterEffectContext` as the session type — it leaks `CreateTarget`/`Open`/target-list mutation, which reintroduces uncounted allocations and per-effect sync; rejected (and FR-016 mandates its removal).

## D9 — Observability: counters and benchmarks

**Decision**: A `PipelineDiagnostics` instance owned by each renderer/processor (not global state): plain `long` fields incremented on the render thread — `GpuPasses`, `TargetAllocations`, `PoolAcquires`, `PoolMisses`, `FullFrameMaterializations`, `FlushSyncs`, `PlanCompilations`, `ProgramCreations` — with a `Snapshot()` struct for assertions. Always-on increments (a field increment is negligible); "not observed" costs nothing beyond that. Wired first into the *current* pipeline (activator `Flush`, `RenderTarget.Create` on the effect path, `PrepareForSampling`) so the baseline is measured before any behavior change (FR-017/FR-020 step 1). Benchmarks: a new `tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs` (BenchmarkDotNet, excluded from `dotnet test` as today) with the four spec scenes (pure color chain, mixed chain, split/composite tree, high-res source), reporting counters + frame time; counter *assertions* live in NUnit tests next to the golden suite so CI enforces SC-001/002/003 without running BenchmarkDotNet.

**Rationale**: Per-renderer instances avoid cross-test interference and thread ambiguity; NUnit-side assertions make the success criteria CI-enforceable; BenchmarkDotNet gives the human-readable before/after report (FR-018).

**Alternatives considered**: `System.Diagnostics.Metrics`/EventCounters — right for production telemetry, wrong for deterministic test assertions (async publication); can be layered later.

## D10 — Rollout and removal

**Decision**: Follow FR-020's six steps as separate PR-sized stages on this branch, each keeping `dotnet build` + full tests green:

1. **Counters + benchmarks** on the *existing* pipeline (no behavior change); record baseline numbers into `docs/specs/004-gpu-pass-fusion/notes/baseline.md`.
2. **Pool introduced under the existing pipeline** (activator `Flush`/`CreateTarget` acquire from the pool) — behavior-identical, golden suite proves it; counters show allocation drop.
3. **Graph model + compiler + executor** land alongside the old path; `ShaderNode`/`ColorFilterNode` primitives; migrate the 15 color-effect classes; `FilterEffectRenderNode` routes graphs through the new executor. Remaining effects route via a bridge that wraps their legacy item list as a single **`OpaqueLegacyPass`, executed by the retained legacy activator machinery internally** — the old activator/custom-context stay alive (internal-only) until step 6, so custom/geometry-style effects run correctly *before* `GeometryPass`/`ComputePass` exist in step 5.
4. **Fusion + plan cache + program cache** (SC-001/SC-002 gates activate).
5. **Spatial/split/composite/compute migration**: `SkiaFilterNode` grouping, `GeometryNode` sessions, `ComputeNode` (PixelSort), split/composite, scripts, `NodeGraphFilterEffect`.
6. **Removal**: delete `FilterEffectActivator`, `CustomFilterEffectContext`, the imperative `FilterEffectContext` surface, `SKImageFilterBuilder` (public), mutable `EffectTarget(s)`; rename/finalize `Describe`; ship as `refactor!` with a `BREAKING CHANGE:` footer and the author migration guide (`contracts/breaking-changes.md`).

**Rationale**: Steps 1–2 de-risk with zero behavior change and produce the baseline SC-005 is measured against; step 3's bridge keeps the tree green while migration proceeds; the removal is last so no shim ever ships to users (AGENTS.md: no `[Obsolete]`, no v2 types).

**Alternatives considered**: big-bang replacement — rejected (41 effects × parity gates is too much risk in one step); permanent dual pipeline — rejected by FR-016 and the design priorities.

## D11 — SC-005 numeric targets

**Decision**: Keep the spec's initial targets (≥ 60% reduction in passes/allocations/flushes on the benchmark color chain; ≥ 20% median frame-time improvement) as provisional until step 1's baseline lands, then pin the final numbers in `notes/baseline.md` and update the spec if the measured baseline shows materially different headroom. The *counter*-based criteria (SC-001/002/003) are exact and not subject to tuning.

**Rationale**: A 5-effect color chain today costs ≥ 5 passes + ≥ 5 allocations + per-effect syncs; fused it costs 1 pass + ≤ 1 intermediate, so ≥ 60% is conservative — but honesty requires measuring, not asserting (the 003 review history shows perf claims must be gated on measured baselines).
