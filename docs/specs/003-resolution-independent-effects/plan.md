# Implementation Plan: Resolution-Independent Pixel-Absolute Rendering

**Branch**: `speckit/003-resolution-independent-effects` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `docs/specs/003-resolution-independent-effects/spec.md`

## Summary

Make pixel-absolute parameters on the **enumerated raster-space rendering surfaces** — filter effects, transforms, pens, shapes, and the direct `GraphicsContext2D` length-typed API (`DrawRectangle`, `DrawEllipse`, `PushTransform`, `PushClip`, `PushLayer`, `PushOpacityMask`) — interpret as "pixels at the project's export resolution" regardless of the actual raster size being rendered into. **Not every rendering API in Beutl is covered by this PR**: `Geometry` path coordinates, `TextBlock.Size / Spacing`, and `Brush` interior rectangles are explicitly deferred to follow-up features (see `data-model.md` § "Deferred follow-ups"). Within the enumerated surfaces, today every primitive uses values literally against the current render target, so a hypothetical proxy preview at 1/4 resolution would not match the export; this feature fixes that.

The implementation introduces (a) a new `RenderScale` carried by `FilterEffectContext` / `GraphicsContext2D` / `ImmediateCanvas` whose value is `currentRaster / referenceFrame`, and (b) **internal scaling inside the rendering pipeline at three distinct sites**:

- **API call time** for most helpers — `FilterEffectContext` length helpers (`Blur`, `DropShadow`, …) and `GraphicsContext2D` Rect-typed helpers (`DrawRectangle`, `DrawEllipse`, `PushClip`, `PushLayer`, `PushOpacityMask`) multiply their length argument by `this.RenderScale` before forwarding.
- **Render-node application time** for the Transform path — `GraphicsContext2D.PushTransform(Matrix | Transform.Resource)` records the matrix verbatim into `TransformRenderNode`; `ImmediateCanvas.PushTransform(matrix, op, isRaw)` multiplies the translation column by `this.RenderScale` at the bottom of the rendering pipeline. This keeps `Transform.Resource.Matrix` authoring-space and lets `RenderNodeCache` survive `RenderScale` changes; see `research.md` § R10.
- **Consumption-site, opt-in via helper** for Pen — `PenHelper.GetScaledThickness / GetScaledDashOffset / GetScaledOffset` (plus the render-space `GetScaledBounds`) at rendering call sites. `Pen.Resource` properties stay raw so project-space callers (bounding-box math, animation) read authored values; see `research.md` § R9.

A `*Raw` twin (`BlurRaw(Size)`, `DrawRectangleRaw(Rect)`, `PushTransformRaw(Matrix)`, …) is added wherever it makes sense for plugins that need raw-raster pixel semantics. For Transform, the opt-out is implemented as an `IsRaw` flag on `TransformRenderNode`. For Pen, the opt-out is simply not calling the scaled helper.

**No new property types, no `IProperty<...>` declaration churn, no animator / property-editor work, no project-file migration**: legacy projects render visually equivalent to before at export resolution because `RenderScale = Identity` makes every scaling multiplication a no-op. (Strict byte-equality is *not* required — `data-model.md` § "Validation summary" introduces NaN / negative-length guards that fire only on previously-undefined inputs; FR-003 / SC-002 require SSIM ≥ 0.97 against the pre-feature baseline, not bit-equal.)

A proxy-preview *workflow* (rendering at less than 100% during editing) is **not** built by this feature — it remains a separate, future feature. What this feature ships is the rendering-layer plumbing that makes that future feature visually correct.

> **Design pivot history** — `research.md` § R2 records the first pivot (typed wrappers → helper-internal scaling on `FilterEffectContext`). § R8 records the scope expansion (FilterEffects-only → the broader raster-space rendering surface). § R9 covers the Pen consumption-site decision. § R10 (revised after the Codex design review) covers the Transform render-node-application decision — the previous draft used materialize-time scaling in `CreateMatrix` with `CompositionContext.RenderScale`; that has been abandoned. `Geometry / Text / Brush` remain deferred follow-ups.

## Technical Context

**Language/Version**: C# with `LangVersion: preview`, `Nullable: enable`. .NET 10 (`net10.0`) and `net10.0-windows` dual-target as required by the constitution.

**Primary Dependencies**: `Beutl.Engine` (filter-effect runtime, `GraphicsContext2D`, `FilterEffectContext`, `Renderer`), `Beutl.Engine.SourceGenerators` (animator registration), `Beutl.ProjectSystem` (`Scene.FrameSize`, `SceneRenderer`), `Beutl.Extensibility` (plugin contract surface). Skia (`SkiaSharp`) for actual blur / shadow filters — already a transitive dependency.

**Storage**: None added. Project files (`.scene`, etc.) keep their existing schema. **No property type is renamed and no migration step runs.** `Size` / `Point` / `float` continue to serialize as they always have.

**Testing**: NUnit + Moq under `tests/Beutl.UnitTests` (rendering-equivalence tests, animator tests, parameter-wrapper serialization tests), `tests/SourceGeneratorTest` (if any generator changes), `tests/Beutl.Graphics3DTests` (for 3D-output-with-2D-filter cases). New `RenderScaleTests` exercise the scale-plumbing in isolation.

**Target Platform**: Same as Beutl itself — Windows / macOS / Linux desktop, .NET 10. No new platform constraints.

**Project Type**: Library / desktop application feature within an existing solution. No new project added; changes land in existing `src/Beutl.Engine`, `src/Beutl.Extensibility`, `src/Beutl.ProjectSystem`, and `tests/Beutl.UnitTests`.

**Performance Goals**: Per-frame render-time overhead from the new scale wiring must be negligible — target is ≤ 0.5% added wall-clock on `tests/Beutl.Benchmarks` for representative effect-heavy scenes. The scaling itself is two `float` multiplies per parameter per frame.

**Constraints**:
- MIT / GPL boundary unchanged — all work is inside MIT projects. No `Beutl.FFmpegWorker` touched.
- Dual-target build must keep passing (`net10.0` + `net10.0-windows`).
- Existing project files MUST load and render the same at export resolution (no migration step on disk — see FR-003).
- Out-of-tree plugins automatically benefit on rebuild — see `contracts/effect-helper-scaling.md` § "Plugin author migration (authoritative cross-surface table)" for the per-surface contract (automatic vs manual-opt-in vs `*Raw` opt-out). The one surface that requires manual opt-in is direct `pen.Thickness` reads in rendering paths (switch to `PenHelper.GetScaledThickness`).

**Scale/Scope**:
- **Zero built-in filter-effect / shape files modified.** All 13 in-scope effects + the Shape subclasses (`RectShape`, `EllipseShape`, `RoundedRectShape`) automatically benefit because their helper calls scale internally.
- 1 new `RenderScale` value type in `Beutl.Engine`.
- 1 modified `FilterEffectContext.cs` — every length-taking helper applies `RenderScale` internally; each gets a `*Raw` twin.
- 1 modified `GraphicsContext2D.cs` — every length-taking helper applies `RenderScale` internally; each gets a `*Raw` twin.
- **No `Transform` subclass modifications.** `TranslateTransform`, `Rotation3DTransform`, `MatrixTransform`, etc. continue to return authoring-space matrices. `TransformRenderNode` gains an `IsRaw` flag and `ImmediateCanvas.PushTransform` does the translation-column scaling at render-node application time. `CompositionContext` is unchanged.
- 1 modified `PenHelper.cs` (new scaled helpers) + ~3 consumer files updated (`ImmediateCanvas.cs`, `Shape.cs`, `StrokeEffect.cs`) to call the scaled helpers.
- `IRenderer.ReferenceFrame` + `GraphicsContext2D.PushReferenceFrame` plumbing additions.
- 1 documentation entry for plugin authors under `docs/extensibility/`.

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
│   ├── effect-helper-scaling.md      # FilterEffectContext scaled helpers + *Raw twins
│   ├── graphics-context-scaling.md   # GraphicsContext2D scaled helpers + *Raw twins (Rect, Matrix)
│   ├── pen-scaling.md                # Pen thickness / DashOffset / Offset via PenHelper.GetScaled*
│   ├── transform-scaling.md          # TransformRenderNode.IsRaw + ImmediateCanvas.PushTransform scales translation column at render-node application
│   └── render-scale.md         # RenderScale propagation contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality checklist (already exists)
└── tasks.md             # Phase 2 — written by /speckit-tasks
```

### Source Code (repository root)

```text
src/Beutl.Engine/
├── Graphics/
│   ├── Rendering/
│   │   ├── RenderScale.cs       # NEW small struct (ScaleX, ScaleY); FromFrames enforces uniform scale
│   │   ├── GraphicsContext2D.cs # MODIFIED — exposes ReferenceFrame / RenderScale; adds PushReferenceFrame;
│   │   │                        #   every length-taking helper (DrawRectangle, DrawEllipse, PushTransform,
│   │   │                        #   PushClip, PushLayer, PushOpacityMask) applies RenderScale internally;
│   │   │                        #   each gets a *Raw twin for opt-out.
│   │   ├── Renderer.cs          # MODIFIED — adds (int w, int h, PixelSize referenceFrame) ctor overload
│   │   ├── IRenderer.cs         # MODIFIED — adds ReferenceFrame (default-interface impl returns FrameSize)
│   │   └── PenHelper.cs         # MODIFIED — adds GetScaledThickness / GetScaledDashOffset / GetScaledOffset /
│   │                            #   GetScaledRealThickness; existing helpers unchanged.
│   ├── ImmediateCanvas.cs       # MODIFIED — pen.Thickness reads in rendering paths switch to PenHelper.GetScaledThickness;
│   │                            #   PushTransform(Matrix, op, isRaw) multiplies translation column by this.RenderScale unless isRaw.
│   │                            #   Exposes RenderScale mirrored from the constructing Renderer.
│   ├── FilterEffects/
│   │   ├── FilterEffectContext.cs   # MODIFIED — snapshots (ReferenceFrame, RenderScale);
│   │   │                            #   every length-taking helper (Blur, DropShadow, InnerShadow, Erode, Dilate, …)
│   │   │                            #   applies RenderScale internally; adds *Raw twin per helper.
│   │   └── StrokeEffect.cs          # MODIFIED — thickness read switches to PenHelper.GetScaledThickness
│   ├── Shapes/
│   │   └── Shape.cs             # MODIFIED — GetRealThickness switches to PenHelper.GetScaledRealThickness;
│   │                            #   RectShape / EllipseShape / RoundedRectShape unchanged (they call DrawRectangle / DrawEllipse).
│   ├── Transformation/
│   │   └── (no source changes — Transform.CreateMatrix returns project-space matrix verbatim; scaling moved to ImmediateCanvas)
│   └── Rendering/
│       └── TransformRenderNode.cs  # MODIFIED — adds `bool IsRaw` ctor parameter / property;
│                                   #   Process passes IsRaw through to the lambda that calls ImmediateCanvas.PushTransform.

src/Beutl.ProjectSystem/
└── ProjectSystem/
    └── SceneDrawable.cs          # MODIFIED — wraps inner draw in ctx.PushReferenceFrame(ReferencedScene.FrameSize);
                                  # when allocating a sub-canvas for a nested scene, constructs it with the inner ReferenceFrame.

src/Beutl.Extensibility/
└── (no changes)

tests/Beutl.UnitTests/
└── Engine/Graphics/
    ├── Rendering/
    │   ├── RenderScaleTests.cs               # NEW — Identity, FromFrames uniform-scale enforcement, NaN/zero
    │   └── ReferenceFramePropagationTests.cs # NEW — PushReferenceFrame discipline, FilterEffectContext snapshot, nested scene
    └── FilterEffects/
        ├── FilterEffectContextScalingTests.cs # NEW — every scaled helper multiplies by RenderScale; *Raw bypasses; sub-pixel/zero/NaN guards
        ├── ResolutionEquivalenceTests.cs      # NEW — per-effect SSIM proxy↔export comparison (one [TestCase] per in-scope effect)
        └── LegacyRenderingTests.cs            # NEW — pre-feature project corpus check

tests/Beutl.Graphics3DTests/
└── FilterEffects/
    └── Render3DWithFilterResolutionTests.cs   # NEW — 3D framebuffer + 2D filter scales correctly

docs/
├── extensibility/
│   └── resolution-independent-effects.md      # NEW — plugin-author guide ("do nothing; *Raw if you opt out")
└── ai-workflow/coding-guidelines-for-ai.md    # MODIFIED — note the helper contract for new effects
```

**Structure Decision**: Existing modular layout is reused — no new project, **no new file under `src/Beutl.Engine/Graphics/FilterEffects/` beyond the modified `FilterEffectContext.cs`**. The `RenderScale` value type lives in `Beutl.Engine.Graphics.Rendering` so all consumers (built-in effects, plugins, scripting) get it transitively via the existing `Beutl.Engine` reference. Test coverage is added under `tests/Beutl.UnitTests/Engine/Graphics/` plus `tests/Beutl.Graphics3DTests/FilterEffects/` with a curated fixture corpus committed alongside the tests.

## Complexity Tracking

> No constitution gate violations. Table intentionally left blank.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|

## Phase 0 — Research (output: `research.md`)

Open questions resolved by Phase 0 (full content in `research.md`):

1. **Does Beutl already have a "proxy preview" rendering mode?** → No. The plumbing is what this feature adds; the workflow is a future concern.
2. **Should the design introduce typed parameter wrappers or apply scaling inside the helpers?** → Apply scaling inside the existing `FilterEffectContext` helpers; add `*Raw` twins as an opt-out. No new wrapper types are introduced. Rationale: zero churn at the EngineObject layer, universal coverage for plugins for free, single-file scaling logic. See `research.md` § R2.
3. **How does the renderer's reference frame flow to nested compositions?** → A scoped stack on `GraphicsContext2D` that `SceneDrawable.Render` pushes / pops; the `FilterEffectContext` snapshots the top on creation.
4. **Source-generator impact** — no new property types means no generator changes expected. Spot-checked: confirmed.
5. ~~**Animator registration**~~ — moot under the helper-internal design. No new animators needed.
6. **How is `Pen.Thickness` (used by `StrokeEffect`) reached?** — Via `PenHelper.GetScaledThickness(pen, renderScale)` at the consumption site inside `StrokeEffect` (and the other rendering Pen consumers). Pen scaling is **in scope** for this PR after the 2026-05-21 scope expansion. See `research.md` § R6 (updated) / § R8 / § R9 and `contracts/pen-scaling.md`.
7. **Scope expansion (2026-05-21)**: Should non-FilterEffect surfaces (`GraphicsContext2D` direct draw, `Pen.Thickness`, `Transform` translations, `Shape.Width/Height`) also become resolution-independent? → Yes. Helper-internal-scaling pattern applies to most; the Transform path uses render-node-application scaling instead (see R10). See `research.md` § R8.
8. **Pen scaling strategy**: At Pen materialization vs at consumption vs via helper? → Via `PenHelper.GetScaled*` helpers; opt-in at each rendering call site; bounds-computation paths intentionally keep using raw `pen.Thickness`. See `research.md` § R9.
9. **Transform scaling strategy**: Scale at `CreateMatrix` vs at API consume site vs at render-node application? → **Scale at render-node application time** inside `ImmediateCanvas.PushTransform`. `Transform.CreateMatrix` returns project-space; `TransformRenderNode` carries an `IsRaw` flag; `CompositionContext` does NOT gain a `RenderScale` property (rolled back from earlier draft after design review). See `research.md` § R10 (revised).
10. **What stays raw / deferred**: `Geometry` path coordinates, `TextBlock.Size / Spacing`, `Brush` rectangles. Tracked as separate follow-up features in `data-model.md` § "Deferred follow-ups".

## Phase 1 — Design & Contracts

**Prerequisites**: `research.md` complete.

1. **Entities** in `data-model.md`:
   - `RenderScale` (struct, `ScaleX` / `ScaleY` floats; `FromFrames` enforces uniform scale).
   - `ReferenceFrame` semantic on `IRenderer` (`PixelSize ReferenceFrame { get; }`).
   - Modified `FilterEffectContext` surface — every length-taking helper applies `RenderScale` internally; `*Raw` twins added.
   - Definitive in-scope effect list (T001 audit walk of `src/Beutl.Engine/Graphics/FilterEffects/`) — descriptive only; effect files are not modified.

2. **Public contracts** in `contracts/`:
   - `effect-helper-scaling.md` — the new semantics of `FilterEffectContext` helpers, the `*Raw` opt-out, and the (mostly null) plugin-author migration recipe.
   - `graphics-context-scaling.md` — `GraphicsContext2D` direct-draw / `Push*` helper scaling rules + `*Raw` twins.
   - `pen-scaling.md` — `Pen.Thickness` / `DashOffset` / `Offset` scaling via `PenHelper.GetScaled*` at consumption sites.
   - `transform-scaling.md` — `Transform.CreateMatrix` stays authoring-space; `TransformRenderNode.IsRaw` + `ImmediateCanvas.PushTransform` apply the translation-column scaling at render-node application time.
   - `render-scale.md` — how `RenderScale` flows from `Renderer` → `GraphicsContext2D` → `FilterEffectContext` and how nested scenes override it.

3. **Quickstart** in `quickstart.md` — concrete commands for build / test / SSIM-fixture regeneration, plus a "smoke project" the developer can render at two resolutions to eyeball-verify.

4. **Agent context update**: append a reference to this plan inside the `<!-- SPECKIT START -->` / `<!-- SPECKIT END -->` markers in `CLAUDE.md`. (If those markers don't yet exist in `CLAUDE.md`, add them just before the "Claude Code-specific notes" section so future `/speckit-*` runs can update in place.)

## Phase 2 — Tasks (output: handled by `/speckit-tasks`)

Out of scope for this command. Expected shape (preview only):

- T1 (Engine): introduce `RenderScale` with uniform-scale enforcement and its tests.
- T2 (Engine): plumb `ReferenceFrame` and `RenderScale` through `IRenderer` / `Renderer` / `GraphicsContext2D`. Add nested-scene push/pop via `SceneDrawable`.
- T3 (Engine): modify every length-taking helper on `FilterEffectContext` to multiply by `RenderScale`; add `*Raw` twin per helper. Add per-helper unit tests covering scale, sub-pixel, zero, NaN, and `*Raw` bypass.
- T4 (Tests): add SSIM-based proxy-equivalence tests parameterised over the 13 in-scope effects (their `.cs` files unchanged); add the legacy-corpus regression tests; add the 3D-with-2D test.
- T5 (Docs): plugin-author migration guide (mostly "do nothing; here is `*Raw` if you need to opt out"); update `docs/ai-workflow/coding-guidelines-for-ai.md`.
- T6 (Verify): run `/beutl-build`, `/beutl-test`, `/beutl-format`, `/beutl-coverage` and address `beutl-design-reviewer` feedback.
