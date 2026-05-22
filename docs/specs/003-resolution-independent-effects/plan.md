# Implementation Plan: Per-Clip Proxy via RenderNodeOperation CorrectionScale

**Branch**: `speckit/003-resolution-independent-effects` | **Date**: 2026-05-20 (created), 2026-05-22 (full rewrite) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/003-resolution-independent-effects/spec.md`

## Summary

Enable **per-clip proxy** in the Beutl render pipeline. Each `RenderNodeOperation` gains a `CorrectionScale: RenderScale` field that describes the scale ratio between the operation's `Bounds` (in authoring space) and its actual produced raster (at proxy resolution). Source-producing RenderNode subclasses (`VideoSourceRenderNode`, `ImageSourceRenderNode`, `DrawableRenderNode` for nested scenes) declare their own `CorrectionScale` based on per-clip proxy configuration. Transformer RenderNode subclasses (`FilterEffectRenderNode`, `TransformRenderNode`, container / push-state nodes) read the upstream `CorrectionScale` and divide their own length-typed internal parameters by it before invoking Skia. The compositor (top-level `Renderer` / `ImmediateCanvas`) consumes `CorrectionScale` by upscaling the raster with a `SKCanvas.Scale` transform at blit time. With `CorrectionScale = Identity` (the default everywhere, until proxy is wired in), the new code path is a no-op; pre-feature projects render byte-equivalently.

The user-facing proxy toggle UX, per-clip proxy persistence schema, and automatic proxy media generation are **out of scope** for this PR. They live in a follow-up feature. This PR ships only the engine-side mechanism.

> **This is the 6th design iteration.** Earlier drafts (commits `63dd67191` through `d4728ede9`) explored typed wrappers (`PixelLength` / `PixelExtent` / `PixelOffset`), helper-internal scaling on `FilterEffectContext` / `GraphicsContext2D` / `Pen` / `Transform`, scene-wide `RenderScale` propagation through push/pop stacks, and render-node application time for the Transform path. All those drafts assumed scene-wide proxy and were abandoned after the user clarified during the 4th design review that the actual mental model is **per-clip proxy with bottom-up scale propagation through `RenderNodeOperation`**. See `research.md` for the chronological pivot history.

## Technical Context

**Language/Version**: C# `LangVersion: preview`, `Nullable: enable`. .NET 10 dual-target (`net10.0` and `net10.0-windows`).

**Primary Dependencies**: `Beutl.Engine` (`RenderNodeOperation`, `RenderNode`, `ImmediateCanvas`, `Renderer`), `Beutl.ProjectSystem` (`SceneRenderer`, `SceneDrawable`), `Beutl.Extensibility` (plugin contract surface). Skia (`SkiaSharp`).

**Storage**: None added by this PR. Project files (`.scene`, etc.) keep their existing schema; the per-clip proxy setting schema is a follow-up feature.

**Testing**: NUnit + Moq under `tests/Beutl.UnitTests`. SSIM-based equivalence tests (proxy frame vs. export frame), per-RenderNode-subclass `CorrectionScale` tests, backward-compatibility corpus tests.

**Target Platform**: Same as Beutl — desktop (Windows / macOS / Linux), .NET 10.

**Project Type**: Engine-level feature in an existing solution. No new project.

**Performance Goals**: Per-frame overhead from the new `CorrectionScale` plumbing must be negligible. Target ≤ 0.5% wall-clock impact on `tests/Beutl.Benchmarks` for representative scenes with `CorrectionScale = Identity` (the common case).

**Constraints**:
- MIT / GPL boundary unchanged — all work in MIT projects.
- Dual-target must keep building.
- Existing project files MUST load and render visually equivalent (SSIM ≥ 0.97) to pre-feature.
- Out-of-tree plugins (Drawable / FilterEffect / Shape / custom Pen consumers / custom Transform subclasses) MUST automatically benefit without source change — the scale handling lives in the RenderNode layer one level below the authoring surface.

**Scale/Scope**:
- 1 new value type: `RenderScale` (struct, ScaleX / ScaleY floats with validation).
- 1 virtual property added to existing `RenderNodeOperation` (`CorrectionScale`, default `Identity`).
- 4 factory overloads on `RenderNodeOperation` updated to accept the value.
- ~3 source-producing RenderNode classes audit-modified to declare their `CorrectionScale` (Video, Image, Drawable-as-source).
- ~5 transformer RenderNode classes audit-modified to consume upstream `CorrectionScale` (FilterEffect, Transform, Container, push-state nodes).
- 1 compositor blit path extended in `Renderer` / `ImmediateCanvas` to apply the upscale transform.
- **Zero modifications** to: `GraphicsContext2D` (other than verifying it still works), `Pen`, `PenHelper`, `Transform` subclasses, `CompositionContext`, every `Shape` subclass, every animator, every property editor, project-file schema.
- **`FilterEffectContext` gains `CorrectionScale` + length-typed primitives divide internally** (deviation from earlier drafts — see `data-model.md` § "Implementation deviation" and `contracts/transformer-node-scale-handling.md`). The 5 pure-primitive in-scope `FilterEffect` subclasses (Blur, DropShadow, InnerShadow, Erode, Dilate) stay unmodified; the 8 CustomEffect-based in-scope subclasses (StrokeEffect, ColorShift, DisplacementMapTransform × 3, FlatShadow, Clipping, SplitEffect, ShakeEffect, MosaicEffect) need to read `CustomFilterEffectContext.CorrectionScale` and divide their own length-typed parameters — a single-line opt-in.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. License Firewall | PASS | All changes in MIT projects (`Beutl.Engine`, `Beutl.ProjectSystem`). No `Beutl.FFmpegWorker` dependency added. |
| II. Dual-Target Framework | PASS | No new TFM. Existing `net10.0` / `net10.0-windows` both build. |
| III. Test-First with NUnit | PASS | Every implementation task is paired with a test task to be written first. SSIM corpus + per-RenderNode-subclass tests. |
| IV. Avalonia + Compiled Bindings | PASS (N/A) | No new UserControl; no XAML. |
| V. Style Belongs to the Linter | PASS | `dotnet format Beutl.slnx --verify-no-changes` must pass. |
| VI. Source Generators Are Load-Bearing | PASS | No source-generator changes expected; `RenderNodeOperation` is hand-written. Verify in implementation. |
| Quality Gates (PR list) | PASS | Format / build / test / coverage / TODO comments all covered by existing CI. |

No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
docs/specs/003-resolution-independent-effects/
├── spec.md              # User-facing requirements (rewritten 2026-05-22)
├── plan.md              # This file (rewritten 2026-05-22)
├── research.md          # Phase 0 design decisions (rewritten 2026-05-22)
├── data-model.md        # Type-level changes (rewritten 2026-05-22)
├── quickstart.md        # Build / test / verify workflow (rewritten 2026-05-22)
├── contracts/
│   ├── render-node-operation-scale.md       # The core CorrectionScale contract
│   ├── source-node-proxy.md                 # Source-producing nodes
│   ├── transformer-node-scale-handling.md   # Filter / Transform / etc.
│   └── compositor-blit.md                   # Final blit with upscale
├── checklists/
│   └── requirements.md                      # Validation checklist
└── tasks.md             # Phase 2 work breakdown (rewritten 2026-05-22)
```

The four prior contract documents (`effect-helper-scaling.md`, `graphics-context-scaling.md`, `pen-scaling.md`, `transform-scaling.md`, `render-scale.md`) were deleted in the rewrite — their content is no longer relevant.

### Source Code (repository root)

```text
src/Beutl.Engine/
└── Graphics/
    └── Rendering/
        ├── RenderScale.cs                    # NEW — value type (ScaleX, ScaleY), validation, FromFrames factory
        ├── RenderNodeOperation.cs            # MODIFIED — adds virtual CorrectionScale (default Identity);
        │                                     #   adds CorrectionScale parameter to CreateLambda / CreateFromRenderTarget /
        │                                     #   CreateFromSurface factories; LambdaRenderNodeOperation stores and overrides.
        ├── ImmediateCanvas.cs                # MODIFIED — exposes a blit path that consumes CorrectionScale (push transform + draw).
        ├── Renderer.cs                       # MODIFIED — compositor walks operations and applies the upscale blit.
        ├── VideoSourceRenderNode.cs          # MODIFIED — source-producing; declares CorrectionScale based on proxy config (default Identity).
        ├── ImageSourceRenderNode.cs          # MODIFIED — same as Video.
        ├── DrawableRenderNode.cs             # MODIFIED — when rendering a sub-canvas, sets up SKCanvas.Scale(1/CorrectionScale)
        │                                     #   so inner pass renders in authoring space; declares CorrectionScale on output.
        ├── FilterEffectRenderNode.cs         # MODIFIED — reads upstream CorrectionScale, divides filter parameters,
        │                                     #   computes output Bounds with authored parameters, propagates CorrectionScale.
        ├── TransformRenderNode.cs            # MODIFIED — adjusts Bounds in authoring space, propagates CorrectionScale unchanged.
        ├── ContainerRenderNode.cs            # MODIFIED — aggregates child operations; each child's CorrectionScale flows through independently.
        └── (push-state nodes)                # MODIFIED — clip / layer / opacity-mask propagate CorrectionScale; bounds in authoring space.

src/Beutl.ProjectSystem/
└── (no modifications expected) — SceneDrawable's existing path produces a DrawableRenderNode that handles its own proxy via Beutl.Engine.

tests/Beutl.UnitTests/
└── Engine/Graphics/Rendering/
    ├── RenderScaleTests.cs                          # NEW
    ├── RenderNodeOperationCorrectionScaleTests.cs   # NEW — virtual default = Identity; factory overloads honour the parameter
    ├── SourceNodeCorrectionScaleTests.cs            # NEW — Video / Image / Drawable-as-source declare scale correctly
    ├── TransformerNodeCorrectionScaleTests.cs       # NEW — Filter / Transform / Container / push-state propagate / divide
    ├── CompositorBlitTests.cs                       # NEW — upscale blit produces correct sizes
    └── ResolutionEquivalenceTests.cs                # NEW — SSIM ≥ 0.97 proxy vs export, per in-scope effect

tests/Beutl.UnitTests/
└── Engine/Graphics/FilterEffects/
    └── LegacyRenderingTests.cs                      # NEW — pre-feature project corpus, SSIM vs baseline

docs/
└── extensibility/
    └── render-node-correction-scale.md              # NEW — RenderNode author guide (audience: rare advanced extension)
```

**Structure decision**: No new project. All changes in `src/Beutl.Engine/Graphics/Rendering/`. The plugin authoring surface (`FilterEffectContext`, `GraphicsContext2D`, `Pen`, `Transform` subclasses, `Drawable` / `Shape`) is **not modified**.

## Complexity Tracking

> No constitution gate violations.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | | |

## Phase 0 — Research (output: `research.md`)

The Phase 0 questions and answers live in `research.md`. Summary of decisions:

- R1: `CorrectionScale` lives on `RenderNodeOperation`. Bottom-up propagation.
- R2: Per-RenderNode classification (source vs transformer). Audit task confirms.
- R3: Filter parameter math — divide length-typed parameters by upstream `CorrectionScale`.
- R4: Effects whose bounds extend with parameters — bounds in authoring space using authored parameters; raster uses divided parameters.
- R5: Type B sources (sub-canvas-rendering) use `SKCanvas.Scale(1/CorrectionScale)` at the inner pass; everything inside is automatic.
- R6: Compositor applies the upscale at blit time via `SKCanvas.Scale` transform.
- R7: Backward compat — Identity everywhere by default; SSIM-equivalent to pre-feature.
- R8: Per-clip proxy settings schema and UX — out of scope (follow-up feature).
- R9: Test strategy — SSIM equivalence with `BicubicResampler` + `SsimHelper` (carried over from prior drafts).

## Phase 1 — Design & Contracts

**Output**: `data-model.md`, `contracts/`, `quickstart.md`.

Already produced in the rewrite. Four contract documents in `contracts/`:

- `render-node-operation-scale.md` — the core data contract.
- `source-node-proxy.md` — how source-producing nodes participate.
- `transformer-node-scale-handling.md` — how transformer nodes consume and propagate.
- `compositor-blit.md` — how the final blit upscales.

The `CLAUDE.md` SPECKIT marker continues to point at this `plan.md`.

## Phase 2 — Tasks (output: `tasks.md`)

`tasks.md` enumerates the implementation tasks. Expected shape (preview):

- **Block A**: Foundational types and plumbing (`RenderScale` struct, `RenderNodeOperation.CorrectionScale` virtual + factory overloads).
- **Block B**: Audit of `src/Beutl.Engine/Graphics/Rendering/` to classify each `RenderNode` subclass as source / transformer / both / N/A; record decisions.
- **Block C**: Per-source-node modifications (`VideoSourceRenderNode`, `ImageSourceRenderNode`, `DrawableRenderNode`).
- **Block D**: Per-transformer-node modifications (`FilterEffectRenderNode`, `TransformRenderNode`, `ContainerRenderNode`, push-state nodes).
- **Block E**: Compositor / `ImmediateCanvas` blit changes.
- **Block F**: Tests — RenderScale, CorrectionScale propagation, per-transformer math, SSIM equivalence, legacy corpus.
- **Block G**: Polish — RenderNode-author migration guide, perf benchmark, format / build / test / coverage / pre-PR / design review.
