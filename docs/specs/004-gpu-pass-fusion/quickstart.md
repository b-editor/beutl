# Quickstart: Implementing Renderer-Wide GPU Pass Fusion

Read [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), and the [contracts](contracts/) before changing production code. This guide uses the finalized public authoring model.

## 1. Confirm the feature worktree

```bash
set -euo pipefail

expected_branch=speckit/004-gpu-pass-fusion-unified
expected_baseline_sha=83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53

test "$(git branch --show-current)" = "$expected_branch"
git merge-base --is-ancestor "$expected_baseline_sha" HEAD
```

The evidence SHA is a behavioral ancestor, not a required current merge base, so the ancestor guard stays true after the squash-merge while the branch-name guard does not. The earlier `speckit/004-gpu-pass-fusion` branch was superseded and is not an ancestor of the delivered work. Do not cherry-pick an abandoned GPU-pass branch; adapt only reviewed algorithms to this request-wide architecture.

## 2. Freeze evidence before changing scheduling

First add test-owned visual and workload evidence: raw linear-premultiplied RGBA16F artifacts, image-quality assertions, immutable provenance manifests, baseline shape probes, and persistent-lifetime benchmarks. Capture primary chains, barriers, thin antialiased paths, multiple roots, ROI/scale, reuse hits and misses, nested work, 3D, preview, and allocation failures.

The paired baseline runner must use a temporary worktree pinned to the evidence SHA and copy back only immutable artifacts and a manifest. Regular CI compares fusion-disabled and fusion-enabled schedules on the same process/device. The internal fusion mode is not a public renderer option.

*Amended.* The pinned starting-SHA baseline, its temporary-worktree runner, the immutable provenance manifests and the baseline shape probes were withdrawn with the evidence tree (tasks T005–T007, T016, T019, T020, T114, T115, T123), and `docs/specs/004-gpu-pass-fusion/evidence/` is not part of the repository. The evidence that still comes first is narrower: the raw RGBA16F store and the image-quality assertions under `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/`, plus the same-process fusion-disabled/enabled A/B in `GpuPassFusionSameProcessParityHarness`. [tasks.md](tasks.md) records the retirement per task and [spec.md](spec.md) SC-007 carries the parity contract that now applies.

## 3. Introduce one request recorder

The first recorder records a request-wide ordered fragment graph without executing GPU, media, or raw-canvas work:

```csharp
public sealed class PassThroughNode : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.PassThrough();
    }
}
```

Each invocation checkpoints fragments, publications, and resource transfers; validates them on normal return; then commits atomically or rolls back. Contexts, handles, and resource tokens become invalid when the invocation ends.

## 4. Migrate public authoring in one change

`RenderNode.Process` is `void`. It records and explicitly publishes fragment handles. Use the following topology choices:

- `PassThrough` for identity and no recording;
- `PublishMappedInputs` for an ordered one-to-one transform;
- `Opacity`, `Shader`, `Geometry`, or another proven typed primitive for its exact semantics;
- `OpaqueSource`, `OpaqueMap`, `OpaqueCombine`, or `OpaqueExpand` for callback-defined value work;
- `TargetScope` and `TargetCommand` for guarded target work;
- raw target calls only for unavoidable external-canvas behavior;
- `Publish`, `PublishRange`, `Drop`, `RecordNode`, and `RecordSubtree` for all other topology, where `Drop` abandons a fragment recorded only to inspect its metadata.

Content invalidation is equally direct: call `MarkChanged()` whenever a node property can alter pixels, metadata, or topology. Do not introduce application-managed output identities or resource content fields.

### One-to-one publication

```csharp
public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        _opacity,
        static (current, input, opacity) => current.Opacity(input, opacity));
}
```

`PublishMappedInputs` invokes its mapper once per input in painter order and publishes the returned handle immediately. An empty input collection produces no output. The mapper may record intermediate handles but must not publish; publication in a mapper is rejected and rolls back the transaction. Prefer the generic overload and a `static` mapper in allocation-sensitive paths.

### Guarded operation descriptions

Build the description inside `Process` and hand it straight to the recording method. Hoist only what is genuinely fixed — a slot, the slot list a description declares, a hit-test contract over a slot — because the plan is keyed by the callback's method and the declared contracts, not by the values a recording carries.

```csharp
private sealed record DrawState(float Opacity);

private static readonly RenderResourceSlot<Brush.Resource> s_brush = new();
private static readonly RenderResourceSlot[] s_slots = [s_brush];

public override void Process(RenderNodeContext context)
{
    RenderResource<Brush.Resource> brush = context.Borrow(_brush);

    context.PublishMappedInputs(
        OpaqueRenderDescription.Create(
            new DrawState(_opacity),
            static (session, state) => session.UseResource(
                s_brush,
                brush => Draw(session, brush, state.Opacity)),
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.PreserveInputSupply,
            resources: [s_brush.Bind(brush)],
            slots: s_slots),
        static (current, input, description) => current.OpaqueMap(input, description));
}
```

Every slot declared through `slots:` must be bound exactly once in `resources:`. Guarded callbacks lease it through `session.UseResource(slot, ...)`. `Borrow` retains caller ownership; `Own` transfers a disposable raw object to the request family. Neither takes caller-controlled reuse metadata.

### Raw target work

Raw work remains request-local, but it still passes state and declares typed slots:

```csharp
private readonly record struct RawState(RenderResource<IBackdrop> Resource);

private static readonly RenderResourceSlot<IBackdrop> s_backdrop = new();
private static readonly RenderResourceSlot[] s_slots = [s_backdrop];

public override void Process(RenderNodeContext context)
{
    RenderResource<IBackdrop> backdrop = context.Borrow(_backdrop);

    context.Publish(context.RawTargetCommand(
        RawTargetCommandDescription.Create(
            new RawState(backdrop),
            static (session, state) => session.UseResource(
                state.Resource,
                value => value.Draw(session.Canvas)),
            queryBounds: _bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            resources: [s_backdrop.Bind(backdrop)],
            slots: s_slots)));
}
```

This callback reaches its resource by token, which raw sessions allow and guarded ones do not; the same token is bound to the declared slot so the schema is still checked. What makes raw work unreusable is the opaque canvas, not the token — a raw description passes state like any other, and that state is what gives the planner one plan per callback instead of one per recording. A raw scope uses `RawTargetScopeDescription` and must replay its input exactly once.

## 5. Add shader and geometry stages

Parse the SkSL once into a shared `SkslSource`, then build a `ShaderDescription` where the stage is recorded:

```csharp
private static readonly SkslSource s_tintSource = SkslSource.CurrentPixel(
    """
    uniform float amount;
    half4 apply(half4 color) {
        return half4(color.rgb * amount, color.a);
    }
    """);

public override void Process(RenderNodeContext context)
{
    float amount = _amount;

    context.PublishMappedInputs(
        ShaderDescription.CurrentPixel(
            s_tintSource,
            bindings => bindings.Uniform("amount", amount)),
        static (current, input, description) => current.Shader(input, description));
}
```

Use `.WholeSource` for a whole-input shader with `uniform shader src;` and fixed bounds behavior. Renderer-generated names are reserved: any shader source that declares a binding named `__beutl_pixel` or `__beutl_head_main`, a `__beutl_s<N>_`-prefixed name, or an `fe`-prefixed name containing `_`, is rejected, and a whole-source shader may not declare a renderer-generated top-level name. The `bindings` action runs immediately and is never retained, so it may close over this recording's values; the execution-time binders it registers may not, and take their changing value as an argument beside them. `ShaderBindingBuilder.Resource` declares typed child-shader slots. `GeometryDescription.Create` follows the same shape for geometry callbacks, metadata, optional readback, and slots. `FilterEffectContext` accepts a `ShaderDescription` and a `GeometryDescription` directly.

## 6. Record complete roots, then analyze and execute

Record every root into one ordered graph. Only after recording should the renderer lower scoped target dependencies, resolve bounds/density/required regions, choose retained-output substitutions and captures, plan islands, and execute in painter order.

Do not pass resolved ROI into `Process`. `Process` records contracts; analysis derives concrete regions after the complete graph is known. Bounds and hit-test requests use that same recorder but stop before deferred pixel work.

Raw target callbacks, backend transitions, target readback, capture, and unsupported shader/geometry features create deliberate barriers. A fused run admits adjacent current-pixel shader stages and bounds- and scale-preserving opacity stages once coverage has been resolved, and it may be led by a whole-source shader head whose downstream current-pixel stages are appended to it; folding work upstream of a whole-source head is still rejected.

## 7. Add retained-output and resource planning last

The renderer owns retained output, structural/program plans, and pooled targets. A node only reports content change through `HasChanges`. A node that records a child it cannot list in `ChildNodes` must also call `context.DisableRenderCache()` during that transaction, because the cache cannot observe a change reported only by an unlisted child. Raw target work cannot be retained across requests.

After the uncached plan is correct, verify stable parameter frames reuse immutable plans and programs, concrete target sizes reuse pools, and changed node content triggers correct rerecording. Keep direct output ownership and resource disposal inside the request/renderer lifecycle.

## 8. Finish boundaries and failure behavior

Record 3D as a backend source and materialize one 2D value at the boundary. Record separate-target nested work before GPU execution; same-target nested work remains in the parent graph.

Inject failures around recording, analysis, allocation, shader compilation/binding, callback execution, capture publication, and cleanup. Each resource transfer must settle exactly once. A failure preserves the primary exception and publishes no partial output.

## 9. Run final validation

```bash
dotnet format Beutl.slnx --verify-no-changes
dotnet build Beutl.slnx
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings
```

Run the fallback shader tests on every host and the GPU-required suites on a configured graphics host. Run paired persistent-lifetime benchmarks in the pinned baseline and feature worktrees on the same system. The branch's breaking commits each use a breaking Conventional Commit subject and carry their own `BREAKING CHANGE:` footer, but `main` is squash-only, so the footer that reaches changelog tooling is the one in the pull request description: keep a footer there that names `Beutl.Engine` and summarizes the migrations in [contracts/breaking-changes.md](contracts/breaking-changes.md), and update it whenever another breaking commit lands.

*Amended.* The paired pinned-baseline benchmark comparison was withdrawn with the evidence tree (tasks T114, T115, T123). `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` stays runnable on demand for the SC-008 workloads, but the same-fingerprint paired comparison and its confidence interval are not produced, so the performance improvement is measurable on demand and is not asserted as a met acceptance criterion; see [spec.md](spec.md) SC-008.
