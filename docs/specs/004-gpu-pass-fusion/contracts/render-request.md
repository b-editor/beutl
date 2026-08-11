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

Content invalidation enters this sequence through `RenderNode.HasChanges`. It is the sole public signal that a node's recorded content must be refreshed. The renderer invalidates the changed node and its recorded ancestors, while independently reusable unchanged descendants retain their warm-up and may still satisfy cache lookup. The context has no public method for overriding retention and public resource tokens carry no content-invalidation fields.

## Recording

### Transaction protocol

Each `Process` invocation creates a transaction checkpoint. The recorder provides borrowed input handles, validates publications and resource transfers, and commits only on normal return. An exception discards fragments, publications, and pending transfers from that invocation, releases owned raw objects best-effort, preserves the primary exception, and invalidates every context/handle/token facade.

`PublishMappedInputs` runs its mapper inside this same checkpoint. It is a strict one-input/one-output helper, not an implicit pass-through or a general expansion API.

### Definition and call lowering

Public authoring passes a typed call to the context. Internally, the call is lowered into execution state plus immutable metadata captured from its definition. The fixed definition supplies callback code, bounds/hit-test/scale/cardinality or target contracts, and typed resource-slot schema. The call supplies current state and bindings.

The engine derives plan shape from those fixed inputs. Equivalent definitions recreated later produce the same plan shape; sharing a definition instance is an allocation optimization, not a correctness condition. There is no caller-provided operation or resource identifier in the lowering protocol.

### Resource binding validation

A definition declares a heterogeneous list of `RenderResourceSlot` values. A call must bind exactly the declared slots, once each, through typed `slot.Bind(token)` values. Guarded execution sessions use a slot to lease the raw value. Raw execution sessions retain token leasing because the raw-canvas boundary is request-local; their state includes the token and the call still binds it to the matching slot.

## Recorded IR

### Ordered fragment graph and value graph

Every fragment preserves ordered child fragments, value inputs, target effects, scope kind, publication/provenance, value cardinality, and whether it can be consumed as a value input. Target commands and raw commands are real ordered fragments even when their value cardinality is none. Captures are target-token-to-value edges. Opacity, blend, mask, guarded scope, and raw scope wrap their child fragment without moving target effects out of painter order.

### Scope-local target lowering

Target effects are lowered as scoped target-token dependencies rather than a request-global side list. A guarded target scope has declared bounds/scale/access behavior; a guarded target command has declared affected region, query bounds, access, and optional readback. Raw scope and raw command are opaque external boundaries. A raw scope must replay its input exactly once.

### Provenance

Root provenance retains painter order and query behavior independently of materialized value substitutions. `RootOutputExtent` covers contributing values and pixel-writing target effects; query bounds remain separate. A null requested region selects the complete output extent.

## Metadata analysis

### Forward resolution

Forward analysis resolves output bounds, effective supply, value cardinality, contribution, target dependence, and hit-test provenance from the complete graph. It may reevaluate pure bounds or scale mappings after symbolic upstream metadata becomes concrete. It does not execute callbacks.

### Backward regions

Backward analysis starts at the requested root region or complete output extent and propagates required regions through value transforms and scope-local target dependencies. Unknown mappings request full inputs. Full target access requires a finite owning target domain.

## Retained-output resolution

The renderer discovers safe candidates after metadata and region analysis. A hit substitutes an internal materialized value while preserving original query provenance. A miss inserts a capture point after the scheduled producer. Capture publication happens only after the complete request succeeds.

The public lifecycle is deliberately simple: if node content changes, the node sets `HasChanges`; otherwise the renderer may reuse eligible output according to its complete internal plan and request conditions. Raw target work, unbounded external work, and target-dependent regions without a proven complete predecessor are not safely retained.

## Island planning

The planner partitions work at materialization, opaque callbacks, geometry callbacks, target commands/captures/readback, target scopes where equivalence is unproven, raw canvas work, external targets, backend transitions, dynamic topology, and unsupported shader capability/resource limits.

An island is maximal only if combining adjacent work preserves painter order, target-token order, value semantics, bounds/ROI, scale, color/alpha semantics, hit-test provenance, output cardinality, and required synchronization.

## Shader fusion

### Eligibility

Eligible stages are current-pixel `ShaderDefinition<TState>` calls and engine operations with a proved equivalent lowering. Whole-source shaders, geometry, opaque work, coordinate-changing or unknown sampling stages, blend/composite, readback, capture, external targets, raw work, and backend transitions are barriers.

An arbitrary public current-pixel shader cannot cross an analytic or antialiased coverage-producing source. Coverage must first resolve into a materialized value unless an engine-owned stage has a mechanical premultiplied-coverage-homogeneity proof.

### Composition and binding

The compiler merges compatible stages in authored order, isolates uniform/resource names and declarations, validates source and backend limits, and splits deterministically before an overflowing stage. A one-stage run is valid.

After plan selection, runtime binding receives the resolved logical bounds, required region, effective supply, working density, device footprint, call-state uniforms, and child resources. Any resource or binding failure fails the request and suppresses output publication.

### Program reuse

The renderer owns compiled shader reuse based on the complete merged source, layout, backend capability, color/alpha/format contract, and relevant compile options. Hashing is only a lookup optimization; full equality decides reuse. Public shader calls never provide program identity fields.

## Resource and scale plan

### Working density

`RenderScaleContract.PreserveInputSupply` copies the resolved supply for an element-wise one-input map or replay scope. `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale>)` applies a pure transform to the corresponding input supply and may return `EffectiveScale.Unbounded`. The callback is reevaluated after symbolic metadata resolves.

Materializing values choose a concrete positive density from complete bounds, input supplies, output scale, and maximum working scale. The renderer applies its per-buffer device-axis clamp and binds the actual resulting density to execution. Region cropping does not recompute a declared supply.

### Pool and liveness

The resource plan computes first/last use for each materialized value and leases exact compatible targets from a renderer-owned pool. Planner-owned targets are initialized before guarded callback access. Borrowed root and presentation targets are neither pooled nor disposed by the request.

### Synchronization

Synchronization occurs only for declared CPU readback, backend transitions, target-token dependencies that require it, and platform ownership transitions. Compatible same-backend shader stages do not introduce per-stage flushes. Guarded callback canvases are executor-managed one-shot leases; raw canvases remain opaque external work.

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
