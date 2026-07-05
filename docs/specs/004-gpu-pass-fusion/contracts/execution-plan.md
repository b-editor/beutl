# Contract: Graph Compilation & Execution

**Feature**: `004-gpu-pass-fusion` | Binding on: the compiler (`EffectGraph` → `CompiledPlan`) and `PlanExecutor`

## C1. Fusion rules (normative)

1. A **fusion group** is a maximal run of adjacent nodes on one edge-chain where every node is coordinate-invariant (`ShaderNode` snippet with `IsCoordinateInvariant`, or `ColorFilterNode`).
2. One fusion group compiles to **one `FusedShaderPass`**: one draw into one target, built by shader composition (input image shader → alternating `WithColorFilter` wraps and `SKRuntimeEffect` child-shader wraps, in node order). Node order is strictly preserved.
3. Adjacent SKSL snippets within a group SHOULD be merged into one generated program (uniforms prefixed `fe{N}_`, chained `apply` calls); merging is an optimization and MUST NOT change ordering or results beyond floating-point tolerance.
4. Fusion groups never cross: a non-invariant node, a `SplitNode`/`CompositeNode`, a backend transition, or a group that would exceed the composition budget (max 16 shader stages or backend uniform limits — the compiler splits into consecutive fused passes, FR-005).
5. A fusion group at the head of a chain samples the group's input exactly once per output pixel; a group's pass ROI equals the intersection of the requested output ROI with the group bounds (identity transforms).

## C2. Skia filter grouping

Adjacent `SkiaFilterNode`s compose into a single `SkiaFilterPass` (one filtered draw), reproducing today's `SKImageFilterBuilder` accumulation as an explicit plan pass. A `ColorFilterNode` adjacent to a Skia filter run MAY fold into the same paint (Skia applies color filters in the same draw) when node order allows; otherwise it joins the neighboring fusion group.

## C3. Resource planning

1. Every intermediate is declared (`Id`, device size from ROI × working scale, format, `[FirstUse, LastUse]`). Peak-live = interval overlap; the executor MUST NOT exceed it (FR-007 test hook).
2. Device sizes obey the 003 budget: working scale is resolved once per effect boundary (`ResolveWorkingScale`) and re-clamped per buffer (`ClampWorkingScaleToBufferBudget`, 16 384 px/axis) — same functions, same results as the pre-redesign pipeline (FR-012).
3. Ping-pong iteration uses exactly 2 declared color targets (+1 depth if required) regardless of iteration count.
4. Empty ROI ⇒ the pass and its exclusive upstream are elided (spec edge case "zero-size targets").

## C4. Scheduling & synchronization

1. Passes are topologically ordered; same-backend runs are contiguous when the DAG allows.
2. `SyncBefore` is set only where pass *i* reads a resource last written by the other backend. The executor performs: Skia→Vulkan boundary = surface flush + `PrepareForSampling`; Vulkan→Skia = `PrepareForRender`. **No other flushes occur during plan execution** — the `FlushSyncs` counter equals the number of backend transitions in the schedule (test-asserted).
3. `GeometryPass` canvases are opened/closed by the executor; a session cannot outlive its pass.

## C5. Plan cache invalidation (exhaustive list)

A cached plan is reused iff **all** hold: equal `StructuralKey`, equal resolved working scale, equal input-bounds signature (sizes that determine intermediate declarations), same graphics context (not device-lost). Anything else ⇒ recompile exactly once, old plan's pooled resources released. Parameter-only changes MUST hit the cache (SC-002: `PlanCompilations == 1` over 100 animated frames; `ProgramCreations == 0` after frame 1 given warm `ProgramCache`).

## C6. Fallback execution (no Vulkan)

Plans containing only Skia-backend passes execute unchanged on raster/SwiftShader contexts (fused SKSL runs through Skia's raster pipeline). `ComputePass` applies its declared fallback. CI (software rendering) MUST pass with the same golden thresholds (FR-014).

## C7. Failure semantics

Pool exhaustion / allocation failure during execution: preview (`MaxWorkingScale` finite) → drop the pass output, continue, log; delivery (export) → throw `InvalidOperationException` (message parity with today's `ThrowIfDeliveryAllocationFailure`). A thrown pass releases all acquired resources (no pool leaks; leak-asserted in tests).

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
