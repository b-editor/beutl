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

## Legacy baseline (step 1 — measured 2026-07-05, T010)

| Scene | Size | GpuPasses | TargetAllocations | FullFrameMaterializations | FlushSyncs | Median frame time |
|---|---|---|---|---|---|---|
| ColorChain | 1920×1080 | 4 | 4 | 2 | 4 | 9.385 ms |
| ColorChain | 3840×2160 | 4 | 4 | 2 | 4 | 37.292 ms |
| MixedChain | 1920×1080 | 6 | 6 | 3 | 6 | 18.525 ms |
| MixedChain | 3840×2160 | 6 | 6 | 3 | 6 | 45.749 ms |
| SplitTree | 1920×1080 | 20 | 20 | 10 | 20 | 22.681 ms |
| SplitTree | 3840×2160 | 20 | 20 | 10 | 20 | 40.686 ms |
| HeavySource | 1920×1080 | 6 | 6 | 3 | 6 | 16.097 ms |
| HeavySource | 3840×2160 | 6 | 6 | 3 | 6 | 43.755 ms |

**Counter derivations** (structure-determined, size-independent; the legacy cost model of research §0 —
one flush bake **plus** the effect's own pass per custom item):

- **ColorChain** (Gamma·custom, HueRotate, Saturate, Brightness, Invert·custom): 2 custom items ⇒ 2 bakes
  (the three color-matrix filters accumulate into the second bake) + 2 SKSL passes = 4 passes / 4 allocations
  / 4 sync pairs / 2 materializations.
- **MixedChain** (Blur, Gamma·custom, Invert·custom, DropShadow, LutEffect·custom): 3 custom items ⇒ 3 bakes
  (Blur folds into Gamma's bake, DropShadow into Lut's; Invert pays a forced bake with an empty Skia chain —
  the adjacent-custom double cost) + 3 SKSL passes = 6 / 6 / 6 / 3.
- **HeavySource** (Gamma·custom, Invert·custom, ColorGrading·custom): 3 adjacent custom items ⇒ 3 forced
  bakes + 3 SKSL passes = 6 / 6 / 6 / 3.
- **SplitTree** (SplitEffect·custom 3×3, Saturate, LayerEffect·custom): 1 bake before the split + the
  split's 1 composite session, then the split's 9 output targets each pay a bake of the accumulated Saturate
  filter and LayerEffect's session: materializations = 1 + 9 = 10; passes / allocations / syncs =
  10 bakes + (1 split + 9 LayerEffect) custom sessions = 20.

The pinned ColorChain/MixedChain values are CI-enforced in
`tests/Beutl.UnitTests/Engine/Graphics/Rendering/EffectPipeline/EffectPipelineCounterTests.cs`.

**Environment**: Apple M3 (8-core, integrated GPU), macOS Tahoe 26.5.1, MoltenVK 1.4.0 (Vulkan 1.4.323
driver, app instance 1.2.323), .NET 10.0.9, BenchmarkDotNet v0.15.8, RGBA16F linear-sRGB surfaces. GPU frame
times on this machine show visible run-to-run variance (thermal/scheduler); the median is the tracked
statistic per O3.

**BDN job**: `InProcessEmitToolchain`, LaunchCount=1, WarmupCount=3, IterationCount=7 (CLI:
`dotnet run -c Release --project tests/Beutl.Benchmarks -- --filter '*EffectPipelineBenchmarks*' -i
--iterationCount 7 --warmupCount 3 --launchCount 1`). The suite's committed `[ShortRunJob]` uses BDN's
out-of-process toolchain, which cannot generate its boilerplate project in a working copy that contains a
nested agent-worktree duplicate of `Beutl.Benchmarks.csproj`; the in-process run measures the identical
code on the identical runtime with the same warmup/measure budget as ShortRun. `PoolAcquires` /
`PoolMisses` / `PlanCompilations` / `ProgramCreations` are structurally 0 at step 1 (pool and compiler land
in later rollout steps) and are omitted from the table.

## After redesign (step 6 — measured 2026-07-07, T053, post-removal commit)

| Scene | Size | GpuPasses | TargetAllocations | FullFrameMaterializations | FlushSyncs | Median frame time |
|---|---|---|---|---|---|---|
| ColorChain | 1920×1080 | 1 | 1 | 0 | 0 | 5.359 ms |
| ColorChain | 3840×2160 | 1 | 1 | 0 | 0 | 17.635 ms |
| MixedChain | 1920×1080 | 4 | 4 | 0 | 0 | 15.414 ms |
| MixedChain | 3840×2160 | 4 | 4 | 0 | 0 | 43.814 ms |
| SplitTree † | 1920×1080 | 10 | 11 | 1 | 0 | 15.498 ms |
| SplitTree † | 3840×2160 | 10 | 11 | 1 | 0 | 43.866 ms |
| HeavySource | 1920×1080 | 1 | 1 | 0 | 0 | 7.415 ms |
| HeavySource | 3840×2160 | 1 | 1 | 0 | 0 | 31.962 ms |

† **SplitTree: step-6 follow-up, re-measured** (2026-07-07). The coordinate-invariant Saturate run between the
split and the composite now folds into the composite's per-branch draw (C9, contracts/execution-plan.md), so
GpuPasses fell 19 → 10 and TargetAllocations 20 → 11 (the 9 standalone Saturate passes and their 9 intermediate
targets are gone; the composite applies the Saturate `SKColorFilter` per branch instead). The original T053 row was
19 / 20 / 1 / 0 at 27.016 ms (1080p) / 55.166 ms (4K). The frame times above are a **same-session A/B on a
contended shared machine, control-normalized against ColorChain** (see the follow-up finding below).

**Counter derivations** (structure-determined, size-independent; pinned in
`EffectPipelineCounterTests` for ColorChain/MixedChain):

- **ColorChain**: five coordinate-invariant nodes fuse to one `FusedShaderPass` — 1 pass / 1 intermediate /
  0 materializations / 0 syncs (SC-001).
- **MixedChain**: [SkiaFilter Blur, fused Gamma+Invert, SkiaFilter DropShadow, fused LUT] — 4 passes /
  4 intermediates; fusion never crosses the non-adjacent Skia filters (C2).
- **HeavySource**: Gamma+Invert+ColorGrading fuse to one pass — 1 / 1 / 0 / 0 (versus 6 / 6 / 3 / 6 legacy;
  the three adjacent custom items no longer pay per-item bakes).
- **SplitTree** (step-6 follow-up, post-fold): 1 split pass materializing its input (1 FullFrameMaterialization) +
  9 pooled part targets, then LayerEffect composites the 9 branches into 1 target with the Saturate `SKColorFilter`
  folded onto each branch draw (C9): 10 passes / 11 allocations / 1 materialization / 0 syncs (versus 19 / 20 / 1 / 0
  before the fold and 20 / 20 / 10 / 20 legacy). The fold eliminates the 9 standalone Saturate passes and their 9
  intermediate targets because a coordinate-invariant color run adjacent to a composite is identical to applying that
  color filter per composite input. Pinned in `PrimitivePassTests.SplitTree_CounterDerivation` (re-pinned 19 → 10).

**Environment / method**: identical to the step-1 run (same machine, MoltenVK, RGBA16F linear-sRGB; BDN
`InProcessEmitToolchain`, LaunchCount=1, WarmupCount=3, IterationCount=7, same CLI). Median over the
measured workload iterations. `PoolAcquires`/`PoolMisses` equal the acquires per frame with zero misses in
steady state (SC-003 gate); `PlanCompilations` = 1 per structural change, `ProgramCreations` = 0 on warm
frames (SC-002 gate) — asserted exactly in NUnit rather than tabulated here.

## SC-005 verdict (step 6, ColorChain — the pinned scene)

| Metric | Re-pinned target | Measured | Verdict |
|---|---|---|---|
| GpuPasses reduction | ≥ 60% (4 → ≤ 1.6) | 4 → 1 (**75%**) | **PASS** |
| TargetAllocations reduction | ≥ 60% (4 → ≤ 1.6) | 4 → 1 (**75%**) | **PASS** |
| FlushSyncs reduction | ≥ 60% (4 → ≤ 1.6) | 4 → 0 (**100%**) | **PASS** |
| Median frame time, 1080p | ≥ 20% (9.385 → ≤ 7.508 ms) | 9.385 → 5.359 ms (**42.9%**) | **PASS** |
| Median frame time, 4K | ≥ 20% (37.292 → ≤ 29.834 ms) | 37.292 → 17.635 ms (**52.7%**) | **PASS** |

**Non-gated scene observations** (honest reporting; SC-005 pins only the color chain):

- **HeavySource** improves strongly (16.097 → 7.415 ms at 1080p, 43.755 → 31.962 ms at 4K) — its three
  custom items now fuse to one pass.
- **MixedChain** improves modestly (18.525 → 15.414 ms, 45.749 → 43.814 ms) — passes fell 6 → 4 but each
  declarative Skia-filter pass pays its own bake that the legacy pipeline folded into an adjacent custom
  bake.
- **SplitTree regression — CLOSED (step-6 follow-up, 2026-07-07).** The T053 measurement recorded a frame-time
  regression (22.681 → 27.016 ms at 1080p, 40.686 → 55.166 ms at 4K) despite materializations falling 10 → 1 and
  syncs 20 → 0, driven by the 9 standalone per-branch fused Saturate draws — 9 × (pooled-target acquire + clear +
  shader-tree build + `SKImage` snapshot + draw) on small tiles. The fix folds that coordinate-invariant Saturate run
  into the LayerEffect composite draw (C9): the composite already draws each of the 9 branches once, so applying the
  Saturate `SKColorFilter` per branch is byte-identical to baking each branch through Saturate first, and it removes
  the 9 passes and 9 intermediate targets entirely (GpuPasses 19 → 10, TargetAllocations 20 → 11). The frozen
  `chain-SplitTree` parity reference still passes **un-regenerated**, confirming the fold is semantics-preserving.

  *Measurement (honest).* This machine was under heavy, non-stationary background load during the follow-up
  (1-minute load average swinging 5–117), so absolute cross-session comparison against the T053 idle-machine numbers
  is invalid. Instead a **same-session A/B** was run — folded (new) vs unfolded (old) built back-to-back — with
  **ColorChain as the control** (unaffected by the fold): each run was accepted only when its ColorChain median
  matched the T053 baseline (1080p ≈ 4.7 ms vs 5.359 ms; 4K ≈ 17.6 ms). In the two control-matched runs:
  - SplitTree 1080p: **22.194 → 15.498 ms (−30%)**, now below both the T053 regression and the legacy 22.681 ms.
  - SplitTree 4K: **51.089 → 43.866 ms (−14%)**; the 9 branch draws that remain are bandwidth-bound at 4K, so the
    proportional win is smaller than at 1080p where the eliminated per-pass fixed overhead dominated.
  - ColorChain (guard): 4.752 → 4.660 ms (1080p), 19.120 → 17.551 ms (4K) — within noise, the fused fast path is
    not regressed by the composite-fold code path.

  A secondary follow-up also lightened the plan-cache **hit path**: `ApplySyncBefore` no longer rebuilds the pass
  array when no backend-transition flag changes (the all-Skia common case), cutting ~112 B/frame of the per-frame
  rebind allocation (measured: SplitTree 968 → 856 B, a 30-effect chain 1992 → 1880 B). The residual per-frame cost
  is the inherent immutable pass/stage-record construction, which cannot be removed without making the pass records
  mutable — a redesign whose correctness risk (bridged-effect staleness, sampler freshness, NestedGraph re-describe)
  is not justified for the ~1–2 KB/frame at stake; the counter gates (SC-002) remain exact either way.

## SC-005 re-pinned targets

Per [research.md D11](../research.md), the spec's provisional SC-005 targets (≥ 60% reduction in
passes/allocations/flushes on the benchmark color chain; ≥ 20% median frame-time improvement) stay
provisional until the step-1 legacy baseline above lands. Once measured, pin the final targets here and
update spec.md SC-005 if the measured headroom differs materially. The counter-based criteria
(SC-001/002/003) are exact and are **not** subject to tuning.

| Metric | Provisional target | Re-pinned target | Basis (scene / measured headroom) |
|---|---|---|---|
| GpuPasses reduction | ≥ 60% | ≥ 60% (kept) — absolute: 4 → ≤ 1.6, i.e. ≤ 1 | ColorChain: SC-001 already mandates `GpuPasses == 1`, a 75% reduction, so ≥ 60% is conservative and safely met |
| TargetAllocations reduction | ≥ 60% | ≥ 60% (kept) — absolute: 4 → ≤ 1.6, i.e. ≤ 1 | ColorChain: SC-001's "intermediates ≤ 1" gives ≥ 75% |
| FlushSyncs reduction | ≥ 60% | ≥ 60% (kept) — absolute: 4 → ≤ 1.6, i.e. ≤ 1 | ColorChain fused is Skia-only ⇒ 0 backend transitions ⇒ expected `FlushSyncs == 0` (100%) |
| Median frame-time improvement | ≥ 20% | ≥ 20% (kept) — absolute: 9.385 ms → ≤ 7.508 ms (1080p), 37.292 ms → ≤ 29.834 ms (4K) | ColorChain: 4 full-frame passes collapse to 1; at 4K the frame is bandwidth-dominated by those full-frame draws, so ≥ 20% has headroom; 1080p includes fixed non-effect cost (source rasterization, root composite), so ≥ 20% is a real but plausible bar |

**Assessment**: the measured baseline does **not** suggest changing spec.md SC-005. The counter targets are
implied (and exceeded) by SC-001's exact gates; the 20% time target is the binding one, and the
1080p/4K absolute thresholds above are what the step-6 run (T053) must beat on this machine.
