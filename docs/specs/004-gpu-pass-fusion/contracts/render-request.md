# Internal Render Request Contract

## Request entry points

`RenderNodeRenderer` creates one complete request for rendering, rasterization, measurement, or hit testing. A request contains intent, purpose, optional target domain, optional requested region, output/maximum working scales, and renderer-owned execution policy.

Frame requests record every root in painter order. Bounds and hit-test requests use the same recorder and metadata analysis but stop before deferred GPU, media, and canvas work. Public APIs never return a fragment handle outside its active recording transaction.

## Renderer frame sequencing

```text
update node state
  -> record all roots into one graph
  -> lower scoped target dependencies
  -> resolve metadata and required regions
  -> select safe retained-output substitutions/captures
  -> plan islands, shader runs, and resource leases
  -> execute once in painter order
  -> publish successful output and settle resources
```

Content invalidation enters this sequence through `RenderNode.HasChanges`. It is the sole public signal that a node's recorded content must be refreshed. The renderer invalidates the changed node and its recorded ancestors, while independently reusable unchanged descendants retain their warm-up and may still satisfy cache lookup. The context's only public retention control is `DisableRenderCache()`, which a node must call when it records a child it cannot list in `ChildNodes`; there is no public way to force retention, and public resource tokens carry no content-invalidation fields.

## Recording

### Transaction protocol

Each `Process` invocation creates a transaction checkpoint. The recorder provides borrowed input handles, validates publications and resource transfers, and commits only on normal return. An exception discards fragments, publications, and pending transfers from that invocation, releases owned raw objects best-effort, preserves the primary exception, and invalidates every context/handle/token facade.

`PublishMappedInputs` runs its mapper inside this same checkpoint. It is a strict one-input/one-output helper, not an implicit pass-through or a general expansion API.

### Description lowering

Public authoring passes an operation description to the context. Internally the description is split into execution state plus the immutable metadata the planner reads: callback code, bounds/hit-test/scale/cardinality or target contracts, and the declared resource-slot schema on one side, the recording's state and bindings on the other.

The engine derives plan shape from the first half only. A description rebuilt with different values therefore produces the same plan shape, and no description has to be held across requests for reuse to work. There is no caller-provided operation or resource identifier in the lowering protocol.

### Resource binding validation

A description declares a heterogeneous list of `RenderResourceSlot` values and must bind exactly those slots, once each, through typed `slot.Bind(token)` values. The declaration also orders the bindings, so the order the author wrote them in does not reach the operation's structural identity. Guarded execution sessions use a slot to lease the raw value. Raw execution sessions additionally allow token leasing because the raw-canvas boundary is request-local; the token then travels in the description's state and is still bound to its matching slot.

## Recorded IR

### Ordered fragment graph and value graph

Every fragment preserves ordered child fragments, value inputs, target effects, scope kind, publication/provenance, value cardinality, and whether it can be consumed as a value input. Target commands and raw commands are real ordered fragments even when their value cardinality is none. Captures are target-token-to-value edges. Opacity, blend, mask, guarded scope, and raw scope wrap their child fragment without moving target effects out of painter order.

### Scope-local target lowering

Target effects are lowered as scoped target-token dependencies rather than a request-global side list. A guarded target scope has declared bounds/hit-test/scale behavior; a guarded target command has declared affected region, query bounds, access, and optional readback. Raw scope and raw command are opaque external boundaries. A raw scope must replay its input exactly once.

### Provenance

Root provenance retains painter order and query behavior independently of materialized value substitutions. `RootOutputExtent` covers contributing values and pixel-writing target effects; query bounds remain separate. A null requested region selects the complete output extent.

## Metadata analysis

### Forward resolution

Forward analysis resolves output bounds, effective supply, value cardinality, contribution, target dependence, and hit-test provenance from the complete graph. It may reevaluate pure bounds or scale mappings after symbolic upstream metadata becomes concrete. It does not execute deferred execution callbacks; only pure metadata callbacks run during analysis.

A reevaluated mapping is held to the answer recording stored. For a fragment with no symbolic upstream metadata that is the resolved answer itself, which is computed over the same input metadata recording passed the mapping. For a symbolic fragment the resolved answer is expected to differ, so the mapping is asked once more for the input metadata it was recorded over and must return what recording stored. Either disagreement fails the request. Equality is exact: a tolerance would admit a mapping that reads mutable state rather than accommodate drift between identical inputs. Backward mappings and hit-test contracts are not covered - recording never evaluates the first, and resolution never reevaluates the second.

### Backward regions

Backward analysis starts at the requested root region or complete output extent and propagates required regions through value transforms and scope-local target dependencies. Unknown mappings request full inputs. Full target access requires a finite owning target domain.

## Retained-output resolution

The renderer discovers safe candidates after metadata and region analysis. A hit substitutes an internal materialized value while preserving original query provenance. A miss inserts a capture point after the scheduled producer. Capture publication happens only after the complete request succeeds.

The public lifecycle is deliberately simple: if node content changes, the node calls `MarkChanged()`; otherwise the renderer may reuse eligible output according to its complete internal plan and request conditions. Raw target work, unbounded external work, and target-dependent regions without a proven complete predecessor are not safely retained.

## Island planning

The planner partitions work at materialization, opaque callbacks, geometry callbacks, target commands/captures/readback, target scopes where equivalence is unproven, raw canvas work, external targets, backend transitions, dynamic topology, unsupported shader capability/resource limits, a fragment consumed more than once, an incompatible working-scale transition between adjacent stages, and the retained-output substitution and capture points selected earlier in the sequence.

An island is maximal only if combining adjacent work preserves painter order, target-token order, value semantics, bounds/ROI, scale, color/alpha semantics, hit-test provenance, output cardinality, and required synchronization.

## Shader fusion

### Eligibility

Eligible stages are current-pixel `ShaderDescription` stages and engine operations with a proved equivalent lowering. Whole-source shaders, geometry, opaque work, coordinate-changing or unknown sampling stages, blend/composite, readback, capture, external targets, raw work, and backend transitions are barriers.

*Amended (`991f49e70`).* A whole-source shader is no longer a barrier. One may lead a fused run of downstream current-pixel and opacity stages, so a run's leading stage may be coordinate-changing; folding work upstream of a whole-source stage stays rejected, because its sampling is too broad to prove that rewrite equivalent. `research.md` R8 and `plan.md`'s Shader and Geometry seam carry the current rule.

An arbitrary public current-pixel shader cannot cross an analytic or antialiased coverage-producing source. Coverage must first resolve into a materialized value unless an engine-owned stage has a mechanical premultiplied-coverage-homogeneity proof.

### Composition and binding

The compiler merges compatible stages in authored order, isolates uniform/resource names and declarations, validates source and backend limits, and splits deterministically before an overflowing stage. A one-stage run is valid.

After plan selection, runtime binding receives the resolved logical bounds, required region, effective supply, working density, device footprint, call-state uniforms, and child resources. A binding failure fails the request and suppresses output publication. A target-allocation failure fails the request under `RenderIntent.Delivery`; under `RenderIntent.Preview` the renderer drops the affected contribution, completes the request with degraded pixels, and publishes no retained output for that request.

### Program reuse

The renderer owns compiled shader reuse based on the complete merged source, layout, backend capability, color/alpha/format contract, and relevant compile options. Hashing is only a lookup optimization; full equality decides reuse. Public shader calls never provide program identity fields.

A stage may additionally carry an engine-authored SPIR-V lowering. When the run is that single stage, the shared graphics context supports it, and its input and output are matching RGBA16F footprints at equal density, the renderer executes that lowering through a separate SPIR-V program cache; a native compile or resource failure falls back to the SkSL lowering, which remains the compatibility contract. Backend selection is engine-owned and never author-declared.

## Resource and scale plan

### Working density

`RenderScaleContract.PreserveInputSupply` copies the resolved supply for an element-wise one-input map or replay scope. `RenderScaleContract.MapInputSupply` applies a pure transform to the corresponding input supply, which may return `EffectiveScale.Unbounded`, together with the pure transform that carries a backward output demand to the input demand. `RenderScaleContract.MapInputSupplyPreservingDemand` declares the forward transform alone and leaves demand unchanged. The callbacks are reevaluated after symbolic metadata resolves.

Materializing values choose a concrete positive density from complete bounds, input supplies, output scale, and maximum working scale. The renderer applies its per-buffer device-axis clamp and binds the actual resulting density to execution. Region cropping does not recompute a declared supply.

### Pool and liveness

The resource plan computes first/last use for each materialized value and leases exact compatible targets from a renderer-owned pool. Planner-owned targets are initialized before guarded callback access. Borrowed root and presentation targets are neither pooled nor disposed by the request.

### Synchronization

Synchronization is declared per consumer through a sampling intent. Declared CPU readback, backend transitions, and cross-context or undetermined consumers submit and wait; same-context texture sampling at a materialization boundary submits without waiting. Target-token dependencies and platform ownership transitions synchronize where they require it. Compatible same-backend shader stages do not introduce per-stage flushes. Guarded callback canvases are executor-managed one-shot leases; raw canvases remain opaque external work.

## Execution and failure

`RenderRequestExecutor` owns plan resources. It acquires program/target leases, executes islands and scoped target dependencies in dependency/painter order, validates dynamic output against declared contracts, stages captures, publishes final state only after complete success, and settles every lease in all paths.

On failure it preserves the first planning or rendering exception, discards partial outputs and staged captures, invalidates callback sessions, continues cleanup best-effort, and reports cleanup faults separately.

## Nested requests

Same-target nested rendering uses `RecordNode` or `RecordSubtree` and remains in the parent graph. Separate-target work records a child request before parent execution and inherits appropriate renderer ownership and request policy. A child requested region is expressed in child coordinates; it is never copied blindly from the parent.

Nodes cannot retain public fragment handles between requests. NodeGraph-style wrappers use active request-local bindings and publish only while that binding is active.

## 3D boundary

`Scene3DRenderNode` records a graphics-backend source using an engine-defined opaque call. Execution resolves the declared bounds/density, renders one materialized 2D value, performs the required backend transition, and releases resources through the request owner. The 2D planner does not inspect 3D internals; fusion may begin after the materialized boundary.

## Metadata-only queries

Bounds and hit-test requests perform recording and metadata analysis only. They do not execute deferred callbacks, allocate renderer targets, read media frames, or publish retained output. Hit testing evaluates query provenance in reverse painter order and returns false outside a non-null requested region.
