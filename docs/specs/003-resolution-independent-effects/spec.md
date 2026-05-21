# Feature Specification: Resolution-Independent Pixel-Absolute Rendering

**Feature Branch**: `speckit/003-resolution-independent-effects` (slug retained for history continuity; scope broadened from "effects" to "rendering" during the design pivot — see Clarifications)

**Created**: 2026-05-20

**Status**: Draft

**Input**: Original — "ピクセル絶対値エフェクトを resolution-independent 化して proxy preview と export の見た目を一致させる。" Scope-expansion follow-up — "エフェクト以外の部分も resolution-independent にする必要があります" (Transform / Pen / Shape / direct `GraphicsContext2D` draw calls also need to be resolution-independent for the proxy-preview vs. export match to hold end-to-end).

## Clarifications

### Session 2026-05-20

- Q: What unit semantic do effect parameters expose to the user and to project files? → A: "Pixels at the project's export resolution." A saved numeric value `N` means "`N` pixels measured against the export raster"; the renderer scales it to the current raster at draw time. No file-format migration is required because legacy values already match this interpretation.
- Q: Which reference frame applies when an effect is inside a nested composition (sub-scene, layer effect)? → A: The innermost containing scene's own configured frame size. Sub-scenes are self-contained: an effect inside a 1920×1080 sub-scene resolves against 1920×1080, and the outer project then composes/scales that result like any other source.
- Q: How do plugin and user-authored effects declare a parameter as pixel-absolute? → A: They do **nothing**. Scaling is applied by the `FilterEffectContext` helper methods (`context.Blur(Size)`, `context.DropShadow(Point, Size, Color)`, `context.Erode(float, float)`, …) — they internally multiply by the current `RenderScale` before forwarding to Skia. Existing `IProperty<Size>` / `IProperty<Point>` / `IProperty<float>` declarations stay as-is; no new wrapper types are introduced; every effect that uses the standard helpers automatically becomes resolution-independent. A `*Raw` family of helpers (e.g. `context.BlurRaw(Size)`) is provided for the niche case where a plugin needs to bypass scaling.
- Q: What is the acceptance metric for "visually equivalent" between proxy (upscaled) and export? → A: SSIM ≥ 0.97 per scene on the test corpus, computed after upscaling the proxy frame to export size with bicubic resampling. The same metric and threshold also gate the legacy-vs-new comparison.
- Q: How are proxy resolutions handled when they would otherwise be a non-uniform scale of the export resolution? → A: Proxy must be a uniform scale of export. Requests that would result in a non-uniform ratio are snapped to the nearest uniform scale (with the chosen ratio observable to the user). Non-square-pixel handling is explicitly out of scope.

### Session 2026-05-21 (scope expansion)

- Q: Does the scope include non-FilterEffect pixel-absolute rendering primitives — `Transform` translations, `Pen.Thickness`, `Shape.Width/Height`, direct `GraphicsContext2D.DrawRectangle / PushTransform / PushClip` calls? → A: **Yes — they must all be resolution-independent for proxy preview vs. export to actually match end-to-end.** The same helper-internal-scaling design pattern applies: every API entry point that accepts a length-typed argument scales internally by the context's `RenderScale`, paired with a `*Raw` opt-out twin. `Pen.Thickness` is scaled at Pen materialization / consumption; `Transform.CreateMatrix` scales its translation component; `GraphicsContext2D` direct-draw and `Push*` helpers scale Rect / Matrix arguments. `Shape` subclasses (`RectShape`, `EllipseShape`, `RoundedRectShape`) benefit automatically because they call `DrawRectangle` / `DrawEllipse` internally.
- Q: Which surfaces remain out of scope for this PR? → A: **`Geometry` path coordinates**, **`TextBlock.Size` / `Spacing` (text typeface materialization)**, **`Brush` rectangles (TileBrush, ImageBrush SourceRect/DestinationRect)** — these have larger materialization paths or touch typography rendering. Tracked as follow-ups in `data-model.md` § "Deferred follow-ups".

## Background

A wide range of rendering primitives in Beutl express their parameters as **absolute pixel values**:

- Filter effects — blur sigma, drop-shadow position and sigma, stroke offset, dilate / erode radius, etc.
- Transforms — `TranslateTransform.X / Y`, `Rotation3DTransform.Center*` and `Depth`, the translation component of `MatrixTransform`.
- Pens — `Pen.Thickness`, `Pen.DashOffset`, `Pen.Offset` flow into Skia stroke parameters.
- Shapes — `RectShape.Width / Height`, `EllipseShape.Width / Height`, `RoundedRectShape.Width / Height / Smoothing / CornerRadius`.
- Direct `GraphicsContext2D` API — `DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushTransform(Matrix)`, `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect)`.

Beutl supports working at a lower-resolution **proxy preview** during editing (for performance) and rendering the final output at the project's full export resolution.

Today, every one of those length-typed values is interpreted *literally* against whatever raster the renderer is currently writing into. The proxy preview is a smaller raster than the export raster, so the same project file produces visibly different output between the two: a blur that looks subtle on the proxy becomes a soft haze in the export; a 4 px stroke shrinks to a hairline; a rectangle drawn at "200 px wide" stays the same nominal width in raster pixels even as the surrounding picture shrinks. Users currently have no reliable way to author and check work on the proxy and trust that the export will match.

This feature makes every in-scope pixel-absolute parameter **resolution-independent**, so that the *visual* result of a project at proxy resolution and at export resolution is the same up to sampling differences. The mechanism is uniform: every API entry point that accepts a length-typed argument multiplies by the current `RenderScale` before forwarding to the rasterizer; a `*Raw` twin of each such entry point provides an explicit opt-out for the niche case where raw-raster pixel semantics are desired.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Proxy preview visually matches export (Priority: P1)

A user edits a project at a reduced proxy resolution (for example, a 1920×1080 project previewed at 480×270 to keep playback smooth) and composes their scene — blur and drop-shadow effects, translation transforms, stroked shapes drawn through `RectShape` / `EllipseShape`, dynamic `PushClip` regions — until the look is right. They then export at full resolution. The exported video has the same visual character — blur softness, shadow spread, stroke thickness, transform offsets, shape sizes, clip regions — as what they saw in the preview, just sharper because of the higher pixel count.

**Why this priority**: This is the entire reason the feature exists. Without it, every user who relies on proxy preview discovers their export looks different and either gives up on proxy or wastes time re-rendering at full resolution to verify. Fixing this unlocks the proxy workflow end-to-end — not just for effects but for the whole rendering pipeline.

**Independent Test**: Take a single project containing each affected primitive (effects, transforms, pens, shapes, direct `GraphicsContext2D` calls), render it once at proxy resolution and once at export resolution, scale the proxy frame up to export size with simple resampling, and compare side-by-side. The two should agree on the qualitative look — not pixel-identical, but visually equivalent.

**Acceptance Scenarios**:

1. **Given** a project with a Blur effect of a specific strength applied to a graphic, **When** the user renders it at 1/4 proxy resolution and at full export resolution, **Then** the perceived blur softness is the same in both outputs (after scaling the proxy up for comparison).
2. **Given** a project with a DropShadow effect, **When** the user renders at proxy and at export, **Then** the shadow's offset, blur, and color shape match between the two outputs in relative terms.
3. **Given** a project with a `TranslateTransform` of `(X = 100, Y = 50)` applied to a `RectShape` of `(Width = 200, Height = 100)` stroked with a `Pen` of `Thickness = 4`, **When** the user renders at 1/4 proxy and at export, **Then** the rectangle's position relative to the frame, its size relative to the frame, and the stroke's thickness as a fraction of the rectangle all match across the two outputs.
4. **Given** a project that pushes a `PushClip(Rect)` region and draws inside it, **When** rendered at proxy and at export, **Then** the clip region covers the same proportion of the frame at both resolutions.

---

### User Story 2 - Existing projects keep their current appearance (Priority: P1)

A user opens a project they authored before this change. They expect the visual output at the original export resolution to be **the same as before** — none of their saved work should suddenly look different. They also expect that after the upgrade, the proxy preview now matches that export look, even though they never re-edited the project.

**Why this priority**: Backward visual compatibility is the difference between a quality-of-life improvement and a destructive change. If projects break visually on upgrade, users lose trust in the editor and may not upgrade at all.

**Independent Test**: Render a representative library of pre-feature projects (covering all affected effect types) on the new build at full export resolution, and compare against a baseline render produced by the old build. Visual output must match the baseline within a documented tolerance. Then render the same projects at proxy resolution on the new build and confirm they match the export, demonstrating the new behavior also applies to legacy files.

**Acceptance Scenarios**:

1. **Given** a project saved with the previous version of Beutl that uses Blur, DropShadow, StrokeEffect, or any other newly resolution-independent effect, **When** the user opens it in the new version and exports at the original export resolution, **Then** the exported video matches the previous version's output within a documented visual tolerance.
2. **Given** the same legacy project, **When** the user previews at proxy resolution in the new version, **Then** the proxy preview matches the new export rendering (closing the proxy-vs-export gap retroactively).

---

### User Story 3 - Authoring units are predictable across project sizes (Priority: P2)

A user duplicates a 1920×1080 project as a 3840×2160 master version. They expect the effects to scale proportionally — a stroke that read as a "4 px outline" in the HD project should look like the same relative outline in the 4K version, not a hairline that was overlooked.

**Why this priority**: Multi-resolution masters and template reuse are common workflows. Without consistent semantics, every duplicated project requires the user to re-tune every effect.

**Independent Test**: Author a project at one export resolution, duplicate the project and change only the export resolution to a higher one, render both, and compare. The effect strength relative to the picture should be preserved.

**Acceptance Scenarios**:

1. **Given** a project authored at 1920×1080 export resolution with a configured Blur, **When** the user changes the project's export resolution to 3840×2160 without editing the Blur parameter, **Then** the blur strength relative to the picture stays the same.

---

### Edge Cases

- **Anisotropic parameters**: Blur sigma is a 2D size (X and Y can differ), drop-shadow position is a 2D offset. Each axis must scale according to the corresponding axis of the render target so that a non-square frame does not warp the effect.
- **Non-uniform proxy ratios**: Proxy resolution MUST be a uniform scale of the export resolution. A requested proxy size that would yield a non-uniform per-axis ratio is snapped to the nearest uniform scale, and the actual applied ratio is observable to the user. Non-square-pixel handling is out of scope.
- **Animated parameters / keyframes**: Effect parameter animations authored against the project must replay with the same visual trajectory at proxy and export.
- **Composed / nested effects**: Effects applied inside a nested scene, layer effect, or container must be evaluated against the appropriate reference frame, not the outer raster, so that a nested composition keeps its intended look when the outer raster is at a different resolution.
- **3D / Graphics3D content**: The 3D pipeline already renders into a target framebuffer at whatever size the renderer chose; any 2D filter effects applied to 3D output must follow the same resolution-independent rule. Verified by `tests/Beutl.Graphics3DTests/FilterEffects/Render3DWithFilterResolutionTests.cs` (see tasks T038a).
- **Plugin / user-authored effects**: Third-party effects (`CSharpScriptEffect`, `GLSLScriptEffect`, custom `FilterEffect` subclasses) cannot be automatically migrated; the system must surface a documented contract so plugin authors know how to opt in.
- **Sub-pixel parameters**: Any length whose authored value is below 1 px at proxy resolution (e.g. a 0.4 px stroke or shape edge after scaling) must degrade gracefully — typically by clamping to the minimum the underlying rasterizer supports — without producing zero-width or invisible output.
- **Zero / disabled values**: A length of 0 must remain 0 at any resolution (no division-by-zero, no spurious minimum width).
- **Out-of-scope surfaces (this PR)**: `Geometry` path coordinates, `TextBlock.Size / Spacing` (font and glyph metrics), and `Brush` rectangles (`TileBrush`, `ImageBrush` SourceRect / DestinationRect) remain raw-raster in this PR — see Assumptions and `data-model.md` § "Deferred follow-ups". These are tracked as separate features.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All in-scope pixel-absolute rendering primitives MUST produce visually equivalent output regardless of the resolution of the raster they are rendered into, given the same project. The in-scope set covers FilterEffects, Transforms, Pens, Shapes, and direct `GraphicsContext2D` length-typed entry points (`DrawRectangle`, `DrawEllipse`, `PushTransform` translation, `PushClip`, `PushLayer`, `PushOpacityMask` bounds).
- **FR-002**: The exact in-scope surface MUST be enumerated in `data-model.md` and each entry MUST be matched by a test. At a minimum the surface includes:
  - **FilterEffects** — Blur, DropShadow, InnerShadow, StrokeEffect.Offset, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform (3 subclasses), MosaicEffect, ShakeEffect, SplitEffect, Clipping. (13 effects per T001 audit.)
  - **Transforms** — `TranslateTransform.X / Y`, `Rotation3DTransform.CenterX / CenterY / CenterZ / Depth`, the translation column of `MatrixTransform.Matrix`. (Pure rotation, scale (%), and skew (deg) stay raw — dimensionless.)
  - **Pen** — `Thickness`, `DashOffset`, `Offset`. (`MiterLimit` is a multiplier; `TrimStart / TrimEnd / TrimOffset` are %.)
  - **Shapes** — `RectShape`, `EllipseShape`, `RoundedRectShape` `Width / Height / Smoothing / CornerRadius`. These flow through `DrawRectangle` / `DrawEllipse` and benefit automatically once those helpers scale.
  - **`GraphicsContext2D` direct API** — `DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushTransform(Matrix)` translation column, `PushTransform(Transform.Resource)` materialized matrix translation, `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect)`.
- **FR-003**: A project saved before this feature MUST, when opened on the new build, produce export-resolution output that is visually equivalent (SSIM ≥ 0.97 per scene against the previous build's output for the same file) without any project-file migration step. Numeric parameter values in the project file MUST NOT be rewritten on load.
- **FR-004**: Proxy-resolution rendering of any project (legacy or new) MUST produce output visually equivalent to that project's export-resolution rendering, measured as SSIM ≥ 0.97 after upscaling the proxy frame to the export size with bicubic resampling.
- **FR-005**: Effect parameters that carry pixel-absolute lengths MUST be interpreted as "pixels measured against the project's export resolution" at both edit time and render time. No new parameter types are introduced — existing `IProperty<Size>` / `IProperty<Point>` / `IProperty<float>` declarations on effects stay verbatim, and the property editor displays them exactly as it does today. The unit semantic is established by the `FilterEffectContext` helper contract (see FR-008): a numeric value of `N` shown in the property editor means `N` pixels in the exported frame, regardless of the resolution currently being previewed.
- **FR-006**: The system MUST handle anisotropic parameters (2D sizes, 2D offsets) by scaling each axis independently against the corresponding axis of the reference frame.
- **FR-007**: The system MUST scale animated parameter values (keyframes, easings) consistently with static values, so that animation timing and amplitude are preserved across resolutions.
- **FR-008**: The system MUST apply resolution-independent scaling implicitly inside the existing length-taking rendering API entry points:
  - **`FilterEffectContext` helpers** — `Blur(Size)`, `DropShadow(Point, Size, Color)`, `DropShadowOnly`, `InnerShadow`, `InnerShadowOnly`, `Erode(float, float)`, `Dilate(float, float)`, etc. Each multiplies its length argument by `this.RenderScale` before forwarding.
  - **`GraphicsContext2D` direct helpers** — `DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushTransform(Matrix)` (translation column), `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect)` and `PushTransform(Transform.Resource)` (read `transform.Matrix`, scale translation column). Same multiplication rule.
  - **`Pen` materialization** — `Pen.Resource.Thickness`, `DashOffset`, `Offset` are scaled at materialization time (or, equivalently, at every consumption site via a shared helper). `MiterLimit`, `TrimStart`, `TrimEnd`, `TrimOffset` stay raw.
  - **`Transform.CreateMatrix`** — for `TranslateTransform`, `Rotation3DTransform`, and `MatrixTransform`, the translation component of the produced `Matrix` is multiplied by the composition context's `RenderScale`. Pure rotation / scale (%) / skew (deg) classes are unchanged.

  The same system MUST also expose a `*Raw` variant of every scaled API entry point (e.g. `BlurRaw(Size)`, `DrawRectangleRaw(Rect)`, `PushTransformRaw(Matrix)`, `PushClipRaw(Rect)`) that bypasses the scaling step, for the niche case where a caller needs raw-raster pixel semantics. Existing effects, drawables, transforms, and pens — built-in or third-party — that call the scaled API automatically become resolution-independent on upgrade; no source change is required on the caller side.
- **FR-009**: For sub-pixel and zero-valued cases, the system MUST degrade gracefully (clamp to the rasterizer's minimum, preserve zero exactly) without producing invisible or unbounded output.
- **FR-010**: For nested compositions (scene-within-scene, layer effects), pixel-absolute parameters MUST resolve against the **innermost containing scene's own configured frame size**, not the outer raster. The outer project then composes/scales the sub-scene's result like any other source, so a sub-scene's internal look is preserved when reused in a different outer resolution.
- **FR-011**: Existing automated tests covering these effects MUST continue to pass, and new tests MUST cover the proxy-vs-export visual-equivalence acceptance criterion and the legacy-file equivalence criterion (FR-003, FR-004).

### Key Entities

- **Project export resolution**: The canonical pixel size at which the final output is produced. Serves as the reference frame against which resolution-independent parameter values are interpreted at render time.
- **Proxy preview resolution**: A reduced pixel size derived from the export resolution for interactive editing; the per-frame target for the editor's preview renderer.
- **Pixel-absolute effect parameter**: A parameter on a filter effect whose value has dimensions of length in pixels — e.g. blur sigma, shadow offset, stroke width. These are the parameters in scope for this feature. They keep their existing primitive types (`Size`, `Point`, `float`); the unit semantic is established by the helper they are passed to.
- **`FilterEffectContext` helper contract**: The set of methods on `FilterEffectContext` (`Blur`, `DropShadow`, `Erode`, …) that effect implementations call. After this feature, every such helper takes its length-typed arguments as "pixels at the project's export resolution" and scales internally; the matching `*Raw` variant takes the same arguments as raw raster pixels. This contract is what plugin authors implicitly follow.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For each in-scope built-in effect (FR-002), a rendered comparison at proxy resolution (representative ratio, e.g. 1/4) and at full export resolution achieves SSIM ≥ 0.97 per scene on a curated test corpus, computed after upscaling the proxy frame to the export size with bicubic resampling.
- **SC-002**: Across a curated regression corpus of projects authored on the previous version, every project renders at full export resolution with SSIM ≥ 0.97 per scene against the previous version's output for the same file — no silent visual regression for users who upgrade.
- **SC-003**: For a representative project, a user can author at proxy preview and have the final export match their visual intent without re-tuning effect parameters — measured by zero parameter edits required between "looks right on proxy" and "looks right on export" in a held-out evaluation set.
- **SC-004**: Plugin authors and user-authored effects have a documented and adopted path to opt in to resolution-independent behavior — verified by at least the bundled scripting examples (`CSharpScriptEffect`, `GLSLScriptEffect`) demonstrating the contract.
- **SC-005**: The total in-scope effect list is enumerated and traceable in tests — every effect on that list has a dedicated test in `tests/Beutl.UnitTests` (or the appropriate test project) covering the proxy-equivalence and legacy-equivalence cases.

## Assumptions

- The project has a single canonical export resolution that is the natural reference frame for parameter values; if a project changes its export resolution, the user explicitly accepts that effect parameters now describe the new reference frame.
- Proxy preview is a uniform-scale resampling of the export frame (not, for example, an arbitrary crop); the scaling factor is a known runtime quantity available to the renderer.
- "Visually equivalent" between proxy (upscaled) and export means *perceptually* matching at typical viewing distances, not bit-identical — sub-pixel rasterization and sampling differences are accepted within the documented tolerance.
- The previous-version visual baseline used by FR-003 is captured before this change ships, so that comparisons are against the actual prior behavior rather than a written description of it.
- Effects whose parameters are *already* dimensionless (color, opacity, blend mode, boolean toggles, etc.) are out of scope and continue to behave exactly as before.
- 3D-specific parameters (camera, lighting, geometry units in `Beutl.Graphics3D`) are out of scope; only 2D filter effects applied to the 3D output are in scope.
- Third-party plugins shipped today are not silently rewritten; they automatically benefit from helper-internal scaling without source change. Plugins that need to opt out use the `*Raw` variant of the helper (one method-name suffix change per call site).
- **Out-of-scope surfaces** explicitly deferred to follow-up PRs: `Geometry` path coordinates (touches `Geometry.Resource` and Skia path materialization), `TextBlock.Size / Spacing` (touches typeface materialization and glyph layout), and pixel-absolute `Brush` rectangles (`TileBrush`, `ImageBrush.SourceRect / DestinationRect`).
