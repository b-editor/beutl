# Feature Specification: Resolution-Independent Pixel-Absolute Effects

**Feature Branch**: `speckit/003-resolution-independent-effects`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "ピクセル絶対値エフェクトを resolution-independent 化して proxy preview と export の見た目を一致させる。"

## Clarifications

### Session 2026-05-20

- Q: What unit semantic do effect parameters expose to the user and to project files? → A: "Pixels at the project's export resolution." A saved numeric value `N` means "`N` pixels measured against the export raster"; the renderer scales it to the current raster at draw time. No file-format migration is required because legacy values already match this interpretation.
- Q: Which reference frame applies when an effect is inside a nested composition (sub-scene, layer effect)? → A: The innermost containing scene's own configured frame size. Sub-scenes are self-contained: an effect inside a 1920×1080 sub-scene resolves against 1920×1080, and the outer project then composes/scales that result like any other source.
- Q: How do plugin and user-authored effects declare a parameter as pixel-absolute? → A: Via dedicated typed wrappers (`PixelLength`, `PixelSize`, `PixelPoint` — exact names finalized in plan) that the property system and renderer both recognize. Existing plain `Size`/`Point` properties on third-party effects keep their current raw-pixel behavior so no out-of-tree effect changes on upgrade; opting in is an explicit type change.
- Q: What is the acceptance metric for "visually equivalent" between proxy (upscaled) and export? → A: SSIM ≥ 0.97 per scene on the test corpus, computed after upscaling the proxy frame to export size with bicubic resampling. The same metric and threshold also gate the legacy-vs-new comparison.
- Q: How are proxy resolutions handled when they would otherwise be a non-uniform scale of the export resolution? → A: Proxy must be a uniform scale of export. Requests that would result in a non-uniform ratio are snapped to the nearest uniform scale (with the chosen ratio observable to the user). Non-square-pixel handling is explicitly out of scope.

## Background

Several built-in filter effects in Beutl express their parameters as **absolute pixel values** — most notably blur sigma, drop-shadow position and sigma, stroke offset and width, dilate/erode radius, and any effect whose size is measured in raster pixels. Beutl supports working at a lower-resolution **proxy preview** during editing (for performance) and rendering the final output at the project's full export resolution.

Today, a parameter such as "blur sigma = 20 px" is interpreted *literally* against whatever raster the renderer is currently writing into. The proxy preview is a smaller raster than the export raster, so the same project file produces visibly different output between the two: a blur that looks subtle on the proxy becomes a soft haze in the export, a 4 px stroke shrinks to a thin line on the proxy, etc. Users currently have no reliable way to author and check work on the proxy and trust that the export will match.

This feature makes the pixel-absolute parameters of built-in filter effects **resolution-independent**, so that the *visual* result of a project at proxy resolution and at export resolution is the same up to sampling differences.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Proxy preview visually matches export (Priority: P1)

A user edits a project at a reduced proxy resolution (for example, a 1920×1080 project previewed at 480×270 to keep playback smooth) and adjusts blur, drop-shadow, and stroke effects until the look is right. They then export at full resolution. The exported video has the same visual character — blur softness, shadow spread, stroke thickness, dilation amount — as what they saw in the preview, just sharper because of the higher pixel count.

**Why this priority**: This is the entire reason the feature exists. Without it, every user who relies on proxy preview discovers their export looks different and either gives up on proxy or wastes time re-rendering at full resolution to verify. Fixing this unlocks the proxy workflow.

**Independent Test**: Take a single project containing each affected effect, render it once at proxy resolution and once at export resolution, scale the proxy frame up to export size with simple resampling, and compare side-by-side. The two should agree on the qualitative look (same softness, same shadow shape, same stroke proportion) — not pixel-identical, but visually equivalent.

**Acceptance Scenarios**:

1. **Given** a project with a Blur effect of a specific strength applied to a graphic, **When** the user renders it at 1/4 proxy resolution and at full export resolution, **Then** the perceived blur softness is the same in both outputs (after scaling the proxy up for comparison).
2. **Given** a project with a DropShadow effect, **When** the user renders at proxy and at export, **Then** the shadow's offset, blur, and color shape match between the two outputs in relative terms.
3. **Given** a project with a StrokeEffect, **When** the user renders at proxy and at export, **Then** the stroke covers the same proportion of the underlying shape at both resolutions.

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
- **Sub-pixel parameters**: Effects whose authored value is below 1 px at proxy resolution (e.g. a 0.4 px stroke after scaling) must degrade gracefully — typically by clamping to the minimum the underlying rasterizer supports — without producing zero-width or invisible output.
- **Zero / disabled effects**: A parameter of 0 must remain 0 at any resolution (no division-by-zero, no spurious minimum width).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: All built-in filter effects whose parameters are currently expressed in raster pixels MUST produce visually equivalent output regardless of the resolution of the raster they are rendered into, given the same project.
- **FR-002**: The set of in-scope built-in effects MUST include at minimum: Blur (Sigma), DropShadow (Position, Sigma), InnerShadow, StrokeEffect (Offset, stroke width), Dilate / Erode (Radius), Border, ColorShift offsets, and any other built-in effect whose parameter has length units of pixels. The final in-scope list MUST be enumerated in the plan and matched by tests.
- **FR-003**: A project saved before this feature MUST, when opened on the new build, produce export-resolution output that is visually equivalent (SSIM ≥ 0.97 per scene against the previous build's output for the same file) without any project-file migration step. Numeric parameter values in the project file MUST NOT be rewritten on load.
- **FR-004**: Proxy-resolution rendering of any project (legacy or new) MUST produce output visually equivalent to that project's export-resolution rendering, measured as SSIM ≥ 0.97 after upscaling the proxy frame to the export size with bicubic resampling.
- **FR-005**: Effect parameters that carry pixel-absolute lengths MUST be interpreted as "pixels measured against the project's export resolution" at both edit time and render time. The authoring UI MUST surface this unit consistently; a value of `N` shown in the property editor means `N` pixels in the exported frame, regardless of the resolution currently being previewed.
- **FR-006**: The system MUST handle anisotropic parameters (2D sizes, 2D offsets) by scaling each axis independently against the corresponding axis of the reference frame.
- **FR-007**: The system MUST scale animated parameter values (keyframes, easings) consistently with static values, so that animation timing and amplitude are preserved across resolutions.
- **FR-008**: The system MUST expose dedicated typed parameter wrappers (working names `PixelLength`, `PixelSize`, `PixelPoint`; final names finalized in plan) that any effect — built-in, scripted, or third-party — uses to declare a parameter as pixel-absolute and thereby opt in to resolution-independent scaling. Existing plain `Size`/`Point` properties on out-of-tree effects MUST keep their current raw-pixel semantics on upgrade; opting in requires an explicit type change made by the effect's author.
- **FR-009**: For sub-pixel and zero-valued cases, the system MUST degrade gracefully (clamp to the rasterizer's minimum, preserve zero exactly) without producing invisible or unbounded output.
- **FR-010**: For nested compositions (scene-within-scene, layer effects), pixel-absolute parameters MUST resolve against the **innermost containing scene's own configured frame size**, not the outer raster. The outer project then composes/scales the sub-scene's result like any other source, so a sub-scene's internal look is preserved when reused in a different outer resolution.
- **FR-011**: Existing automated tests covering these effects MUST continue to pass, and new tests MUST cover the proxy-vs-export visual-equivalence acceptance criterion and the legacy-file equivalence criterion (FR-003, FR-004).

### Key Entities

- **Project export resolution**: The canonical pixel size at which the final output is produced. Serves as the reference frame against which resolution-independent parameter values are interpreted at render time.
- **Proxy preview resolution**: A reduced pixel size derived from the export resolution for interactive editing; the per-frame target for the editor's preview renderer.
- **Pixel-absolute effect parameter**: A parameter on a filter effect whose value has dimensions of length in pixels — e.g. blur sigma, shadow offset, stroke width. These are the parameters in scope for this feature.
- **Reference-frame contract**: The (yet-to-be-finalized) shared agreement between effect implementations and the renderer about which raster size a length-valued parameter is interpreted against. This contract is what plugin authors need to follow.

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
- Third-party plugins shipped today are not silently rewritten; they keep their current behavior until they explicitly adopt the new contract (FR-008). Documenting and announcing the migration path is part of this feature; rewriting other people's code is not.
