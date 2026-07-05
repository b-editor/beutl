# Contract: Observability & Benchmarks

**Feature**: `004-gpu-pass-fusion` | Binding on: `PipelineDiagnostics`, the NUnit counter assertions, and `tests/Beutl.Benchmarks`

## O1. Counters

Names, increment rules, and threading are fixed by [execution-plan.md §C8](./execution-plan.md). Additional requirements:

1. Counters exist and are correct on the **legacy** pipeline first (rollout step 1): `Flush` → `FullFrameMaterializations`; effect-path `RenderTarget.Create` → `TargetAllocations`; `PrepareForSampling`/`BeginDraw` sync pairs → `FlushSyncs`; per-materialization draw → `GpuPasses`. Baseline numbers are recorded in `notes/baseline.md` before any behavior change.
2. Overhead: unconditional `long` increments on the render thread; no locks, no allocation, no I/O. No "enable" flag is needed at this cost; if a future counter needs more, it must be gated.
3. Exposure: `IRenderer`-level access for tests (`renderer.Diagnostics.Snapshot()`); not serialized, not UI-facing in this feature.

## O2. CI-enforced assertions (NUnit, next to the golden suite)

| Test | Asserts |
|---|---|
| Fusion count | N ≥ 3 invariant color chain ⇒ `GpuPasses == 1`, intermediates ≤ 1 (SC-001) |
| Plan cache | 100 animated frames ⇒ `PlanCompilations == 1`, `ProgramCreations` after frame 1 == 0 (SC-002) |
| Pool steady state | frames 2..K of a static-structure scene ⇒ `TargetAllocations` delta == 0 (SC-003) |
| Peak intermediates | 10-effect chain peak-live ≤ same-shape 3-effect chain (SC-003/FR-007) |
| Sync minimality | `FlushSyncs` == count of backend transitions in the schedule (C4.2) |
| Mixed chain | Blur→Gamma→Invert→DropShadow→LUT ⇒ pass/allocation counts strictly below the recorded legacy baseline (US1-AS2) |
| Animated bounds | blur-sigma animation over 100 frames ⇒ `PlanCompilations == 1`, sizes re-resolved per frame (C5) |
| Clamp carry parity | a chain whose inflated bounds trigger the 16 384 px clamp renders with legacy-parity densities (monotonic `w` carry — C3.2, FR-012) |
| Structural threshold | animating SplitEffect division counts ⇒ one recompile per topology change, no stale reuse (C3.6/C5) |
| Allocation failure | forced pool failure on fused / geometry-session / compute paths ⇒ preview drops + continues, delivery throws (C7 normalization) |

## O3. Benchmark suite (`tests/Beutl.Benchmarks/Rendering/EffectPipelineBenchmarks.cs`)

- BenchmarkDotNet, excluded from `dotnet test` (constitution III); runnable locally on macOS/Windows and on the Linux CI runner in software mode (numbers are per-machine, not CI-gated).
- Scenes (fixed seeds, 1920×1080 and 3840×2160 variants): **ColorChain** (5 invariant color effects), **MixedChain** (Blur→Gamma→Invert→DropShadow→LUT), **SplitTree** (split → per-branch effects → composite), **HeavySource** (4K bitmap through 3 shader effects).
- Report: median frame time + final counter snapshot per scene. The step-1 run against the legacy pipeline is the SC-005 baseline; the step-4/5 runs demonstrate the ≥ 60% counter and ≥ 20% time targets (numbers re-pinned in `notes/baseline.md` per research D11).

## O4. Parity gates (golden suite reuse)

- Every migrated effect: single-effect golden test at output scale 1.0 vs a **frozen pre-redesign reference render** (generated at step 1 and stored with the existing harness conventions), thresholds `ExactSsimMin = 0.99` / `ExactMaeMax = 0.02` (linear RGBA16F). Parity scenes MUST include semitransparent content so premultiplied-alpha handling of fused shader/color-filter interleavings is exercised (data-model color/alpha contract).
- Representative chains (the O3 scenes minus HeavySource) get the same treatment.
- The existing 003 golden suites must stay green untouched throughout the rollout (FR-019, SC-004).
