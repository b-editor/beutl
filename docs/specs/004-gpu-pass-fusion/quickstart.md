# Quickstart: Declarative Effect Graph with GPU Pass Fusion

**Feature**: `004-gpu-pass-fusion` — orient yourself here before touching code; details live in [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), and [contracts/](./contracts/).

## The one-paragraph idea

Effects stop *executing* and start *describing*: `FilterEffect.Describe(EffectGraphBuilder, Resource)` appends node descriptors (seven kinds — shader / color-filter / Skia-filter / compute / geometry / split / composite — realizing the spec's five primitives). A compiler turns the graph into a cached `CompiledPlan` — adjacent coordinate-invariant color nodes collapse into **one draw** via Skia shader composition — and a `PlanExecutor` runs it with pooled render targets and sync only at Skia↔Vulkan boundaries. Structure is the cache key; animated values rewrite uniforms, and bounds/ROIs/buffer sizes are re-resolved every frame (an animated blur radius never recompiles the plan).

## Authoring an effect (after the redesign)

```csharp
// A fusable per-pixel color effect: one SKSL snippet, identity bounds.
public override void Describe(EffectGraphBuilder builder, Resource r)
{
    builder.Shader(ShaderNodeDescriptor.Snippet(
        source: s_gammaSksl,                    // half4 apply(half4 c) { ... }
        uniforms: u => u.Float("gamma", r.Gamma)));
}
```

- Coordinate-invariant snippet ⇒ fuses with neighbors automatically.
- Bounds-changing work declares a `BoundsContract` (forward `TransformBounds` + backward `GetRequiredInputBounds`) — see [contracts/effect-authoring.md](./contracts/effect-authoring.md).
- Imperative composite work (stroke, clipping, C# scripts) uses a `GeometryNode` whose `GeometrySession` gives a canvas + read-only inputs; the executor owns targets/sync.
- Never render or allocate inside `Describe`; never encode parameter values into shader source.

## Where things live

| Concern | Location |
|---|---|
| Authoring surface (public) | `src/Beutl.Engine/Graphics/FilterEffects/` (`EffectGraphBuilder`, descriptors, `GeometrySession`) |
| Compiler / plan / executor / pool (internal) | `src/Beutl.Engine/Graphics/Rendering/` |
| Entry point (unchanged seam) | `FilterEffectRenderNode.Process` — resolves 003 working scale, then describe → cache → execute |
| Counters | `PipelineDiagnostics` per renderer; definitions in [contracts/execution-plan.md §C8](./contracts/execution-plan.md) |
| Counter/parity tests | `tests/Beutl.UnitTests/Engine/Graphics/Rendering/` (+ `Golden/` harness reuse) |
| Benchmarks | `tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs` |

## Hard rules (from the constitution & 003)

- 003 scale semantics are frozen: `ResolveWorkingScale` / `MaxWorkingScale` / 16 384 px clamp produce identical densities (FR-012). Do not touch `docs/specs/003-*` behavior.
- Parity gate = SSIM ≥ 0.99 / MAE ≤ 0.02 vs frozen pre-redesign references, at scale 1.0 — not byte-identity.
- Rollout order is binding (FR-020): counters → pool (no behavior change) → graph + color migration → fusion + caches → spatial/split/compute migration → removal. Every step: build + full tests green.
- No `[Obsolete]` shims; removal ships `refactor!` + `BREAKING CHANGE:` with [contracts/breaking-changes.md](./contracts/breaking-changes.md).
- GPU-less CI must pass: fused passes are plain Skia draws (raster backend runs them); `ComputeNode`s declare a fallback.

## Verify loop

```bash
dotnet build Beutl.slnx
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings   # or /beutl-test <FQN-substring>
dotnet run -c Release --project tests/Beutl.Benchmarks -- --filter '*EffectPipeline*'
```

Baseline numbers (step 1) and re-pinned SC-005 targets: `notes/baseline.md`.
