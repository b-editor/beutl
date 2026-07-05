# Contract: Breaking Changes & Migration Map

**Feature**: `004-gpu-pass-fusion` | Ships with the removal step (rollout step 6) as the `BREAKING CHANGE:` documentation for effect authors.

All changes land as `refactor!:` / `feat!:` Conventional Commits with a `BREAKING CHANGE:` footer naming `Beutl.Engine` (`Beutl.Graphics.Effects`). No `[Obsolete]` shims, no v2 duplicates; in-tree call sites migrate in the same change (AGENTS.md design priorities).

## Removed → replacement

| Removed (public today) | Replacement | Migration note |
|---|---|---|
| `FilterEffect.ApplyTo(FilterEffectContext, Resource)` | `FilterEffect.Describe(EffectGraphBuilder, Resource)` | same append idiom; convenience methods keep their names |
| `FilterEffectContext` (recording surface, `AppendSkiaFilter`, `AppendSKColorFilter`, `CustomEffect<T>`) | `EffectGraphBuilder` (`SkiaFilter`, `ColorFilter`, `Shader`, `Compute`, `Geometry`, `Split`, `Composite` + conveniences) | factories become descriptors; `CustomEffect` callbacks become `GeometryNode` sessions or `ShaderNode`s |
| `CustomFilterEffectContext` (`CreateTarget`, `Open`, `ForEach`, target mutation) | `GeometrySession` (`OpenCanvas`, `Inputs`, scales) | target creation/flushing is executor-owned; multi-target flows become multiple nodes |
| `FilterEffectActivator` | `PlanExecutor` (internal) | no public replacement — execution is engine-owned |
| `EffectTarget` / `EffectTargets` (mutable) | `EffectInput` (read-only) for authors; `ResourcePlan` internally | |
| `SKImageFilterBuilder` (public) | internal compiler detail of `SkiaFilterPass` | |
| `SKSLShader.ApplyToNewTarget(CustomFilterEffectContext, …)` | `ShaderNodeDescriptor` (whole-source or snippet) | `SKSLShader` survives as source/uniform holder only if still needed |
| `GLSLShader.Apply/ApplyMultiPass(CustomFilterEffectContext, …)` | `ComputeNodeDescriptor` | pipeline creation/ping-pong is executor-owned |
| `CSharpScriptEffect` script globals typed on `CustomFilterEffectContext` | globals typed on `GeometrySession` | **breaks user scripts** (maintainer-approved): legacy scripts fail at script compile time with a diagnostic referencing this guide — never silently wrong output; release notes carry a before/after script sample |

## Behavioral changes (allowed by spec)

- Rendered output of migrated shader effects may differ within the golden thresholds (SSIM ≥ 0.99 / MAE ≤ 0.02, linear light) — floating-point rounding of fused programs. Byte-identity is not claimed (spec Assumptions).
- Per-effect intermediate targets, per-effect flushes, and full-frame snapshots between custom effects disappear; any out-of-tree code that observed them (timing, memory) sees different numbers.
- **Allocation-failure behavior is normalized** (FR-015): preview drops the failed pass output and continues; delivery throws. The legacy surface was path-dependent (`Flush` drop-or-throw vs `CreateTarget` returning an empty target whose `Open` threw unconditionally); that divergence is intentionally not reproduced.

> Internal-only removals (`IFEItem`, `FEItem_*`, activator internals — data-model §7) need no plugin migration; this table maps only the public surface.

## Unchanged (explicit non-breaks)

- `FilterEffect` subclassing as the plugin model; `Resource` capture; `Resource.CreateRenderNode()` / custom `FilterEffectRenderNode` overrides (003 seam).
- All effect *properties*, serialization formats, and project files.
- 003 scale semantics (`OutputScale`, `EffectiveScale`, `ResolveWorkingScale`, `MaxWorkingScale`, 16 384 px clamp).
- `NodeGraphFilterEffect` user-facing behavior (internally re-described).
- The GPL/MIT boundary: no FFmpeg/IPC surface is touched by this feature.
