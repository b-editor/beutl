# Implementation Plan: Resolution-Independent Rendering Pipeline

**Branch**: `speckit/003-resolution-independent-pipeline` | **Date**: 2026-05-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/003-resolution-independent-pipeline/spec.md`

## Summary

Thread render scale through Beutl's 2D render-node tree so the *same project* can render at different resolutions: reduced-scale preview for cheap editing, full-scale (or supersampled) export for delivery — the foundation for a future proxy/optimized-media workflow. Today `1 logical unit == 1 device pixel` is hard-wired (`ToSize(1)`, `(int)bounds.Width`, `PixelRect.FromRect(bounds)`); the feature makes all drawable/effect properties **logical** and is **supply-driven**: the render request carries an **output scale `s_out`** (the *final* target only); each operation carries an **`EffectiveScale`** (the density its pixels exist at; vector = `Unbounded`); each effect computes a **working scale `w`** from its inputs — **supply-driven above an `s_out` floor**, `w = min( max(s_out, densest concrete supply), MaxWorkingScale )` (a 2.0 source stays 2.0, a sub-output `At(0.5)` supply is floored up to `s_out`; `s_out` never *clamps* an intermediate from above — a denser supply runs higher, FR-016 — but an effect never runs below `s_out`; *amended 2026-06-15, the earlier "a 0.5 proxy stays 0.5" wording is superseded*; no per-effect policy; an effect needing a different `w` overrides `Process` in a custom `FilterEffectRenderNode`) — and applies the FR-008 coordinate-space rule at `w`: logical-space geometry under the CTM is left unchanged, device-buffer dimensions and device-space shader uniforms are converted once (`× w`), readback-derived geometry converts back (`÷ w`). The root surface becomes `ceil(FrameSize × s_out)` with one `Matrix.CreateScale(s_out)`; an op whose `e ≠ s_out` is resampled once at the final-stage blit, and a global ceiling (`MaxWorkingScale`) bounds the **working scale `w`** (NOT buffer memory — memory scales `area × w²` and is left unbounded by design; see FR-037, and the separate per-buffer dimension clamp `ClampWorkingScaleToBufferBudget` that bounds the per-axis device size). **At `s_out = 1.0`, vector / Skia-filter / unscaled-bitmap output is byte-identical to today** (the regression anchor for that content). *(Amended 2026-06-08: byte-identity is no longer a universal design constraint — the density model is now coherent, so a transform re-scales a bitmap's density and a scaled bitmap into an effect is intentionally not byte-identical; see FR-019 and the requirements.md amendment log.)* The design decisions (D1–D7) are in [research.md](./research.md); types in [data-model.md](./data-model.md); the breaking surface and author contracts in [contracts/](./contracts/).

## Technical Context

**Language/Version**: C# (`LangVersion: preview`), .NET 10

**Primary Dependencies**: Avalonia (UI), SkiaSharp 3.119.2 (`Directory.Packages.props:80`), the engine's Vulkan/SkSL backend; `Beutl.Engine.SourceGenerators` (Roslyn `IIncrementalGenerator`)

**Storage**: No persisted-format change. Render scale is a render-request property, never serialized (FR-001/FR-035/SC-002). Existing `.belm`/`.bobj` load unchanged.

**Testing**: NUnit + Moq. New Vulkan-gated golden-image harness under `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/` (reuses the `VulkanTestEnvironment` skip gate); the SC-008 search test `NoPixelCouplingOnRenderPathTest` was **deferred** (T007 — a naive scan false-positives on the load-bearing `ToSize(1)`/`(int)`-at-`w=1` sites; SC-008 reframed to "no NEW unguarded truncation", completeness carried by the behavioural buffer-activation goldens); `[Explicit]` benchmark + `tests/Beutl.Benchmarks` entry; `tests/SourceGeneratorTest` stays compile-only.

**Target Platform**: cross-platform desktop (`net10.0` + `net10.0-windows`), GPU (Vulkan/MoltenVK) for rendering

**Project Type**: desktop application + engine library (single repo; module-boundary map in AGENTS.md)

**Performance Goals**: reduced-scale preview render-stage time scales ~`s²` for the rasterization-bound portion (SC-003 gate: `median(0.5)/median(1.0) < 0.6`, ratio-based/hardware-independent — note 2026-06-15: this gate is **loose** relative to the `s² ≈ 0.25` ideal because ~38% fixed overhead sits outside the rasterization-bound portion in the committed best case, so it proves **direction**, not the full `s²` value; a **required** source-heavy benchmark variant anchors the supply-driven model's deliberate non-speedup); reduced-scale "exact" effects SSIM ≥ 0.985 vs 1.0 (SC-004); `s=1.0` byte-identical for vector / Skia-filter / unscaled-bitmap content (SC-001)

**Constraints**: `s=1.0` raw-frame byte-identical to the pre-feature renderer for vector / Skia-filter / unscaled-bitmap content (RgbaF16, zero epsilon) — *not* for a scaled bitmap into an effect (FR-019, 2026-06-08 amendment); origins round **toward-zero** (not floor); uniform `float` scale v1 (Vector primitives pre-exist for later widening); no MIT→GPL boundary crossing; no `[Obsolete]` shims; preview scale per-edit-view, non-persisted

**Scale/Scope**: ~28 `RenderNode.Process` overrides reached via one `RenderNodeContext` construction site; ~40 filter effects + particles + audio visualizers adopt the supply-driven scale contract; ~12 breaking public symbols + 1 new value type (`EffectiveScale`; an earlier `ResolutionPolicy` type was added then removed); 6 implementation slices; FR-001..037

## Constitution Check

*GATE: evaluated against `.specify/memory/constitution.md` v0.1.0. Re-checked post-design — still PASS.*

| Principle | Status | Notes |
|---|---|---|
| **I. License Firewall (NON-NEGOTIABLE)** | ✅ PASS | Proxy *decode* (the only GPL/MIT-boundary work) is **out of scope** (FR-025); `MediaOptions` kept additively extensible but no FFmpeg IPC change in 003. GLSL/SKSL effects run **in-process** (MIT Vulkan/SkSL — `SKSLScriptEffect.cs:53` `SKRuntimeEffect`, `GLSLShader.cs:16` shared context), not the GPL `FFmpegWorker`, so the SKSL `iScale` uniform is an MIT-side change (GLSL ships no scale uniform — the author derives `w` from the device-px `Width`/`Height`; see shader-uniforms.md). **No NEW** MIT→GPL `ProjectReference` is introduced (the app-host `Beutl.csproj` conditional worker reference is pre-existing and intentional). |
| **II. Dual-Target Framework** | ✅ PASS | No new TFM; both `net10.0` / `net10.0-windows` keep building. |
| **III. Test-First with NUnit** | ✅ PASS | New logic ships with NUnit: golden byte-equality + SSIM harness, per-effect manifest `[TestCaseSource]`, supply-driven `ResolveWorkingScale`/`ClampWorkingScaleToBufferBudget` pure-math tests, cache-scale-invalidation, perspective-append unit test. (`NoPixelCouplingOnRenderPathTest` deferred — T007/SC-008.) Benchmarks excluded from the default gate. |
| **IV. Avalonia + Compiled Bindings** | ✅ PASS (action) | The preview-scale control (FR-035) touches XAML/VM. Any new `UserControl` MUST declare `x:CompileBindings="True"` + `x:DataType`; prefer extending an existing player/quality control over a new control. |
| **V. Style Belongs to the Linter** | ✅ PASS | No stylistic-only edits; `dotnet format` owns style. |
| **VI. Source Generators Are Load-Bearing** | ✅ PASS | FR-032 resolves to **no generator change** for the common path (scale is not an `IProperty`; the resource model stays scale-free — D6). Only effect-property unit/type changes (e.g. `ColorShift` `PixelPoint`→`Point`) flow through the existing `CompareAndUpdate<T>`; `tests/SourceGeneratorTest` must still compile and `/beutl-build` must pass before review. |

**Quality Gates** (constitution §Quality Gates, all must pass): `dotnet format --verify-no-changes`; `dotnet build Beutl.slnx`; `dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings`; coverage threshold; CI Claude review; no orphaned TODOs. **Breaking-change governance**: ship as `refactor!:`/`feat!:` + `BREAKING CHANGE:` footer naming `Beutl.Engine`/`Beutl.NodeGraph`/`Beutl.ProjectSystem`; route the public surface through `beutl-design-reviewer` (FR-028).

**No constitutional violations — Complexity Tracking is empty.**

## Project Structure

### Documentation (this feature)

```text
docs/specs/003-resolution-independent-pipeline/
├── spec.md                  # /speckit-specify (+ /speckit-clarify)
├── plan.md                  # this file
├── research.md              # Phase 0 — the design decisions (D1–D7)
├── data-model.md            # Phase 1 — entities/types
├── contracts/               # Phase 1 — public-api.md, shader-uniforms.md, effect-scale-contract.md
├── quickstart.md            # Phase 1 — validation walkthrough + slice order
├── checklists/requirements.md
├── notes/rendering-analysis.md   # research dossier (+ §12 Codex corrections)
└── tasks.md                 # /speckit-tasks (NOT created here)
```

### Source Code (repository root) — affected areas

```text
src/Beutl.Engine/Graphics/Rendering/   # core: RenderNodeContext, RenderNodeOperation, RenderNodeProcessor,
│                                       #   Renderer, IRenderer, GraphicsContext2D, ImmediateCanvas, Cache/*
├── FilterEffects/                      # FilterEffectContext, CustomFilterEffectContext, FilterEffectActivator,
│                                       #   EffectTarget, SKSL/GLSLScriptEffect, ~40 effects (scale contract)
├── Particles/ParticleRenderNode.cs     # FR-029 (hard-coded 1920x1080 -> ceil(bounds*s))
├── AudioVisualizers/*                  # FR-030
├── Media/PixelSize.cs, PixelPoint.cs, PixelRect.cs   # FR-007 helper (already exists; adopt scaled overloads)
└── Graphics3D/Scene3DRenderNode.cs     # FR-033 (mixed-scale bitmap op)
src/Beutl.ProjectSystem/SceneRenderer.cs            # forward renderScale
src/Beutl.NodeGraph/                    # NodeGraphFilterEffectRenderNode, RenderNodeDrawable (recompile + pass scale)
src/Beutl/                              # EditViewModel (PreviewScale + rebuild), PlayerViewModel (hit-test logical),
│                                       #   OutputViewModel + FrameProviderImpl (export SSAA, SourceSize from DeviceSize)
src/Beutl.Engine.SourceGenerators/      # NO change for the common path (document scale-free resource model)

tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/   # new harness + ImageMetrics + thresholds + baselines
tests/Beutl.UnitTests/Engine/Graphics/Rendering/NoPixelCouplingOnRenderPathTest.cs   # SC-008 (no GPU) — DEFERRED (T007; needs an allowlist scan)
tests/Beutl.Benchmarks/                 # SC-003 ratio benchmark
tests/SourceGeneratorTest/              # compile-only regression for any effect-prop type change
```

**Structure Decision**: Single-repo desktop/engine feature; no new project. Changes concentrate in `Beutl.Engine` graphics rendering + filter effects, with thin forwarding in `Beutl.ProjectSystem`/`Beutl.NodeGraph` and editor wiring in `Beutl`. Tests live in the existing `tests/Beutl.UnitTests` (Vulkan-gated golden harness) + `tests/Beutl.Benchmarks` + `tests/SourceGeneratorTest`.

## Phasing (implementation slices — independently testable)

Each slice is golden-testable (render at `s`, compare to `s=1.0` within the gate) and delivers value without the whole pipeline. Detail in [quickstart.md](./quickstart.md#implementation-slice-order-independently-testable-see-planmd); `/speckit-tasks` decomposes these.

1. **Slice 0 — plumbing skeleton** (no behavior change). `RenderNodeContext.OutputScale` + `ResolveWorkingScale` + `EffectiveScale` (value type), `RenderNodeProcessor.OutputScale`, `Renderer.RenderScale`; 3 sinks → `PixelRect.FromRect(rect, w)` (w=1). Land the golden harness (`NoPixelCouplingOnRenderPathTest` deferred — T007/SC-008). **Gate: byte-identical at 1.0.**
2. **Slice 1 — reduced-scale preview for vector + Skia-filter** (root `ceil(FrameSize×s_out)` + `CreateScale(s_out)`, re-shaped text, scale-keyed `RenderNodeCache`). First user-visible slice.
3. **Slice 2 — render-target (Custom) effects** (`FilterEffectActivator`/`CustomFilterEffectContext` `ceil(bounds×w)`; `FilterEffectContext` primitives × `WorkingScale`; supply-driven `w` at the effect boundary; SKSL `iScale` = `w` (GLSL derives `w` from device-px `Width`/`Height`, no uniform)).
4. **Slice 3 — mixed-scale compositing** (per-`EffectTarget` scale; `ImmediateCanvas.DrawSurface`/`DrawRenderTarget` Mitchell-resample on mismatch; nested scenes, `DrawableBrush`, particles, audio visualizers, 3D-as-bitmap).
5. **Slice 4 — media decoupling (proxy foundation)** (`SourceImage`/`SourceVideo` logical size ≠ decoded px; `DrawBitmap` logical dest rect; `MediaOptions` kept extensible).
6. **Slice 5 — editor + export** (`PreviewScale` control + rebuild-by-replacement (FR-031); export supersampling + `SourceSize` from `DeviceSize`; finalize logical hit-test/handles).

**Recommended first ship: Slice 0 + Slice 1.**

## Complexity Tracking

*No constitutional violations — section intentionally empty.*

## Residual decisions for `/speckit-tasks` / implementation

- **CI GPU lane**: whether to add SwiftShader/llvmpipe so SC-001/SC-004 pixel goldens run in CI, or keep them dev/self-hosted (maintainer + CI-workflow approval; do not change `.github/workflows/*` without it). If no GPU lane, the CI-enforced guards are the **non-GPU pure-math** tests (`ResolveWorkingScale`/`ClampWorkingScaleToBufferBudget`/`EffectiveScale` density flow, `Editor/RenderScaleTests`) and goldens run on dev/self-hosted GPU. *(The SC-008 search test it originally named was deferred — T007; an allowlist scan is the follow-up.)*
- **RgbaF16 zero-epsilon reproducibility** across MoltenVK/SwiftShader/native — validate empirically on the chosen golden backend; fall back to a tiny ULP tolerance only if required.
- **`RenderScale` value-type shape** (record struct vs enum+float) and where Fit-to-previewer reads the preview surface size (`PlayerViewModel._maxFrameSize` vs Image bounds) — pin in tasks.
- **Supersample factor surfacing** in `OutputViewModel`/encoder preset UI (cap at 2× + Mitchell per research.md).
- **Built-in working scale** (FR-036) — **as shipped, every built-in is supply-driven** (no per-effect knob; runs at the input supply density, which already keeps a high-res source through the effect). The `ResolutionPolicy` type (`Inherit`/`ClampToOutput`/`Oversample`/`PreserveSource`) was removed entirely — an effect needing a non-supply `w` overrides `Process` in a custom `FilterEffectRenderNode`. The working scale MUST NOT change `s_out=1.0` output.
- **Global working-scale ceiling value** `MaxWorkingScale` (FR-037) — **as shipped:** preview `2 × s_out` (interactive backstop), **export `+∞`** (no working-scale quality ceiling — *amended 2026-06-15*; the earlier finite `max(8, 4 × s_out)` was removed as a quality clip). Allocatability on export is the per-buffer dimension clamp (`ClampWorkingScaleToBufferBudget`, 16384 px/axis) plus the request-scoped byte/area budget follow-up. Configured in `WorkingScaleCeiling` (`Beutl.Editor`), seeded by `EditViewModel` (preview) / `OutputViewModel` (export).
- **`IRenderer.RenderScale`/`DeviceSize`** as hard breaking members vs default-interface-impls — `beutl-design-reviewer` call.
