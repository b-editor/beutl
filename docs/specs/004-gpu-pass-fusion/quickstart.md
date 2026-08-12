# Quickstart: Implementing Renderer-Wide GPU Pass Fusion

Read [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), and the [contracts](contracts/) before changing production code. This guide uses the finalized public authoring model.

## 1. Confirm the feature worktree

```bash
set -euo pipefail

expected_branch=speckit/004-gpu-pass-fusion
expected_baseline_sha=83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53

test "$(git branch --show-current)" = "$expected_branch"
git merge-base --is-ancestor "$expected_baseline_sha" HEAD
```

The evidence SHA is a behavioral ancestor, not a required current merge base. Do not cherry-pick an abandoned GPU-pass branch; adapt only reviewed algorithms to this request-wide architecture.

## 2. Freeze evidence before changing scheduling

First add test-owned visual and workload evidence: raw linear-premultiplied RGBA16F artifacts, image-quality assertions, immutable provenance manifests, baseline shape probes, and persistent-lifetime benchmarks. Capture primary chains, barriers, thin antialiased paths, multiple roots, ROI/scale, reuse hits and misses, nested work, 3D, preview, and allocation failures.

The paired baseline runner must use a temporary worktree pinned to the evidence SHA and copy back only immutable artifacts and a manifest. Regular CI compares fusion-disabled and fusion-enabled schedules on the same process/device. The internal fusion mode is not a public renderer option.

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
- `Publish`, `PublishRange`, `RecordNode`, and `RecordSubtree` for all other topology.

Content invalidation is equally direct: set `HasChanges` whenever a node property can alter pixels, metadata, or topology. Do not introduce application-managed output identities or resource content fields.

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

### Guarded definition and call

Prefer one static/shared definition for fixed callback code, metadata, and slot schema to avoid allocation. Equivalent definitions recreated later still share the engine-derived plan, so a singleton lifetime is not a correctness requirement. Create a call for state and request-scoped tokens each time `Process` records it.

```csharp
private sealed record DrawState(float Opacity);

private static readonly RenderResourceSlot<Brush.Resource> s_brush = new();

private static readonly OpaqueRenderDefinition<DrawState> s_draw =
    OpaqueRenderDefinition<DrawState>.Create(
        static (session, state) => session.UseResource(
            s_brush,
            brush => Draw(session, brush, state.Opacity)),
        OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
        RenderHitTestContract.AnyInput,
        RenderValueCardinality.Single,
        RenderScaleContract.PreserveInputSupply,
        resources: [s_brush]);

public override void Process(RenderNodeContext context)
{
    RenderResource<Brush.Resource> brush = context.Borrow(_brush);
    OpaqueRenderCall<DrawState> call = s_draw.Call(
        new DrawState(_opacity),
        [s_brush.Bind(brush)]);

    context.PublishMappedInputs(
        call,
        static (current, input, recordedCall) => current.OpaqueMap(input, recordedCall));
}
```

Every slot declared through `resources:` must be bound exactly once. Guarded callbacks lease it through `session.UseResource(slot, ...)`. `Borrow` retains caller ownership; `Own` transfers a disposable raw object to the request family. Neither takes caller-controlled reuse metadata.

### Raw target work

Raw work remains request-local, but it still declares typed slots and is recorded through a generic definition:

```csharp
private sealed record RawState(RenderResource<IBackdrop> Backdrop);
private static readonly RenderResourceSlot<IBackdrop> s_backdrop = new();

private static readonly RawTargetCommandDefinition<RawState> s_command =
    RawTargetCommandDefinition<RawState>.Create(
        static (session, state) => session.UseResource(
            state.Backdrop,
            backdrop => backdrop.Draw(session.Canvas)),
        queryBounds: new Rect(0, 0, 1, 1),
        hitTest: RenderHitTestContract.None,
        resources: [s_backdrop]);

public override void Process(RenderNodeContext context)
{
    RenderResource<IBackdrop> backdrop = context.Borrow(_backdrop);
    context.Publish(context.RawTargetCommand(
        s_command.Call(new RawState(backdrop), [s_backdrop.Bind(backdrop)])));
}
```

The raw callback uses the token kept in call state. The same token must be bound to the typed slot, which validates the schema. A raw scope uses `RawTargetScopeDefinition<TState>` and must replay its input exactly once.

## 5. Add shader and geometry definitions

Define fixed shader source and uniform/resource schema once, then pass values through `ShaderCall<TState>`:

```csharp
private sealed record TintState(float Amount);

private static readonly ShaderDefinition<TintState> s_tint =
    ShaderDefinition<TintState>.CurrentPixel(
        """
        uniform float amount;
        half4 apply(half4 color) {
            return half4(color.rgb * amount, color.a);
        }
        """,
        static bindings => bindings.Uniform("amount", static state => state.Amount));

public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        new TintState(_amount),
        static (current, input, state) => current.Shader(input, s_tint.Call(state)));
}
```

Use `.WholeSource` for a whole-input shader with `uniform shader src;` and fixed bounds behavior. `ShaderDefinitionBuilder<TState>.Resource` declares typed child-shader slots. `GeometryDefinition<TState>.Create` uses the same definition/call split for geometry callbacks, metadata, optional readback, and slots. `FilterEffectContext` accepts `ShaderCall<TState>` and `GeometryCall<TState>` directly.

## 6. Record complete roots, then analyze and execute

Record every root into one ordered graph. Only after recording should the renderer lower scoped target dependencies, resolve bounds/density/required regions, choose retained-output substitutions and captures, plan islands, and execute in painter order.

Do not pass resolved ROI into `Process`. `Process` records contracts; analysis derives concrete regions after the complete graph is known. Bounds and hit-test requests use that same recorder but stop before deferred pixel work.

Raw target callbacks, backend transitions, target readback, capture, and unsupported shader/geometry features create deliberate barriers. Adjacent eligible current-pixel shader stages can form a fused run after coverage has been resolved.

## 7. Add retained-output and resource planning last

The renderer owns retained output, structural/program plans, and pooled targets. A node only reports content change through `HasChanges`. Raw target work cannot be retained across requests.

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

Run the fallback shader tests on every host and the GPU-required suites on a configured graphics host. Run paired persistent-lifetime benchmarks in the pinned baseline and feature worktrees on the same system. The public migration commit uses a breaking Conventional Commit and identifies downstream render-node authors in its footer.
