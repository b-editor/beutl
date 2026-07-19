# Quickstart: Implementing Renderer-Wide GPU Pass Fusion

This guide is for implementing the feature after `/speckit-tasks` has generated the dependency-ordered task list. Read [spec.md](spec.md), [plan.md](plan.md), [data-model.md](data-model.md), and the [contracts](contracts/) before changing production code.

## 1. Confirm the feature worktree

```bash
git branch --show-current
git status --short
git merge-base HEAD origin/main
```

Expected feature branch:

```text
speckit/004-gpu-pass-fusion
```

The behavioral baseline recorded by this plan is:

```text
43a38e665d9bf52548161a3917e748bd1457ff55
```

Do not cherry-pick the abandoned GPU-pass branch. Extract only reviewed leaf algorithms from its final HEAD and adapt them to this request-wide design.

## 2. Freeze evidence before changing scheduling

First add only test/provenance tooling and observational counters:

1. raw linear premultiplied RGBA16F golden storage;
2. SSIM, RGB MAE, alpha MAE, edge-band local MAE, and maximum-channel-error assertions;
3. fail-on-missing immutable references and non-vacuity controls;
4. target-baseline provenance patch/scripts/hashes under `docs/specs/004-gpu-pass-fusion/evidence/`, including `run-paired-visual-evidence.sh`;
5. request-wide counters that do not alter decisions;
6. persistent-lifetime BenchmarkDotNet scenes.

`generate-target-baseline.sh` must create a temporary worktree pinned to the starting SHA, apply `target-baseline-generator.patch` there, and copy back only immutable RGBA16F artifacts plus a manifest. Do not compile the historical generator in the feature branch. `run-paired-visual-evidence.sh` runs both worktrees and rejects missing or mismatched fingerprint fields before comparison. Record artifact/patch/generator-script/paired-runner hashes and exact OS, architecture, backend, device, driver, graphics-library, and runtime fingerprints. Normal CI instead compares fusion-disabled and fusion-enabled output on the same process/device through the internal request `FusionMode`, uses a fixed per-channel AA edge maximum error of `0.02`, and always verifies manifest integrity; the paired workflow may use a tighter edge bound only from its exact matching fingerprinted manifest. The mode is inherited and included in structural-plan identity but is not exposed in public renderer options. CI never selects a foreign-device blob, and this A/B check does not replace the starting-SHA proof.

Capture current behavior for the primary chain, barriers, antialiased thin paths, multiple roots, ROI/scale, cache hit/miss, nested/query, 3D, fallback, and preview/delivery allocation failures. Commit the evidence separately before changing `Process` or execution order.

The donor's eight independently reproducible `004-parity-strong` files may be copied as supplemental regressions only after their historical verification script reproduces their hashes. Do not use donor timing/counter values as target baselines.

## 3. Introduce one request recorder

Add the internal request/transaction/fragment/value/scoped-target structures before migrating nodes. The first recorder should deliberately lower all pixel work to guarded opaque or explicitly raw execution descriptions; it is a correctness bridge inside the final request architecture, not a public legacy adapter.

The critical recording skeleton is:

```csharp
public abstract class RenderNode
{
    public abstract void Process(RenderNodeContext context);
}

public sealed class PassThroughNode : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.PassThrough();
    }
}
```

For every node invocation:

- checkpoint fragment/value/publication/cache/resource state;
- invoke `Process` without GPU/media execution;
- validate owner, value-cardinality, contribution, and value-input rules;
- map nested inputs to fresh child-owned facades and committed outputs to fresh parent-owned handles;
- commit atomically or roll back entirely;
- invalidate public contexts and handles.

Run transaction and recording-side-effect tests before broad migration.

## 4. Migrate the complete executable API in one change

Change `RenderNode.Process`, replace executable `RenderNodeOperation` with the sealed `RenderFragmentHandle`, and migrate all production/test overrides and direct pull consumers together. Treat the seven test overrides as only the override subset: separately migrate all 24 starting-SHA test files that name the removed surface, including the golden harness while leaving its 18 consumers unchanged. Move all 24 direct feature-003 scale-helper caller/reference files (15 production and 9 tests) to `RenderScaleUtilities` without a forwarding shim. Replace direct pulls/list rasterization with a disposable `RenderNodeRenderer` and its `Render`, single-result `Rasterize`, `Measure`, and `HitTest` operations; `Rasterize` returns one disposable `RenderNodeRasterization` carrying its logical bounds/origin, output scale, normal empty state, and optional owned bitmap. Dispose each owner at the consumer lifetime boundary. Do not leave a returning overload, obsolete shim, public executable callback factory, or `Pull`/`PullToRoot` path.

Use the shapes in [contracts/breaking-changes.md](contracts/breaking-changes.md):

- publish nothing for intentional zero output;
- `PassThrough` for identity;
- `Opacity`, `Shader`, or other engine-proven semantics for typed maps;
- `OpaqueSource`, `OpaqueMap`, `OpaqueCombine`, or `OpaqueExpand` for unknown callbacks;
- returned/published `TargetCommand` for guarded current-target effects;
- non-contributing `TargetCapture` plus explicit `ContributeValues` for target-to-value reads;
- `TargetScope` for allocation-free transform/clip, `TargetLayerScope(inputs, TargetRegion)` for scope-relative transparent group isolation, and finite-domain `Layer(inputs, Rect)` for mixed painter streams that must become one value;
- `RawTargetScope`/`RawTargetCommand` only for retained unguarded raw-canvas behavior;
- `MaterializedInput` with explicit ownership for pre-existing buffers;
- `RecordNode`/`RecordSubtree` for same-request nested work;
- `DisableRenderCache` for monotonic cache policy.

Apply the public invariants at the same checkpoint:

- move `RenderNodeContext` scale helpers to `RenderScaleUtilities` and `RenderBoundsContract` to `Beutl.Graphics.Rendering`;
- make `Own<T>` require a disposable reference, while `Borrow<T>`/`UseResource<T>` accept any reference type and a null Borrow key selects request-local cache identity;
- assert the complete `CanBeUsedAsValueInput` table: value maps remain eligible, Opacity preserves eligible pure children, Blend/target scopes/commands—including `TargetLayerScope`—are ineligible, and finite Layer restores eligibility by local materialization;
- keep target-less `TargetDomain` separate from `RequestedRegion`; reject root Full target access without a destination/`TargetDomain`, and record existing `PushLayer(default)` through bottom-up `TargetLayerScope(..., Full)` so its region remains symbolic through all later parent transform/clip scopes;
- keep `RootOutputExtent` (contributing values plus potentially pixel-writing target effects) separate from query bounds; a null `RequestedRegion` selects the former, while Measure and HitTest retain the latter.
- treat public `TargetCapture` as a concrete declared-density resampling boundary. Its standard policy starts from output-derived density; its custom resolver receives empty `InputSupplies` and may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale`. Neither form inherits a denser enclosing target resolved later. Reserve owning-scope late binding for the internal backdrop path.

At this checkpoint, all visual/counter baselines must still match with fusion disabled.

## 5. Make Renderer record all roots

Change frame sequencing to:

```text
update every tree
  -> record root clear and every top-level contribution
  -> lower scope-local target-token topology
  -> resolve output/query metadata and requested regions
  -> resolve cache substitutions/captures
  -> execute the complete request once
  -> commit entry bounds/cache state
```

Record one ordered fragment DAG across top-level roots, then lower a separate target-token chain inside each root, finite Layer, and non-empty symbolic TargetLayerScope. Resolve `TargetLayerScope(Full)` only after every enclosing transform/clip/scope map is known. An empty TargetLayerScope remains ordered but creates no local target or pixel work. Do not append commands to an early global side list: it loses `[A, Clear, B]`, lets child commands escape Layers, and breaks Snapshot/Clear/DrawBackdrop ordering.

Convert bounds and hit testing to metadata-only request purposes. They perform no GPU/media execution or persistent frame-cache mutation, emit their own internal `Metadata` diagnostic snapshots, and never replace internal `LatestFrame` or frame render counts.

## 6. Add post-record ROI and cache resolution

Never pass a resolved per-node ROI into `Process`. After the graph is complete:

1. acquire and validate a finite real destination domain, or require explicit target-less `TargetDomain` for every root Full access; self-bounded graphs without Full need no separate domain;
2. lower scope-local target-token topology, resolve symbolic scope regions, and discover complete preceding-token dependencies; fail here if a reachable Full access lacks a finite owning domain;
3. resolve forward value bounds, `RootOutputExtent`, separate `QueryBounds`, density, and hit-test metadata;
4. seed the root requirement from non-null `RequestedRegion` or otherwise `RootOutputExtent`, then propagate requirements backward;
5. use full input for unknown mappings;
6. carry only explicit `Full`, `Empty`, or finite `Region(Rect)` states and reject invalid rectangles;
7. keep the final requirement/commit crop separate from the available root, finite Layer, or resolved TargetLayerScope target domain so target-read ROI may expand to that domain;
8. resolve cache candidates, bypassing raw target work and target-dependent whole subtrees whose complete prior-token identity/coverage is not proven;
9. substitute hits with materialized execution inputs while retaining original query metadata and insert miss capture points into the current schedule;
10. publish captures only after complete success.

Run shifted/full/empty ROI, cache hierarchy, density eligibility, static-prefix/animated-tail, and query-isolation tests before adding fusion.

## 7. Add shared Shader and Geometry descriptions

Implement the minimal public opt-in:

```csharp
public sealed class FilterEffectContext
{
    public void Shader(ShaderDescription description);
    public void Geometry(GeometryDescription description);
}
```

The same descriptions are accepted by `RenderNodeContext`. Keep every existing `ApplyTo` method and item order.

Extract the donor lexer/merger/binding/bounds/session algorithms only after applying these corrections:

- remove author-asserted whole-source invariance;
- reject coordinate access and unsupported grammar in CurrentPixel source;
- define CurrentPixel as operating after upstream analytic/antialiased coverage is resolved. Coordinate validation does not prove `f(kx) = kf(x)`, so arbitrary public stages must not fold into vector/text/path/AA-clip coverage generation; only engine-known mechanically proven coverage-homogeneous operations may cross, with no public assertion flag;
- resolve child/native Shader resources only at execution;
- compare full source/signature after hash lookup;
- add sampler/child/backend budget checks;
- add active-token guards to retained Geometry session/input use;
- make callback canvas use one-shot, composition-global, canonically rounded, clipped, and no-flush-on-close;
- reject every guarded callback path that tries `SaveLayer`-backed opacity/blend/mask/paint, hidden allocation, or nested rendering;
- share one `RenderExecutionInput` facade and require owning descriptions to declare target/input readback separately;
- separate callback scalar runtime/cache identity from structural identity, defaulting to request-unique;
- detach code from effect-private planner/cache/executor types.

Verify public authoring through the non-friend `Beutl.PublicApiContractTests` project.

## 8. Prove the first renderer-wide fusion seam

Start from a deterministic coverage-resolved semitransparent materialized source and build this exact distinct-node topology:

```text
Gamma CurrentPixel Shader -> OpacityRenderNode -> Invert CurrentPixel Shader
```

With fusion disabled, confirm baseline parity and three semantic stages in the recorded request. With fusion enabled, require:

- `CanBeUsedAsValueInput == true` on the Opacity result so the second Shader is accepted;
- one execution island for the eligible run;
- exactly one planned/executed GPU pass and one compiled fused program selected; after warm-up this is a program-cache hit and creates no new program;
- at most one intermediate target;
- no per-stage synchronization;
- SSIM >= 0.99, RGB MAE <= 0.02, alpha MAE <= 0.02;
- non-vacuity when each stage is disabled.

Then insert each required barrier and assert the exact deterministic split.

In particular, apply a valid non-coverage-homogeneous CurrentPixel transform such as `color * color.a` after an antialiased thin line/path. Require a materialization boundary between coverage production and the Shader run, and compare the edge band with local-MAE and maximum-channel-error thresholds. Whole-frame averages alone are not sufficient for this control.

## 9. Add structural/program caches and pooled resources

Only after the uncached planner is correct:

- cache structural plans without parameter values/resource contents;
- resolve/clamp concrete density from complete output bounds during recording, then crop ROI without changing it;
- bind final cropped bounds/device values at execution;
- cache programs with complete equality after hash bucketing;
- schedule exact-size RGBA16F leases by first/last use;
- carry actual clamped density downstream;
- preserve root `OutputScale`, `MaxWorkingScale`, and the 16,384-axis clamp.

Required warmed checks:

```text
100 parameter frames -> 1 structural compile, 0 later program creations
1 structural toggle  -> 1 affected replacement compile
stable target sizes  -> 0 new targets, 0 pool misses
10-stage linear run  -> peak live no greater than equivalent 3-stage run
```

## 10. Finish boundaries and failure behavior

Record 3D as an opaque backend source and render it later into one materialized 2D value. Record separate-target nested work before GPU execution and inherit the parent request owner/options. Provide an unfused supported path for every public Shader description.

Run failure injection around transactions, analysis, cache, materialization, Shader compile/bind, Geometry/target/input readback, opaque dynamic outputs, nested/3D, target commands/captures/scopes, raw callbacks, brush-mask lowering, cache publication, and cleanup. Every acquisition must be discharged exactly once by release/disposal or successful cache ownership transfer; every run rejects retained facade use, publishes no partial cache, and preserves the primary exception.

## 11. Run final validation

```bash
dotnet format Beutl.slnx --verify-no-changes
dotnet build Beutl.slnx
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings
```

Run the ordinary fallback gate on every host:

```bash
dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj \
  -f net10.0 --filter "FullyQualifiedName~ShaderFallbackTests"
```

On a configured capable graphics host, run both GPU-required projects explicitly:

```bash
BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj \
  -f net10.0 \
  --filter "(TestCategory=GpuPassFusionGpu|FullyQualifiedName~GpuGoldenSuiteCanaryTests)"

BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj \
  -f net10.0 --filter "TestCategory=GpuPassFusionGpu"
```

Run the paired persistent-lifetime benchmark in the pinned baseline and feature worktrees on the same system:

```bash
dotnet run -c Release --project tests/Beutl.Benchmarks -- \
  --filter '*RenderPipelineBenchmarks*'
```

Record raw results, SHAs, environment, controls, confidence intervals, and request counters. The primary warmed post/pre median ratio's 95% confidence interval must lie below 1.0; donor percentages are not acceptance thresholds.

Finally run the public-design and repository boundary reviews. The public migration commit must be breaking and name `Beutl.Engine`, `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream custom render-node authors in its `BREAKING CHANGE:` footer.
