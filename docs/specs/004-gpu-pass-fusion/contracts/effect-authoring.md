# Contract: Effect Authoring Surface

**Feature**: `004-gpu-pass-fusion` | Binding on: `Beutl.Graphics.Effects` public API (plugin-facing)

This contract defines what an effect author (built-in, plugin, or script) may rely on after the redesign. It replaces the imperative `ApplyTo`/`CustomFilterEffectContext` contract.

## A1. Describe, don't execute

- `FilterEffect.Describe(EffectGraphBuilder, Resource)` is invoked by the engine whenever the effect's graph may be needed. It MUST be side-effect-free apart from appending descriptors: no rendering, no target allocation, no GPU calls, no flushes. It MAY be called every frame and MUST be cheap (descriptor construction only).
- All animated values MUST be read from the passed `Resource` (the existing capture pattern), never from live properties.

## A2. Node kinds available to authors

| Kind | Author provides | Engine guarantees |
|---|---|---|
| `ShaderNode` (snippet) | SKSL `half4 apply(half4 c)` + uniforms/samplers, `IsCoordinateInvariant = true` | participates in fusion; executes exactly once per output pixel of the fused pass ROI |
| `ShaderNode` (whole-source) | SKSL `main` with `src` child | own pass (or fusion input boundary); `src` is the upstream result |
| `ColorFilterNode` | `SKColorFilter` factory | fused with adjacent invariant nodes into the same draw |
| `SkiaFilterNode` | `SKImageFilter` factory + `BoundsContract` | grouped with adjacent Skia filter nodes into one filtered draw |
| `ComputeNode` | GLSL pass set, structural pass count, declared no-Vulkan fallback | executor schedules passes, provides ping-pong/depth textures from the pool, applies push constants |
| `GeometryNode` | `Action<GeometrySession>` + mandatory `BoundsContract` | session canvas + read-only inputs; executor owns target, ROI, sync |
| `SplitNode`/`CompositeNode` | branch count / composite op | fusion never crosses; branches schedule independently |

## A3. Bounds & ROI obligations

- Every non-invariant node MUST declare `TransformBounds` (forward) and `GetRequiredInputBounds` (backward). Backward MUST cover every input texel the node samples for a given output region; the engine MAY render inputs cropped to exactly that region.
- A node that cannot compute bounds until execution declares `IsRenderTimeResolved`; the engine then uses full input bounds (no ROI benefit) — correctness is preserved, performance is the author's loss.
- Coordinate-invariant nodes get identity bounds by construction and MUST NOT sample any coordinate other than the current pixel (this is what makes fusion sound). Violating this produces wrong output *by contract* — the engine does not detect it at runtime (debug parity tests do).

## A4. Structure vs parameters

- Descriptor fields marked **structural** (shader source identity, pass counts, branch counts, invariance flags, bounds-contract identity) MUST only change when the effect meaningfully restructures; the engine recompiles on any structural change (exactly once, FR-010).
- Everything else (uniform values, colors, matrices, LUT texture *contents*) is a **parameter**: changing it MUST NOT change the compiled plan. Authors MUST NOT encode parameters into shader source strings (that would defeat the program cache and force recompiles).
- Bounds MAY depend on parameters (an animated blur radius inflating `TransformBounds` is normal): bounds, ROIs, and buffer sizes are re-resolved every frame and are **not** structural — only the graph's *shape* is. A parameter that changes the *number or kind* of nodes/passes/branches is structural and must be declared as such.

## A5. Scale semantics (003 carry-over)

- The builder exposes `OutputScale` and the resolved `WorkingScale`; the canvas CTM in `GeometrySession` maps logical→device at the working density. The coordinate-space rule from `docs/specs/003-resolution-independent-pipeline/contracts/effect-scale-contract.md` applies unchanged: logical-space geometry under CTM is not manually scaled; device-buffer values scale by `w` exactly once.
- Shader ROIs and resolution uniforms are device-space at the pass's working scale; snippet nodes never see coordinates and need no scale handling.
- An effect needing a non-supply working scale still overrides `FilterEffect.Resource.CreateRenderNode()` (unchanged 003 seam).

## A6. Script surfaces

- `SKSLScriptEffect`: script SKSL is a whole-source `ShaderNode`; an explicit `CoordinateInvariant` opt-in property makes it a fusable snippet (author asserts A3's single-pixel rule).
- `GLSLScriptEffect`: unchanged authoring semantics, now a `ComputeNode`.
- `CSharpScriptEffect`: script globals expose the `GeometrySession` surface (breaking for existing user scripts, maintainer-approved 2026-07-05). A legacy script written against the removed imperative surface MUST fail at script compile time with a clear diagnostic pointing at the migration guide — it never silently renders wrong output. Migration table in [breaking-changes.md](./breaking-changes.md).

## A7. Failure semantics visible to authors

- Pool/allocation failure inside a pass: preview → the pass's output is dropped (effect chain degrades exactly as today's `Flush` failure); export → `InvalidOperationException` propagates. Authors MUST NOT catch-and-continue inside `GeometrySession`.
- `ComputeNode` on a context without Vulkan support: the declared fallback (`Identity`/`Skip`/`CpuCallback`) applies; authors MUST declare one.
