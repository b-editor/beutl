# Data Model: Declarative Effect Graph with GPU Pass Fusion

**Feature**: `004-gpu-pass-fusion` | **Date**: 2026-07-05 | **Sources**: [spec.md](./spec.md), [research.md](./research.md)

Namespaces: new types live in `Beutl.Graphics.Effects` (authoring surface) and `Beutl.Graphics.Rendering` (compilation/execution), matching the current layout. Names below are the plan-level contract; exact accessibility follows the "public authoring surface, internal machinery" rule stated per entity.

Taxonomy (canonical, research D7 plus the completed meta-effect work): **nine sealed descriptor kinds**. Seven realize the spec's five rendering primitives — shader → `ShaderNodeDescriptor` + `ColorFilterNodeDescriptor`; geometry → `SkiaFilterNodeDescriptor` + `GeometryNodeDescriptor`; compute → `ComputeNodeDescriptor`; split → `SplitNodeDescriptor`; composite → `CompositeNodeDescriptor`. `NestedGraphNodeDescriptor` and `CustomRenderNodeDescriptor` are composition boundaries for branch-local graphs and plugin render nodes. The union is deliberately closed; plugin extensibility means composing these public descriptors, not registering an unknown compiler discriminator.

## 1. Authoring layer (public)

### `FilterEffect` (existing, changed)

| Member | Change |
|---|---|
| `abstract void ApplyTo(FilterEffectContext, Resource)` | **Removed** |
| `abstract void Describe(EffectGraphBuilder builder, Resource resource)` | **Added** — appends node descriptors; must not render or allocate |
| `Resource.PlanRenderNodeFactory` / `Resource.Push(...)` | The normal route creates a `PlanFilterEffectRenderNode`; a retained non-default `PlanFilterEffectRenderNodeFactory` customizes narrow plan policy while preserving compiler, ROI, pooling, and caches. Fully opaque execution uses `CustomRenderNodeFilterEffect.Resource.RenderNodeFactory`. |

### `EffectGraphBuilder` (public, new — replaces `FilterEffectContext`'s recording role)

- **Fields/state**: current logical `Bounds` (advanced by each appended node's forward bounds), `OriginalBounds`, `OutputScale`, `WorkingScale` (read-only, resolved by the render node per FR-012).
- **Primitive appenders**: `Shader(ShaderNodeDescriptor)`, `ColorFilter(ColorFilterNodeDescriptor)`, `SkiaFilter(SkiaFilterNodeDescriptor)`, `Compute(ComputeNodeDescriptor)`, `Geometry(GeometryNodeDescriptor)`, `Split(SplitNodeDescriptor)`, `Composite(CompositeNodeDescriptor)`, and `NestedGraph(NestedGraphNodeDescriptor)`. Containers call `Effect(FilterEffect.Resource)`, which either describes the child through the default plan path or appends a `CustomRenderNodeDescriptor` from the child's captured factory.
- **Convenience methods** (same vocabulary as today): `Blur`, `DropShadow`, `ColorMatrix`, `Saturate`, `HueRotate`, `Brightness`, `HighContrast`, `Lighting`, `LumaColor`, `Transform`, `MatrixConvolution`, `Erode`, `Dilate`, `BlendMode`, … — each constructs the corresponding descriptor.
- **Validation**: descriptor factories and appenders reject invalid payloads (null descriptors/callbacks, non-positive static branch counts, invalid bounds contracts). The builder models the current branch set implicitly: nodes after a split map over every current branch, and `Composite` consumes that runtime set; there is no branch-addressing API or describe-time composite-arity argument. A static split's callback is runtime-checked to emit exactly its declared count.
- **Output**: `EffectGraph Build()` (called by the render node, not by effects).

### Node descriptors (public, immutable records)

Common shape — every descriptor carries:

| Field | Type | Notes |
|---|---|---|
| `BoundsContract` | see §2 | forward + backward bounds functions |
| `StructuralKey` contribution | implicit | kind + payload identity (see §3) |

Per kind:

- **`ShaderNodeDescriptor`**: `SkslSource source` (snippet or whole-source; identity-hashable), `bool IsCoordinateInvariant`, `UniformBinding[] Uniforms`, `ChildBinding[] Children` (every named child shader bound beyond the implicit `src`: a LUT/curve *sampler* — an eager, invariance-safe value lookup — or a whole-source shader's displacement map; there is one binding type, not a separate `SamplerBinding`). Snippet form must define `half4 apply(half4 c)`; whole-source form must define `half4 main(float2 coord)` with a `src` child (today's `SKSLShader` convention). **Color/alpha contract**: shaders receive and return **premultiplied-alpha, linear-light** `half4` (the working surface is RGBA16F / `SrgbLinear` / `Premul`); a snippet needing straight alpha unpremultiplies and re-premultiplies internally, exactly as today's `Gamma`/`Curves`/`LutEffect` SKSL does. Fused composition never changes representation between stages.
- **`ColorFilterNodeDescriptor`**: `Func<SKColorFilter>`-style factory + captured data record; always coordinate-invariant.
- **`SkiaFilterNodeDescriptor`**: `SKImageFilter` factory + captured data record; forward/backward bounds functions (backward defaults to forward's inverse-inflation; may return Unbounded).
- **`ComputeNodeDescriptor`**: dispatch callback, exact successful-dispatch `int PassCount`, mandatory authored `BoundsContract`, exact maximum concurrent color scratch count, closed `ComputeFallbackPolicy` (`Identity`, `Skip`, or `Cpu(callback, requiresReadback)`), and a separate ordinary-dispatch failure policy. Invalid callback/readback combinations cannot be represented. Its `IComputeContext` exposes source logical bounds/density (`SourceBounds`, `SourceScale`) independently from destination logical bounds/density (`TargetBounds`, `WorkingScale`); `Width`/`Height` describe the destination device buffer. The CPU fallback receives the same split contract through `GeometrySession.Inputs[0]` and the session output members.
- **`GeometryNodeDescriptor`**: `Action<GeometrySession>` callback, explicit `BoundsContract` (mandatory — no default), and an explicit CPU-readback requirement.
- **`SplitNodeDescriptor` / `CompositeNodeDescriptor`**: `Static(callback, branchCount, token)` declares an exact structural branch count; `Dynamic(callback, token)` resolves the count at execution and omits it from the key. The composite operation carries its blend mode and optional per-input offsets; it consumes the current runtime branch set rather than declaring an arity.
- **`CustomRenderNodeDescriptor`**: a planned boundary for either a non-default plan factory or a dedicated fully opaque `CustomRenderNodeFilterEffect.Resource`. Structural identity uses the node type plus the resource's collision-free stable identity; the captured factory is rebound per frame, while top-level render-tree reuse additionally requires the exact same factory instance. Bounds are `FullFrame`, never coordinate-invariant. The public factory accepts only the dedicated opaque resource type, preventing ordinary effects from accidentally opting out of the standard plan.

### `GeometrySession` (public, new — replaces `CustomFilterEffectContext` for authors)

| Member | Semantics |
|---|---|
| `Bounds`, `OutputScale`, `WorkingScale` | resolved values for this pass |
| `ImmediateCanvas OpenCanvas()` | canvas over the executor-acquired pooled target; executor brackets begin/end + sync |
| `IReadOnlyList<EffectInput> Inputs` | read-only sampled views (image shader / texture) of input passes |
| *(absent)* | no target creation, no flush, no snapshot, no target-list mutation |

### `EffectInput` (public)

Read-only view of a pass input: logical `Bounds`, `EffectiveScale Density`, `SKShader AsShader()`, `Bitmap Snapshot()` for descriptor-declared CPU readback, texture handle for compute passes.

## 2. Bounds & ROI contract

### `BoundsContract` (public, value type)

| Field | Type | Semantics |
|---|---|---|
| `TransformBounds` | `Func<Rect, Rect>` | forward: output logical bounds from input bounds (drives `Bounds` advancement; today's `transformBounds`) |
| `GetRequiredInputBounds` | `Func<Rect, Rect>` | backward: input region needed for a requested output region (FR-011) |
| `RequiresFullInput` | `bool` | true ⇒ ROI uses the complete input; forward/backward maps remain valid identity maps for `FullFrame` |

**Validation rules**: for coordinate-invariant nodes both functions must be identity (enforced by construction — invariant descriptors don't accept custom bounds). `GetRequiredInputBounds(roi)` must contain the region actually sampled; violating this is an authoring bug surfaced by a debug-mode assertion pass (executor compares sampled vs declared in tests).

## 3. Compilation layer (internal)

### `EffectGraph`

DAG of `EffectNode { Descriptor, Inputs[], Outputs[] }` built by the builder. Owns nothing GPU-side.

### `StructuralKey`

Hash accumulated over: node kinds + topology (branch structure), complete shader-source text + source kind, exact delegate `MethodInfo` identity for default callback/factory tokens and custom bounds contracts, structural ints (pass counts and static branch counts), coordinate-invariance flags, `BoundsContract` identity for non-invariant nodes. Storing `MethodInfo` directly also supports `Expression.Compile()` / `DynamicMethod`, which do not provide a usable runtime method handle. The stable 64-bit shader hash selects a program-cache bucket only; exact source comparison is the identity check. **Excludes** uniform values, colors, matrices, sampler texture contents, and dynamic-output counts. Two graphs with equal keys must compile to plans that differ only in parameter blocks (invariant enforced by construction: everything the compiler branches on feeds the key).

### `CompiledPlan`

| Field | Type | Notes |
|---|---|---|
| `Key` | `StructuralKey` + graphics-context identity | cache identity (D3). Bounds, ROIs, buffer sizes, and the resolved working scale are **per-frame resolution inputs, not key parts** — parameter-driven bounds (animated blur sigma, stroke pen) change sizes without recompiling. Compute pass counts and exact static branch counts are structural. A dynamic-output split deliberately resolves its count at execution and excludes that count from the key. |
| `Passes` | `CompiledPass[]` | topologically ordered schedule |
| `Resources` | `ResourcePlan` | structural shape of intermediates (formats, lifetime intervals) |
| `ParameterSlots` | `ParameterSlot[]` | where each frame's uniform/filter values go |

### `CompiledPass` (one of)

- **`FusedShaderPass`**: composition recipe — ordered stages (`RuntimeShaderStage(SKRuntimeEffect ref, uniform slots)` | `ColorFilterStage(filter slot)`), one input, one output, device ROI. Executes as one draw (D2).
- **`SkiaFilterPass`**: composed `SKImageFilter` chain slot, input, output, ROI.
- **`ComputePass`**: Vulkan pipeline ref, ping-pong color resource refs, push-constant slot, iteration count.
- **`GeometryPass`**: session callback ref, inputs, output, ROI.
- **`CompositePass` / split edges**: composite op + input refs.
- **`NestedGraphPass`**: per-branch describe callback; the executor re-describes, compiles, and recursively runs a child graph per branch index (dynamic-output).
- **`CustomRenderNodePass`**: a child `CustomRenderNodeFilterEffect.Resource` + its render-node `Type`; the executor drives the child's custom `FilterEffectRenderNode` as one node of the plan (full-frame, dynamic-output). The child inherits cache policy and shared diagnostics/pool, remains alive until its returned operations are disposed, and receives a conservative full request because the opaque pass has no compiler-visible backward bounds contract. The declarative home for an effect whose execution lives in a custom render node (`NodeGraphFilterEffect`), so it can be embedded in a group/delay-animation. See execution-plan §C3.6.
- **`OpaqueLegacyPass`** (transition-only, rollout steps 3–5): wraps an unmigrated effect's legacy item list and executes it via the retained (internal-only) activator machinery; deleted with the bridge in step 6.
- Common: `Backend` (Skia | Vulkan), `SyncBefore` flags (computed at schedule time: set only at backend transitions — D5), `IsDynamicOutputs` (execution-time-resolved output count; executor-owned pooled allocation, exempt from the static peak-live bound, counted and leak-checked).

### `ResourcePlan` (structural shape) + per-frame resource resolution

`ResourcePlan` is the structural half: `IntermediateDecl { Id, Format (currently RGBA16F), FirstUse, LastUse }`. Peak-live count = max overlap of `[FirstUse, LastUse]` intervals — the FR-007 bound asserted in tests. Ping-pong pairs are two decls with alternating lifetimes.

Concrete `DevicePixelSize` per decl and per-pass ROIs are computed **every frame** by the **resource resolution** pass: pure `Rect` math over the freshly described bounds, applying the working-scale carry (§6 note) and the 003 per-axis clamp, feeding the pool's `Acquire` sizes. This runs on cache hits and misses alike; it never creates programs or passes.

Passes flagged `IsDynamicOutputs` (contour-based part splitting) have no static decls for their outputs: the executor allocates them from the pool at execution time, tracks them for release within the frame, and counts them — the static peak-live bound (FR-007) applies to declared intermediates only.

### `PlanCache` (per `FilterEffectRenderNode`)

Single-entry (current key → plan) — a render node has one structure at a time; history beyond 1 buys nothing. Invalidation: key mismatch on describe, context device-lost, or node dispose (returns pooled resources). **Bounds/size/working-scale changes are NOT invalidations** — they flow through the per-frame resource resolution.

### `ProgramCache` (per graphics context, global across plans)

`Dictionary<SkslSourceHash, SKRuntimeEffect>` + composed-source cache for merged snippets. Never evicted below a small floor (program count is bounded by distinct effect-source combinations); LRU above a cap.

## 4. Execution layer (internal)

### `RenderTargetPool` (per shared graphics context)

| Member | Semantics |
|---|---|
| `Acquire(int w, int h)` | exact-size RGBA16F Skia-surface bucket pop or fresh allocation (counted as miss) |
| `AcquireTexture(int w, int h, TextureFormat fmt)` | exact-size surface-less raw-texture bucket pop or fresh allocation for custom render-node resources |
| `Release(target)` | return to bucket; contents undefined afterwards. **Lease model**: consumers hold ref-counted `ShallowCopy` handles, so the actual return is hooked into the underlying `RenderTarget`'s last ref-count release — a wrapper's `Dispose` alone never returns a surface other copies still reference. Concretely: today's private `SKSurfaceCounter` *disposes* on last release; a pooled target instead carries a pool-aware deallocator that returns surface+texture to the bucket atomically, plus a generation tag so a stale shallow copy can never observe a reissued target (tested) |
| `Trim(frameIndex)` | dispose targets unused ≥ N frames; enforce byte soft-cap LRU |
| Failure | propagate current semantics: preview → drop/degrade; delivery → throw (FR-015) |

Invariants: acquire/release strictly render-thread; every executor-acquired target is released by the end of the frame (leak assertion in debug tests); pooled targets are cleared on acquire. Bucket identity includes resource kind (`Skia surface` versus `raw texture`) in addition to size and format, so a raw RGBA16F texture cannot consume an RGBA16F `RenderTarget` entry or vice versa.

### `PlanExecutor`

Runs passes in order: resolve ROI targets from pool → bind parameter blocks → sync iff `SyncBefore` → draw/dispatch → release inputs whose `LastUse` passed. Emits counters (§5). Owns `GeometrySession` lifecycles.

### `ParameterBlock`

Per-frame values bound into `ParameterSlots`: uniform floats/vectors/matrices, `SKColorFilter` instances, sampler textures, push constants. Written by the describe pass on cache hit; the only per-frame mutable state (FR-009).

## 5. Observability (public read surface)

### `PipelineDiagnostics` (per renderer/processor)

Counters (`long`): `GpuPasses`, `TargetAllocations`, `PoolAcquires`, `PoolMisses`, `FullFrameMaterializations`, `FlushSyncs`, `PlanCompilations`, `ProgramCreations`, `PrefixCacheHits`, `CompositeLayerSaves`. The public read surface consists of get-only counters plus `Snapshot()`, which returns an immutable struct. Counter mutation and `Reset()` are engine-internal; tests reach `Reset()` through `InternalsVisibleTo`. Always-on field increments (D9).

## 6. State transitions

```text
FilterEffect.Resource (per frame, existing capture)
        │ Describe(builder, resource)          [cheap, every frame]
        ▼
   EffectGraph ──► StructuralKey ──► PlanCache hit? ──yes──► ParameterBlock update
        │                                │ no                 + resource resolution
        ▼                                ▼                    (sizes/ROIs/w carry)
   (discarded)                    Compile → CompiledPlan            │
                                         │                          ▼
                                         └──────────────────► PlanExecutor.Run
                                         │                        │
                                         └────────────────────────┘
                                    targets ⇄ RenderTargetPool, programs ⇄ ProgramCache
```

Boundary working-scale resolution (`ResolveWorkingScale`) happens in `PlanFilterEffectRenderNode.Process` **before** describe (FR-012); its result feeds the builder and the per-frame resource resolution, not the plan key. `ClampWorkingScaleToBufferBudget` is applied during that per-pass resource resolution, after describe, because each pass has different current bounds. The resolver carries the clamped `w` monotonically downward along each chain for legacy parity, while intra-pass allocations re-clamp locally without carrying.

## 7. Removed types (FR-016)

`FilterEffectActivator`, `CustomFilterEffectContext`, `FilterEffectContext` (public recording surface — the *name* may survive internally only if the builder reuses it, but the plan assumes a clean `EffectGraphBuilder`), `SKImageFilterBuilder` (public), `EffectTarget`, `EffectTargets`, `IFEItem`/`FEItem_*`. Replacement mapping: [contracts/breaking-changes.md](./contracts/breaking-changes.md).
