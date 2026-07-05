# Implementation Plan: Declarative Effect Graph with GPU Pass Fusion

**Branch**: `yuto-trd/integrate-gpu-pass` | **Date**: 2026-07-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/004-gpu-pass-fusion/spec.md`

## Summary

Replace the imperative filter-effect pipeline (`FilterEffectContext` recording + `FilterEffectActivator` replay, with per-custom-effect RGBA16F target allocation, snapshots, and uncoordinated GPU sync) with a **declarative effect graph** compiled to a cached **execution plan**. Technical approach (research.md): effects describe node descriptors via `EffectGraphBuilder`; a compiler fuses maximal runs of coordinate-invariant color nodes into **one Skia draw** (shader composition: image shader → `WithColorFilter` wraps → nested `SKRuntimeEffect` child shaders, plus SKSL snippet merging), groups adjacent Skia image filters, schedules compute (Vulkan) and geometry (canvas) passes, plans intermediates explicitly, and a `PlanExecutor` runs the plan with a render-target pool and sync only at backend transitions. Plans are cached on a structural key (parameters → uniform-only updates); counters + benchmarks land first so every step is measured; the imperative public surface is removed at the end (`refactor!` + `BREAKING CHANGE:`).

## Technical Context

**Language/Version**: C# (`LangVersion: preview`), .NET dual-target `net10.0` / `net10.0-windows`

**Primary Dependencies**: SkiaSharp (`SKSurface` RGBA16F linear, `SKRuntimeEffect`/SKSL, `SKColorFilter`, `SKImageFilter`), Silk.NET.Vulkan (compute/3D backend, `GLSLFilterPipeline`, SPIR-V compile), existing `Beutl.Engine` render-node infrastructure (003 scale model)

**Storage**: N/A (no serialization-format changes; project files untouched)

**Testing**: NUnit + Moq (`tests/Beutl.UnitTests`, golden-image harness `Engine/Graphics/Rendering/Golden/` with SSIM/MAE over linear RGBA16F); BenchmarkDotNet (`tests/Beutl.Benchmarks`, excluded from `dotnet test`)

**Target Platform**: Windows / macOS / Linux desktop; CI runs GPU-less (SwiftShader / raster) — every plan must execute on the Skia raster backend

**Project Type**: Engine library redesign inside a desktop application (`src/Beutl.Engine`; no UI work)

**Performance Goals**: N-effect invariant color chain = 1 GPU pass / ≤ 1 intermediate; 0 plan compilations & 0 program creations per frame at steady state; 0 steady-state target allocations (pool); benchmark chain: ≥ 60% fewer passes/allocations/flushes, ≥ 20% median frame-time improvement (provisional until the step-1 baseline, research D11)

**Constraints**: 003 scale semantics frozen (FR-012); golden parity SSIM ≥ 0.99 / MAE ≤ 0.02 at scale 1.0; preview-degrade / export-throw allocation semantics preserved (FR-015); GPL/MIT boundary untouched; no `[Obsolete]` shims; rollout order binding (FR-020)

**Scale/Scope**: 42 `FilterEffect` subclasses migrate — 41 in `Beutl.Engine` (incl. the 3 script effects, `FilterEffectGroup`, `FallbackFilterEffect`, `FilterEffectPresenter`) plus `NodeGraphFilterEffect` (research §0 census); ~2 new public authoring type clusters, ~6 internal machinery types; public-API breaking change for plugin/script authors

## Constitution Check

*GATE: evaluated pre-Phase 0 and re-evaluated post-Phase 1 design — PASS (no violations, no Complexity Tracking entries).*

| Principle | Assessment |
|---|---|
| I. License Firewall | **Pass** — feature is entirely `Beutl.Engine` graphics; no FFmpeg/IPC/worker surface involved (explicit non-break in contracts/breaking-changes.md). |
| II. Dual-Target | **Pass** — no new TFM; SkiaSharp/Silk.NET usage unchanged in kind; both targets keep building at every rollout step. |
| III. Test-First NUnit | **Pass** — counters land first with NUnit assertions (contracts/observability.md §O2); every migrated effect gets a golden parity test vs frozen references; benchmarks stay BenchmarkDotNet-excluded; coverage gate honored per step. |
| IV. Avalonia Compiled Bindings | **N/A** — no XAML/UI changes. |
| V. Style = Linter | **Pass** — `dotnet format` per step; no stylistic edits proposed. |
| VI. Source Generators | **Watch, no change expected** — `FilterEffect.ApplyTo → Describe` changes an abstract method on a generator-consuming type (`EngineObject`/`Resource` pattern) but not generator inputs (properties/attributes). Run `beutl-source-generator-impact` at step 3 before review; `tests/SourceGeneratorTest/` must stay green. |
| Quality Gates | Each rollout step is a PR passing format / build / test+coverage / CI review / no orphaned TODOs. Step 6 (removal) is the single `refactor!` breaking PR. |

**Post-Phase-1 re-check**: design adds no extra projects, no new dependencies, no TFM changes; the public surface change is confined to `Beutl.Graphics.Effects` and documented as a breaking-change contract. PASS.

## Project Structure

### Documentation (this feature)

```text
docs/specs/004-gpu-pass-fusion/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — decisions D1–D11
├── data-model.md        # Phase 1 — authoring/compilation/execution/observability model
├── quickstart.md        # Phase 1 — orientation for implementers
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit-specify)
├── contracts/
│   ├── effect-authoring.md   # Plugin-facing authoring contract (A1–A7)
│   ├── execution-plan.md     # Compiler/executor contract (C1–C8, counter definitions)
│   ├── observability.md      # Counters, CI assertions, benchmark scenes, parity gates
│   └── breaking-changes.md   # Removed→replacement map, behavioral changes, non-breaks
├── notes/
│   └── baseline.md      # Created at rollout step 1: measured baseline + pinned SC-005 targets
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/Beutl.Engine/Graphics/
├── FilterEffects/                      # public authoring surface (existing dir)
│   ├── FilterEffect.cs                 # ApplyTo → Describe (breaking)
│   ├── EffectGraphBuilder.cs           # NEW — replaces FilterEffectContext recording role
│   ├── Nodes/                          # NEW — ShaderNodeDescriptor, ColorFilterNodeDescriptor,
│   │                                   #       SkiaFilterNodeDescriptor, ComputeNodeDescriptor,
│   │                                   #       GeometryNodeDescriptor, Split/CompositeNodeDescriptor,
│   │                                   #       BoundsContract, GeometrySession, EffectInput
│   ├── <41 effect classes>.cs          # migrated per research D7 map (+ NodeGraphFilterEffect in src/Beutl.NodeGraph/)
│   ├── SKSLShader.cs / GLSLShader.cs   # reduced to source/uniform holders (or absorbed)
│   ├── FilterEffectContext.cs          # REMOVED at step 6 (with Activator, CustomFilterEffectContext,
│   │                                   #   EffectTarget(s), SKImageFilterBuilder, FEImpl.cs)
│   └── ...
└── Rendering/                          # internal machinery (existing dir)
    ├── FilterEffectRenderNode.cs       # Process: resolve w → describe → plan cache → execute
    ├── EffectGraph/                    # NEW — EffectGraph, StructuralKey, compiler,
    │                                   #       CompiledPlan/CompiledPass, ResourcePlan,
    │                                   #       PlanExecutor, ParameterBlock, ProgramCache
    ├── RenderTargetPool.cs             # NEW — pooled RGBA16F/depth targets
    └── PipelineDiagnostics.cs          # NEW — counters (wired into legacy path at step 1)

tests/
├── Beutl.UnitTests/Engine/Graphics/Rendering/
│   ├── Golden/                         # existing harness — reused for parity gates
│   ├── EffectGraphCompilerTests.cs     # NEW — fusion grouping, budget splitting, ROI, resource plan
│   ├── EffectPipelineCounterTests.cs   # NEW — O2 assertion table (SC-001/002/003)
│   ├── RenderTargetPoolTests.cs        # NEW — reuse, eviction, leak, failure semantics
│   └── EffectMigrationParityTests.cs   # NEW — per-effect golden parity vs frozen references
└── Beutl.Benchmarks/Rendering/
    └── EffectPipelineBenchmarks.cs     # NEW — O3 scenes, counter+time report
```

**Structure Decision**: Stay inside `Beutl.Engine`'s two existing graphics directories — the public authoring surface next to the effects it serves (`Graphics/FilterEffects/`), the compiler/executor/pool with the render-node machinery (`Graphics/Rendering/EffectGraph/`). No new projects; tests extend the existing per-area test projects (constitution III).

## Phase execution summary

- **Phase 0 (research)** — complete: [research.md](./research.md) resolves all deferred choices (fusion via Skia shader composition + SKSL merging, per-frame describe + structural-key plan cache, exact-size pooled targets, plan-driven sync, two-function ROI contract, the seven-descriptor / five-primitive node taxonomy with the full 42-effect migration map, counters-first rollout, removal strategy). No NEEDS CLARIFICATION remain.
- **Phase 1 (design & contracts)** — complete: [data-model.md](./data-model.md) (entities, validation, cache-invalidation rules, state flow), [contracts/](./contracts/) (authoring A1–A7, execution C1–C8 with normative counter definitions, observability O1–O4, breaking-changes map), [quickstart.md](./quickstart.md). Agent context (`CLAUDE.md` SPECKIT block) updated to point at this plan.
- **Phase 2 (tasks)** — next: `/speckit-tasks` decomposes the six-step rollout (FR-020) into PR-sized tasks with the O2/O4 gates attached per step.
