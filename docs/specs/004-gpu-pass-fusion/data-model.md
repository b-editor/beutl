# Data Model: Declarative Effect Graph with GPU Pass Fusion

**Feature**: `004-gpu-pass-fusion` | **Date**: 2026-07-05 | **Sources**: [spec.md](./spec.md), [research.md](./research.md)

Namespaces: new types live in `Beutl.Graphics.Effects` (authoring surface) and `Beutl.Graphics.Rendering` (compilation/execution), matching the current layout. Names below are the plan-level contract; exact accessibility follows the "public authoring surface, internal machinery" rule stated per entity.

## 1. Authoring layer (public)

### `FilterEffect` (existing, changed)

| Member | Change |
|---|---|
| `abstract void ApplyTo(FilterEffectContext, Resource)` | **Removed** |
| `abstract void Describe(EffectGraphBuilder builder, Resource resource)` | **Added** — appends node descriptors; must not render or allocate |
| `Resource.CreateRenderNode()` / `Resource.Push(...)` | Unchanged (003 seam preserved) |

### `EffectGraphBuilder` (public, new — replaces `FilterEffectContext`'s recording role)

- **Fields/state**: current logical `Bounds` (advanced by each appended node's forward bounds), `OriginalBounds`, `OutputScale`, `WorkingScale` (read-only, resolved by the render node per FR-012).
- **Primitive appenders**: `Shader(ShaderNodeDescriptor)`, `ColorFilter(ColorFilterNodeDescriptor)`, `SkiaFilter(SkiaFilterNodeDescriptor)`, `Compute(ComputeNodeDescriptor)`, `Geometry(GeometryNodeDescriptor)`, `Split(int count, ...)`, `Composite(CompositeNodeDescriptor)`.
- **Convenience methods** (same vocabulary as today): `Blur`, `DropShadow`, `ColorMatrix`, `Saturate`, `HueRotate`, `Brightness`, `HighContrast`, `Lighting`, `LumaColor`, `Transform`, `MatrixConvolution`, `Erode`, `Dilate`, `BlendMode`, … — each constructs the corresponding descriptor.
- **Validation**: appending after a `Split` requires addressing a branch; `Composite` arity must match open branches; descriptor payloads are validated on append (non-null sources, finite bounds functions) so errors surface at describe time, not execute time.
- **Output**: `EffectGraph Build()` (called by the render node, not by effects).

### Node descriptors (public, immutable records)

Common shape — every descriptor carries:

| Field | Type | Notes |
|---|---|---|
| `BoundsContract` | see §2 | forward + backward bounds functions |
| `StructuralKey` contribution | implicit | kind + payload identity (see §3) |

Per kind:

- **`ShaderNodeDescriptor`**: `SkslSource source` (snippet or whole-source; identity-hashable), `bool IsCoordinateInvariant`, `UniformBinding[] Uniforms`, `SamplerBinding[] Samplers` (extra textures, e.g. LUT), `ChildBinding[] Children`. Snippet form must define `half4 apply(half4 c)`; whole-source form must define `half4 main(float2 coord)` with a `src` child (today's `SKSLShader` convention).
- **`ColorFilterNodeDescriptor`**: `Func<SKColorFilter>`-style factory + captured data record; always coordinate-invariant.
- **`SkiaFilterNodeDescriptor`**: `SKImageFilter` factory + captured data record; forward/backward bounds functions (backward defaults to forward's inverse-inflation; may return Unbounded).
- **`ComputeNodeDescriptor`**: GLSL source set (or precompiled pipeline handle), `int PassCount` (**structural**), ping-pong flag, depth-texture requirement, push-constant writer, declared fallback behavior when `Supports3DRendering == false` (`Identity` | `Skip` | `CpuCallback`).
- **`GeometryNodeDescriptor`**: `Action<GeometrySession>` callback, explicit `BoundsContract` (mandatory — no default), input count.
- **`SplitNodeDescriptor` / `CompositeNodeDescriptor`**: branch count (**structural**) / composite operation (blend mode, per-input offsets).

### `GeometrySession` (public, new — replaces `CustomFilterEffectContext` for authors)

| Member | Semantics |
|---|---|
| `Bounds`, `OutputScale`, `WorkingScale` | resolved values for this pass |
| `ImmediateCanvas OpenCanvas()` | canvas over the executor-acquired pooled target; executor brackets begin/end + sync |
| `IReadOnlyList<EffectInput> Inputs` | read-only sampled views (image shader / texture) of input passes |
| *(absent)* | no target creation, no flush, no snapshot, no target-list mutation |

### `EffectInput` (public)

Read-only view of a pass input: logical `Bounds`, `EffectiveScale Density`, `SKShader AsShader()`, texture handle for compute passes.

## 2. Bounds & ROI contract

### `BoundsContract` (public, value type)

| Field | Type | Semantics |
|---|---|---|
| `TransformBounds` | `Func<Rect, Rect>` | forward: output logical bounds from input bounds (drives `Bounds` advancement; today's `transformBounds`) |
| `GetRequiredInputBounds` | `Func<Rect, Rect>` | backward: input region needed for a requested output region (FR-011) |
| `IsRenderTimeResolved` | `bool` | true ⇒ forward returns `Rect.Invalid`; compiler falls back to full input bounds for ROI and defers layout (today's render-time items) |

**Validation rules**: for coordinate-invariant nodes both functions must be identity (enforced by construction — invariant descriptors don't accept custom bounds). `GetRequiredInputBounds(roi)` must contain the region actually sampled; violating this is an authoring bug surfaced by a debug-mode assertion pass (executor compares sampled vs declared in tests).

## 3. Compilation layer (internal)

### `EffectGraph`

DAG of `EffectNode { Descriptor, Inputs[], Outputs[] }` built by the builder. Owns nothing GPU-side.

### `StructuralKey`

Hash accumulated over: node kinds + topology (branch structure), shader-source identity hashes, color-filter/image-filter factory identity, structural ints (pass counts, branch counts), coordinate-invariance flags, `BoundsContract` identity for non-invariant nodes. **Excludes** uniform values, colors, matrices, sampler texture contents. Two graphs with equal keys must compile to plans that differ only in parameter blocks (invariant enforced by construction: everything the compiler branches on feeds the key).

### `CompiledPlan`

| Field | Type | Notes |
|---|---|---|
| `Key` | `StructuralKey` + resolved working scale + input-bounds signature | cache identity (D3) |
| `Passes` | `CompiledPass[]` | topologically ordered schedule |
| `Resources` | `ResourcePlan` | intermediate declarations |
| `ParameterSlots` | `ParameterSlot[]` | where each frame's uniform/filter values go |

### `CompiledPass` (one of)

- **`FusedShaderPass`**: composition recipe — ordered stages (`RuntimeShaderStage(SKRuntimeEffect ref, uniform slots)` | `ColorFilterStage(filter slot)`), one input, one output, device ROI. Executes as one draw (D2).
- **`SkiaFilterPass`**: composed `SKImageFilter` chain slot, input, output, ROI.
- **`ComputePass`**: Vulkan pipeline ref, ping-pong/depth resource refs, push-constant slot, iteration count.
- **`GeometryPass`**: session callback ref, inputs, output, ROI.
- **`CompositePass` / split edges**: composite op + input refs.
- Common: `Backend` (Skia | Vulkan), `SyncBefore` flags (computed at schedule time: set only at backend transitions — D5).

### `ResourcePlan`

List of `IntermediateDecl { Id, DevicePixelSize, Format (RGBA16F | Depth32Float), FirstUse, LastUse }`. Peak-live count = max overlap of `[FirstUse, LastUse]` intervals — the FR-007 bound asserted in tests. Ping-pong pairs are two decls with alternating lifetimes.

### `PlanCache` (per `FilterEffectRenderNode`)

Single-entry (current key → plan) — a render node has one structure at a time; history beyond 1 buys nothing. Invalidation: key mismatch on describe, context device-lost, or node dispose (returns pooled resources).

### `ProgramCache` (per graphics context, global across plans)

`Dictionary<SkslSourceHash, SKRuntimeEffect>` + composed-source cache for merged snippets. Never evicted below a small floor (program count is bounded by distinct effect-source combinations); LRU above a cap.

## 4. Execution layer (internal)

### `RenderTargetPool` (per shared graphics context)

| Member | Semantics |
|---|---|
| `Acquire(int w, int h, TextureFormat fmt)` | exact-size bucket pop or fresh allocation (counted as miss) |
| `Release(target)` | return to bucket; contents undefined afterwards |
| `Trim(frameIndex)` | dispose targets unused ≥ N frames; enforce byte soft-cap LRU |
| Failure | propagate current semantics: preview → drop/degrade; delivery → throw (FR-015) |

Invariants: acquire/release strictly render-thread; every executor-acquired target is released by the end of the frame (leak assertion in debug tests); pooled targets are cleared on acquire.

### `PlanExecutor`

Runs passes in order: resolve ROI targets from pool → bind parameter blocks → sync iff `SyncBefore` → draw/dispatch → release inputs whose `LastUse` passed. Emits counters (§5). Owns `GeometrySession` lifecycles.

### `ParameterBlock`

Per-frame values bound into `ParameterSlots`: uniform floats/vectors/matrices, `SKColorFilter` instances, sampler textures, push constants. Written by the describe pass on cache hit; the only per-frame mutable state (FR-009).

## 5. Observability (public read surface)

### `PipelineDiagnostics` (per renderer/processor)

Counters (`long`): `GpuPasses`, `TargetAllocations`, `PoolAcquires`, `PoolMisses`, `FullFrameMaterializations`, `FlushSyncs`, `PlanCompilations`, `ProgramCreations`. `Snapshot()` returns an immutable struct; `Reset()` for test scoping. Always-on field increments (D9).

## 6. State transitions

```text
FilterEffect.Resource (per frame, existing capture)
        │ Describe(builder, resource)          [cheap, every frame]
        ▼
   EffectGraph ──► StructuralKey ──► PlanCache hit? ──yes──► ParameterBlock update
        │                                │ no                      │
        ▼                                ▼                         ▼
   (discarded)                    Compile → CompiledPlan     PlanExecutor.Run
                                         │                        │
                                         └────────────────────────┘
                                    targets ⇄ RenderTargetPool, programs ⇄ ProgramCache
```

Working-scale resolution (`ResolveWorkingScale` → `ClampWorkingScaleToBufferBudget`) happens in `FilterEffectRenderNode.Process` **before** describe, exactly as today (FR-012); its result feeds both the builder and the plan key.

## 7. Removed types (FR-016)

`FilterEffectActivator`, `CustomFilterEffectContext`, `FilterEffectContext` (public recording surface — the *name* may survive internally only if the builder reuses it, but the plan assumes a clean `EffectGraphBuilder`), `SKImageFilterBuilder` (public), `EffectTarget`, `EffectTargets`, `IFEItem`/`FEItem_*`. Replacement mapping: [contracts/breaking-changes.md](./contracts/breaking-changes.md).
