# Baseline & SC-005 Re-pin Notes

**Feature**: `004-gpu-pass-fusion` | **Purpose**: single source of truth for the SC-005 numbers.

This file records the measured effect-pipeline counters and median frame time for the four O3 scenes
(contracts/observability.md O3), captured by `tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs`.
The **legacy** column (rollout step 1, T010) is measured against the unmodified pipeline *before* any
behavior change; the **after** column (rollout step 6, T053) is measured against the fused pipeline. Numbers
are per-machine and are not CI-gated (the counter-based SC-001/002/003 gates are exact and live in NUnit).

The four counters are defined in [contracts/execution-plan.md §C8](../contracts/execution-plan.md); the
scenes and their fixed-seed builders are in
`tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/SceneFixtures.cs`.

## Legacy baseline (step 1 — TODO: fill from the T010 benchmark run)

| Scene | Size | GpuPasses | TargetAllocations | FullFrameMaterializations | FlushSyncs | Median frame time |
|---|---|---|---|---|---|---|
| ColorChain | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| ColorChain | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| MixedChain | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| MixedChain | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| SplitTree | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| SplitTree | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| HeavySource | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| HeavySource | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |

## After redesign (step 6 — TODO: fill from the T053 benchmark run)

| Scene | Size | GpuPasses | TargetAllocations | FullFrameMaterializations | FlushSyncs | Median frame time |
|---|---|---|---|---|---|---|
| ColorChain | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| ColorChain | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| MixedChain | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| MixedChain | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| SplitTree | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| SplitTree | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| HeavySource | 1920×1080 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |
| HeavySource | 3840×2160 | _TBD_ | _TBD_ | _TBD_ | _TBD_ | _TBD_ |

## SC-005 re-pinned targets

Per [research.md D11](../research.md), the spec's provisional SC-005 targets (≥ 60% reduction in
passes/allocations/flushes on the benchmark color chain; ≥ 20% median frame-time improvement) stay
provisional until the step-1 legacy baseline above lands. Once measured, pin the final targets here and
update spec.md SC-005 if the measured headroom differs materially. The counter-based criteria
(SC-001/002/003) are exact and are **not** subject to tuning.

| Metric | Provisional target | Re-pinned target | Basis (scene / measured headroom) |
|---|---|---|---|
| GpuPasses reduction | ≥ 60% | _TBD_ | ColorChain, from legacy baseline |
| TargetAllocations reduction | ≥ 60% | _TBD_ | ColorChain, from legacy baseline |
| FlushSyncs reduction | ≥ 60% | _TBD_ | ColorChain, from legacy baseline |
| Median frame-time improvement | ≥ 20% | _TBD_ | ColorChain, from legacy baseline |
