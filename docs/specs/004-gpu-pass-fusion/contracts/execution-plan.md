# Contract: Graph Compilation & Execution

**Feature**: `004-gpu-pass-fusion` | Binding on: the compiler (`EffectGraph` → `CompiledPlan`) and `PlanExecutor`

## C1. Fusion rules (normative)

1. A **fusion group** is a maximal run of adjacent nodes on one edge-chain where every node is coordinate-invariant (`ShaderNode` snippet with `IsCoordinateInvariant`, or `ColorFilterNode`).
2. One fusion group compiles to **one `FusedShaderPass`**: one draw into one target, built by shader composition (input image shader → alternating `WithColorFilter` wraps and `SKRuntimeEffect` child-shader wraps, in node order). Node order is strictly preserved.
3. Adjacent SKSL snippets within a group SHOULD be merged into one generated program (uniforms prefixed `fe{N}_`, chained `apply` calls); merging is an optimization and MUST NOT change ordering or results beyond floating-point tolerance.
4. Fusion groups never cross: a non-invariant node, a `SplitNode`/`CompositeNode`, a backend transition, or a group that would exceed the composition budget (max 16 shader stages or backend uniform limits — the compiler splits into consecutive fused passes, FR-005).
5. A fusion group at the head of a chain samples the group's input exactly once per output pixel; a group's pass ROI equals the intersection of the requested output ROI with the group bounds (identity transforms).
   > **Status note (2026-07):** production currently always requests full bounds — `FilterEffectRenderNode.Process` passes `Rect.Invalid` to `ResolveResources` — so requested-ROI intersection is a tested-but-latent capability (exercised by the `ResolveResources` unit tests, not by production renders). Plumbing a real production ROI is a maintainer-deferred follow-up.

## C2. Skia filter grouping

Adjacent `SkiaFilterNode`s compose into a single `SkiaFilterPass` (one filtered draw), reproducing today's `SKImageFilterBuilder` accumulation as an explicit plan pass. A `ColorFilterNode` adjacent to a Skia filter run MAY fold into the same paint (Skia applies color filters in the same draw) when node order allows; otherwise it joins the neighboring fusion group.

## C3. Resource planning

1. The compiled `ResourcePlan` declares the **structural shape** of every intermediate (`Id`, format, `[FirstUse, LastUse]`): each pass's outputs (one per current operation, read by the next pass) **and** its statically bounded pass-scoped scratch — the materialized input a geometry/compute/split pass bakes, and the C3.3 ping-pong pair plus depth attachment for compute. Peak-live = interval overlap; the executor MUST NOT exceed it, and it measures this at runtime — the pool tracks concurrently live leases and the executor debug-asserts the measured per-execution peak against the declared bound (the FR-007 gate compares measured peaks of a 10-effect vs a 3-effect chain). Concrete device sizes and per-pass ROIs are computed **every frame** by the resource-resolution pass (pure `Rect` math over the freshly described bounds) — parameter-driven bounds (animated blur sigma, stroke pen, split geometry) MUST flow through without recompilation.
   > **Status note (2026-07):** the backward-ROI walk itself is likewise tested-but-latent in production — `FilterEffectRenderNode.Process` requests full bounds (`Rect.Invalid`), see the C1.5 note.
2. Device sizes obey the 003 budget with **legacy carry parity** (FR-012): the working scale is resolved once at the effect boundary (`ResolveWorkingScale`); resource resolution then re-clamps per buffer (`ClampWorkingScaleToBufferBudget`, 16 384 px/axis) in schedule order with a **monotonically non-increasing `w` carried along each chain** — a clamped materializing pass propagates its reduced `w` to downstream passes (today's `FilterEffectActivator.Flush` mutates the activator's `WorkingScale`), while intra-pass allocations (ping-pong, geometry-session targets) re-clamp locally without affecting the carry (today's `CustomFilterEffectContext.CreateTarget`). Same functions, same resulting densities as the pre-redesign pipeline.
3. Ping-pong iteration uses exactly 2 declared color targets (+1 depth if required) regardless of iteration count.
4. Empty resolved ROI ⇒ the executor skips the pass at runtime (spec edge case "zero-size targets"); the plan itself is unchanged. The two skip causes differ: an empty resolved **output** (a shrinking pass, e.g. a fully-closed clip) produces **no** downstream operation — the pass's input is released, matching the legacy `Clipping.Apply` target removal — while an empty **input** to an identity pass passes the already-empty operation through.
5. **Dynamic outputs**: a pass declared `IsDynamicOutputs` (execution-time-resolved output count, e.g. contour-based part splitting) has no static output decls; the executor allocates its outputs from the pool at execution time, releases them within the frame (leak-asserted), and counts them. The FR-007 static peak-live bound covers declared intermediates only.
6. **Topology is structural**: split division counts, compute pass counts, and branch counts are part of the `StructuralKey` — changing them recompiles exactly once (FR-010); they are never treated as size-only parameters.

## C4. Scheduling & synchronization

1. Passes are topologically ordered; same-backend runs are contiguous when the DAG allows.
2. `SyncBefore` is set only where pass *i* reads a resource last written by the other backend. The executor performs: Skia→Vulkan boundary = surface flush + `PrepareForSampling`; Vulkan→Skia = `PrepareForRender`. The `FlushSyncs` counter counts **schedule-level backend transitions** and equals their number in the schedule (test-asserted) — these are the only *executor-owned* sync points. Individual draws inside a plan keep Skia's own per-draw flush semantics (`ImmediateCanvas` / `RenderTarget.PrepareForSampling` behave exactly as they did before 004); the executor neither adds nor elides them. Executor-owned flush elision inside a plan is a recorded follow-up, deferred by maintainer decision.
3. `GeometryPass` canvases are opened/closed by the executor; a session cannot outlive its pass.

## C5. Plan cache invalidation (exhaustive list)

A cached plan is reused iff **both** hold: equal `StructuralKey`, same graphics context (not device-lost). Anything else ⇒ recompile exactly once, old plan's pooled resources released. **Bounds, ROIs, buffer sizes, and the resolved working scale are per-frame resource-resolution inputs, never invalidation triggers** — a parameter change that inflates bounds (blur sigma, stroke pen) MUST hit the cache and only re-resolve sizes. A value that changes topology (split division count, compute pass count, branch count) is structural and MUST miss the cache exactly once (C3.6). Parameter-only changes MUST hit the cache (SC-002: `PlanCompilations == 1` over 100 animated frames — including bounds-animating parameters; `ProgramCreations == 0` after frame 1 given warm `ProgramCache`).

## C6. Fallback execution (no Vulkan)

Plans containing only Skia-backend passes execute unchanged on raster/SwiftShader contexts (fused SKSL runs through Skia's raster pipeline). `ComputePass` applies its declared fallback. CI (software rendering) MUST pass with the same golden thresholds (FR-014).

## C7. Failure semantics

Pool exhaustion / allocation failure during execution, uniformly for every pass kind: preview (`MaxWorkingScale` finite) → drop the pass output, continue, log; delivery (export) → throw `InvalidOperationException` (message parity with today's `ThrowIfDeliveryAllocationFailure`). A thrown pass releases all acquired resources (no pool leaks; leak-asserted in tests).

This is a deliberate **normalization** of path-dependent legacy behavior — today `Flush` drops/throws, but `CustomFilterEffectContext.CreateTarget` returns an *empty* target whose `Open` then throws unconditionally even in preview. Tests MUST cover the replacement of each legacy path (fused, geometry-session, compute) under forced allocation failure.

## C8. Counter semantics (normative definitions)

| Counter | Increment rule |
|---|---|
| `GpuPasses` | each executed draw/dispatch of a `CompiledPass` (a fused group = 1; K compute iterations = K) |
| `TargetAllocations` | each fresh GPU target creation (pool miss or non-pooled) |
| `PoolAcquires` / `PoolMisses` | each pool acquire / acquire that allocated |
| `FullFrameMaterializations` | each bake of an accumulated chain into a target (legacy `Flush` during transition; `SkiaFilterPass`/`GeometryPass` outputs after) |
| `FlushSyncs` | each backend-transition sync pair (C4.2) |
| `PlanCompilations` / `ProgramCreations` | each graph compile / each `SKRuntimeEffect` (or Vulkan pipeline) construction |

Counters are per-renderer, render-thread, always-on; `Snapshot()`/`Reset()` are the test API. These definitions are binding — SC-001/002/003/005 are expressed in them.

## C9. Invariant-run fold into an adjacent composite (normative)

A `FusedShaderPass` composed **entirely** of color-filter stages (`ColorFilterStage`; no `RuntimeShaderStage`) that **immediately precedes** a `CompositePass` folds into that composite: the compiler drops the fused pass and attaches its ordered color-filter factories to the composite (`CompositePass.InputColorFilters`), and the executor applies their composed `SKColorFilter` to **each** branch draw the composite performs.

1. **Correctness basis.** A composite draws each current branch exactly once (C4, fan-in). A color-filter run is coordinate-invariant (identity bounds, per-pixel), so applying it to each branch draw is byte-identical to first baking every branch through the run and then compositing — the same equivalence C2 already grants a `ColorFilterNode` adjacent to a Skia-filter run. The composed filter is applied **before** the blend (Skia's layer-paint color-filter semantics), i.e. `blend(colorFilter(branch), dst)`. The fold rides the blend-mode `SaveLayer` the composite already opens per branch, adding no layers.
2. **Applicability.** A run containing a `RuntimeShaderStage` (an SKSL snippet/whole-source shader) is **not** an `SKColorFilter` and does **not** fold — it stays its own pass. The fold only collapses a pure color-filter run that is *immediately* upstream of the composite; a non-invariant pass (Skia filter, geometry, compute) between the run and the composite blocks it.
3. **Counters (C8).** The fold removes the fused pass's draw and its per-branch intermediate targets: `GpuPasses` and `TargetAllocations` **decrease** (never increase). The composite still counts one `GpuPass`. Structure-determined, so a fold is reproduced identically on a plan-cache hit (the folded factories are per-frame parameters re-extracted by `ParameterBlock`, so an animated fold amount rebinds without a recompile).
4. **Parity.** Because the fold is semantics-preserving, a scene's frozen parity reference (e.g. `chain-SplitTree`) MUST still pass **un-regenerated** after the fold lands.
