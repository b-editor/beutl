# Implementation Plan: Resolution-Independent Pixel-Absolute Effects

**Branch**: `speckit/003-resolution-independent-effects` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/003-resolution-independent-effects/spec.md`

## Summary

Make pixel-absolute parameters on built-in filter effects (blur sigma, drop-shadow offset / sigma, stroke offset / thickness, dilate / erode radius, etc.) interpret as "pixels at the project's export resolution" regardless of the actual raster size being rendered into. Today every effect uses values literally against the current render target, so a hypothetical proxy preview at 1/4 resolution would not match the export.

The implementation introduces (a) a new `RenderScale` carried by `FilterEffectContext` / `GraphicsContext2D` whose value is `currentRaster / referenceFrame`, (b) typed parameter wrappers `PixelLength` / `PixelExtent` / `PixelOffset` that mark an effect parameter as resolution-independent, and (c) updates to in-scope built-in effects to use the wrappers and apply the scale at draw time. Existing project files keep their numeric values verbatim — at the default `RenderScale = 1.0` (the current state of every renderer) they behave identically to before, so legacy projects render bit-similar output at export resolution.

A proxy-preview *workflow* (rendering at less than 100% during editing) is **not** built by this feature — it remains a separate, future feature. What this feature ships is the plumbing that makes that future feature visually correct.

## Technical Context

**Language/Version**: C# with `LangVersion: preview`, `Nullable: enable`. .NET 10 (`net10.0`) and `net10.0-windows` dual-target as required by the constitution.

**Primary Dependencies**: `Beutl.Engine` (filter-effect runtime, `GraphicsContext2D`, `FilterEffectContext`, `Renderer`), `Beutl.Engine.SourceGenerators` (animator registration), `Beutl.ProjectSystem` (`Scene.FrameSize`, `SceneRenderer`), `Beutl.Extensibility` (plugin contract surface). Skia (`SkiaSharp`) for actual blur / shadow filters — already a transitive dependency.

**Storage**: None added. Project files (`.scene`, etc.) keep their existing schema; pixel-absolute parameters that move to `PixelExtent` / `PixelOffset` / `PixelLength` serialize using the same numeric layout as the underlying `Size` / `Point` / `float` they replace (see `contracts/parameter-wrappers.md`).

**Testing**: NUnit + Moq under `tests/Beutl.UnitTests` (rendering-equivalence tests, animator tests, parameter-wrapper serialization tests), `tests/SourceGeneratorTest` (if any generator changes), `tests/Beutl.Graphics3DTests` (for 3D-output-with-2D-filter cases). New `RenderScaleTests` exercise the scale-plumbing in isolation.

**Target Platform**: Same as Beutl itself — Windows / macOS / Linux desktop, .NET 10. No new platform constraints.

**Project Type**: Library / desktop application feature within an existing solution. No new project added; changes land in existing `src/Beutl.Engine`, `src/Beutl.Extensibility`, `src/Beutl.ProjectSystem`, and `tests/Beutl.UnitTests`.

**Performance Goals**: Per-frame render-time overhead from the new scale wiring must be negligible — target is ≤ 0.5% added wall-clock on `tests/Beutl.Benchmarks` for representative effect-heavy scenes. The scaling itself is two `float` multiplies per parameter per frame.

**Constraints**:
- MIT / GPL boundary unchanged — all work is inside MIT projects. No `Beutl.FFmpegWorker` touched.
- Dual-target build must keep passing (`net10.0` + `net10.0-windows`).
- Existing project files MUST load and render the same at export resolution (no migration step on disk — see FR-003).
- Out-of-tree plugins that keep using plain `Size` / `Point` / `float` parameters MUST keep their current raw-pixel behavior (opt-in is type change, per FR-008).

**Scale/Scope**:
- ~10–15 built-in filter effects modified (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, ColorShift, FlatShadow, SplitEffect, DisplacementMapTransform, MosaicEffect, ShakeEffect, and any others discovered during the audit pass in `tasks.md`). Final list enumerated in `data-model.md`.
- 3 new typed wrapper structs (`PixelLength`, `PixelExtent`, `PixelOffset`) in `Beutl.Engine`.
- 1 `RenderScale` value carried through `GraphicsContext2D` and `FilterEffectContext`.
- 1 documentation entry for plugin authors under `docs/`.

> Wrapper naming convention: `PixelLength` (1-D), `PixelExtent` (2-D anisotropic extent — width × height), `PixelOffset` (2-D directional offset — X / Y). Distinct from `Beutl.Media.PixelSize` and `Beutl.Media.PixelPoint`, which remain the integer raster-coordinate types they are today. Rationale in `research.md` § R2.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. License Firewall | PASS | All changes in MIT projects (`Beutl.Engine`, `Beutl.ProjectSystem`, `Beutl.Extensibility`). No new dependency on `Beutl.FFmpegWorker`. |
| II. Dual-Target Framework | PASS | No new TFM. Existing `net10.0` / `net10.0-windows` both still build. |
| III. Test-First with NUnit | PASS | New types and rendering paths get tests in `tests/Beutl.UnitTests`; SSIM-based equivalence tests added with a curated fixture corpus. See `quickstart.md`. |
| IV. Avalonia + Compiled Bindings | PASS | No new UserControl. Property editor changes (if any) reuse existing typed editors via the `IPropertyEditorContext` machinery. |
| V. Style Belongs to the Linter | PASS | No stylistic-only edits. `dotnet format Beutl.slnx --verify-no-changes` must pass. |
| VI. Source Generators Are Load-Bearing | PASS (re-verify) | Adding new property types may interact with `Beutl.Engine.SourceGenerators` — confirmed in Phase 0 research (see `research.md` § "Source generator impact"). If generator changes, `/beutl-build` and `tests/SourceGeneratorTest/` re-run before review. |
| Quality Gates (PR list) | PASS | Format / build / test / coverage / TODO comments all covered by existing CI; no workflow changes proposed. |

Post-design re-check (after Phase 1): the typed-wrapper + scale-plumbing design adds no constitution gate violations. **No entries needed in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
docs/specs/003-resolution-independent-effects/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions + alternatives
├── data-model.md        # Phase 1 output — new types, scope-of-effects list
├── quickstart.md        # Phase 1 output — how to build / test / verify
├── contracts/
│   ├── parameter-wrappers.md   # PixelLength / PixelExtent / PixelOffset surface
│   └── render-scale.md         # RenderScale propagation contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality checklist (already exists)
└── tasks.md             # Phase 2 — written by /speckit-tasks
```

### Source Code (repository root)

```text
src/Beutl.Engine/
├── Graphics/
│   ├── PixelLength.cs           # NEW typed wrapper (single-axis length)
│   ├── PixelExtent.cs           # NEW typed wrapper (2D anisotropic extent — e.g. blur sigma)
│   ├── PixelOffset.cs           # NEW typed wrapper (2D directional offset — e.g. drop-shadow position)
│   │                            #   Distinct from existing Beutl.Media.PixelPoint (integer raster coord) — see research R2.
│   ├── Rendering/
│   │   ├── GraphicsContext2D.cs # MODIFIED — carries RenderScale (currentRaster/refFrame)
│   │   ├── Renderer.cs          # MODIFIED — constructor takes optional ReferenceFrame
│   │   ├── IRenderer.cs         # MODIFIED — exposes ReferenceFrame
│   │   └── RenderScale.cs       # NEW small struct (Sx, Sy)
│   └── FilterEffects/
│       ├── FilterEffectContext.cs   # MODIFIED — RenderScale flows in; new overloads
│       │                            #   accept `PixelLength` / `PixelExtent` / `PixelOffset`
│       ├── Blur.cs                  # MODIFIED — Sigma becomes IProperty<PixelExtent>
│       ├── DropShadow.cs            # MODIFIED — Position: PixelOffset, Sigma: PixelExtent
│       ├── InnerShadow.cs           # MODIFIED — same pattern
│       ├── StrokeEffect.cs          # MODIFIED — Offset: PixelOffset (Pen thickness via Pen update)
│       ├── Erode.cs / Dilate.cs     # MODIFIED — RadiusX/Y: PixelLength
│       ├── ColorShift.cs            # MODIFIED — per-channel offsets: PixelLength / PixelOffset
│       ├── FlatShadow.cs            # MODIFIED — Length: PixelLength
│       ├── DisplacementMapTransform.cs # MODIFIED — X/Y/CenterX/Y: PixelLength
│       ├── MosaicEffect.cs          # MODIFIED — tile size: PixelLength
│       ├── ShakeEffect.cs           # MODIFIED — amplitude: PixelLength (after audit)
│       ├── SplitEffect.cs / PartsSplitEffect.cs # MODIFIED — spacing: PixelLength
│       └── (others as audit reveals — see data-model.md)
└── Animation/
    └── AnimatorRegistry.cs       # MODIFIED — register linear animators for `PixelLength` / `PixelExtent` / `PixelOffset`

src/Beutl.ProjectSystem/
├── SceneRenderer.cs              # MODIFIED — passes Scene.FrameSize as ReferenceFrame
└── (no other files)

src/Beutl.Extensibility/
└── (no new public surface; the `PixelLength` / `PixelExtent` / `PixelOffset` live in Beutl.Engine and are
   referenced by plugins via the existing Beutl.Engine PackageReference)

tests/Beutl.UnitTests/
├── Graphics/
│   ├── PixelLengthTests.cs       # NEW — wrapper semantics, serialization, animation
│   ├── PixelSizeTests.cs         # NEW
│   ├── PixelPointTests.cs        # NEW
│   └── Rendering/
│       └── RenderScaleTests.cs   # NEW — scale propagation, nested-scene rules
└── Graphics/FilterEffects/
    ├── ResolutionEquivalenceTests.cs  # NEW — SSIM-based proxy↔export comparison
    ├── LegacyRenderingTests.cs        # NEW — pre-feature project corpus check
    └── (per-effect tests as needed)

docs/
└── ai-workflow/coding-guidelines-for-ai.md  # MODIFIED — mention `PixelLength` / `PixelExtent` / `PixelOffset` for new effects
```

**Structure Decision**: Existing modular layout is reused — no new project. The wrappers and the `RenderScale` value live in `Beutl.Engine` so all consumers (built-in effects, plugins, scripting) get them transitively. Built-in effects are migrated in-place. Test coverage is added in `tests/Beutl.UnitTests` (the existing primary unit-test project for graphics-layer code) with a curated fixture corpus committed alongside the tests.

## Complexity Tracking

> No constitution gate violations. Table intentionally left blank.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|

## Phase 0 — Research (output: `research.md`)

Open questions resolved by Phase 0 (full content in `research.md`):

1. **Does Beutl already have a "proxy preview" rendering mode?** → No. The plumbing is what this feature adds; the workflow is a future concern.
2. **How should the new wrapper names avoid clashing with `Beutl.Media.PixelSize` / `PixelPoint`?** → Use the `Pixel<Concept>` family `PixelLength` / `PixelExtent` / `PixelOffset` so each wrapper has a distinct geometric noun and none collide with the existing integer types. Concrete decision in `research.md` § R2.
3. **How does the renderer's reference frame flow to nested compositions?** → A scoped stack on `GraphicsContext2D` that `SceneDrawable.Render` pushes / pops; the `FilterEffectContext` snapshots the top on creation.
4. **Source-generator impact** — confirm whether `AbstractCoreObjectGenerator` / property generators react to new property types.
5. **Animator registration for `PixelLength` / `PixelExtent` / `PixelOffset`** — pick between (a) implementing `IEquatable<T>` + linear `_Animator<T>` fallback, (b) explicit `RegisterAnimator<PixelExtent, PixelExtentAnimator>` etc.
6. **How is `Pen.Thickness` (used by `StrokeEffect`) reached?** — decide whether `Pen` itself gains a `PixelLength`-flavored thickness or whether `StrokeEffect` adapts at the call site.

## Phase 1 — Design & Contracts

**Prerequisites**: `research.md` complete.

1. **Entities** in `data-model.md`:
   - `RenderScale` (struct, `ScaleX` / `ScaleY` floats).
   - `PixelLength`, `PixelSize`, `PixelPoint` (wrapper structs around `float` / `Size` / `Point`).
   - `ReferenceFrame` semantic on `IRenderer` (`PixelSize ReferenceFrame { get; }`).
   - Updated property-type → animator mapping.
   - Definitive in-scope effect list (audit walk of `src/Beutl.Engine/Graphics/FilterEffects/`).

2. **Public contracts** in `contracts/`:
   - `parameter-wrappers.md` — the surface of the new types and migration recipe for built-in effects.
   - `render-scale.md` — how `RenderScale` flows from `Renderer` → `GraphicsContext2D` → `FilterEffectContext` and how nested scenes override it.

3. **Quickstart** in `quickstart.md` — concrete commands for build / test / SSIM-fixture regeneration, plus a "smoke project" the developer can render at two resolutions to eyeball-verify.

4. **Agent context update**: append a reference to this plan inside the `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->` markers in `CLAUDE.md`. (If those markers don't yet exist in `CLAUDE.md`, add them just before the "Claude Code-specific notes" section so future `/speckit-*` runs can update in place.)

## Phase 2 — Tasks (output: handled by `/speckit-tasks`)

Out of scope for this command. Expected shape (preview only):

- T1 (Engine): introduce `RenderScale` and the three wrapper structs (`PixelLength`, `PixelExtent`, `PixelOffset`) with their animators, plus tests.
- T2 (Engine): plumb `ReferenceFrame` and `RenderScale` through `IRenderer` / `Renderer` / `SceneRenderer` / `GraphicsContext2D` / `FilterEffectContext`. Add nested-scene push/pop.
- T3 (Engine): migrate the audited list of built-in effects one by one with per-effect tests.
- T4 (Engine): add SSIM-based proxy-equivalence tests and the legacy-corpus regression tests.
- T5 (Docs): plugin-author migration guide; update `docs/ai-workflow/coding-guidelines-for-ai.md`.
- T6 (Verify): run `/beutl-build`, `/beutl-test`, `/beutl-format`, `/beutl-coverage` and address `beutl-design-reviewer` feedback.
