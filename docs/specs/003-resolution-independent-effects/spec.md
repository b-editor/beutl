# Feature Specification: Per-Clip Proxy via RenderNodeOperation Correction Scale

**Feature Branch**: `speckit/003-resolution-independent-effects` (slug retained for history continuity; the original problem statement focused on filter effects, but the eventual design covers the full render pipeline via per-clip proxy)

**Created**: 2026-05-20

**Last revised**: 2026-05-22

**Status**: Draft (full rewrite — see § "Design history" at the end of this file)

**Input**: Originally "ピクセル絶対値エフェクトを resolution-independent 化して proxy preview と export の見た目を一致させる。"; clarified during design review (2026-05-22) to actually mean **per-clip proxy with bottom-up CorrectionScale propagation, not uniform scene-wide proxy**.

## Clarifications

### Session 2026-05-22 (architecture clarification)

- Q: Is "proxy preview" applied to the whole scene uniformly, or per-clip? → A: **Per-clip.** Heavy sources (e.g. 4K video) render at a reduced resolution (e.g. 1/4) while lightweight elements (text overlays, vector shapes) stay at full export resolution within the same scene render pass. Prior design drafts of this spec assumed scene-wide uniform proxy and were entirely wrong about the user model.
- Q: How does the scale propagate through the render graph? → A: **Bottom-up via `RenderNodeOperation.CorrectionScale`.** Each `RenderNodeOperation` carries (in addition to `Bounds`) a `CorrectionScale` value indicating "the upstream produced its raster at this fraction of authoring resolution; the consumer must upscale by `CorrectionScale` to align this raster with `Bounds`." Sources declare their own scale; transformer nodes (Filter, Transform, Clip, Layer) read upstream's `CorrectionScale` and adjust their own behavior accordingly; the top-level compositor performs the final upscale blit.
- Q: What is the impact on Drawable / FilterEffect / Shape authors (extension authors)? → A: **None.** Authoring APIs (`IProperty<Size>`, `IProperty<Point>`, `IProperty<float>`, `context.Blur(sigma)`, `RectShape.Width`, etc.) stay verbatim. The scale handling lives one layer below in `RenderNode.Process` — the RenderNode that the Resource path constructs is where `CorrectionScale` is consumed and propagated. Implementors of new RenderNode subclasses do need to handle `CorrectionScale`, but that is a less common, lower-level extension point.
- Q: What about the previously-proposed `RenderScale` propagation through `GraphicsContext2D` / `FilterEffectContext` / `PenHelper`? → A: **Abandoned in this rewrite.** Those mechanisms made sense for scene-wide proxy but are wrong for per-clip proxy. The single source of truth is now `RenderNodeOperation.CorrectionScale`.

## Background

Beutl's render pipeline is built around a render-node graph. Each `RenderNode.Process(RenderNodeContext)` returns one or more `RenderNodeOperation` objects, each of which knows its `Bounds` (the rectangle in the *parent's coordinate space* where this content belongs) and how to `Render` itself into an `ImmediateCanvas`.

Real-world editing workflows want **per-clip proxy**: when a project contains a heavy clip (a 4K video file, an expensive procedural source, a deeply nested sub-scene), the editor renders that clip into a smaller raster (e.g. 1/4 of authoring resolution) for responsive playback; meanwhile lightweight elements in the same scene (text overlays, vector shapes drawn on the fly, image stamps) stay at full quality. At export time, every clip renders at its full authoring resolution and the proxy mechanism is inactive.

Today there is no such mechanism. Every clip in every scene renders at the scene's full resolution, every frame, which makes preview unresponsive on heavy sources and forces the user to choose between quality and editability.

This feature introduces the engine-side mechanism that makes per-clip proxy possible: each `RenderNodeOperation` gains a `CorrectionScale` field; source nodes declare their own resolution choice; transformer nodes propagate and respond. The user-facing proxy *toggle* UX is a separate feature (out of scope here, see § "Future work"); this PR is the rendering-layer plumbing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Heavy 4K video can be previewed responsively without visual divergence from export (Priority: P1)

A user has a 4K (3840×2160) video clip in a 1080p project. During editing they toggle proxy on for that clip, which renders the video at 1/4 (960×540) into a CorrectionScale=4 operation. The scene compositor blits this raster back to its full 1920×1080 placement. Filter effects applied to the video (Blur, DropShadow, etc.) operate on the 960×540 raster with parameters scaled by the upstream CorrectionScale, so the visual character of the filter matches what export produces. Meanwhile, text overlays in the same scene render at the full 1920×1080 raster with no proxy involvement. At export time, the video proxy is disabled and the result is identical to today's full-resolution render.

**Why this priority**: This is the entire reason the feature exists. Without per-clip proxy, editors are forced into all-or-nothing performance tradeoffs.

**Independent Test**: Render the same project at proxy-on and proxy-off settings. Compare the proxy frame (after the compositor's upscale blit) to the export frame using SSIM. They should agree (SSIM ≥ 0.97) for the video portion AND the text portion. Filter effects on the video should look the same in both renders (the proxy version blurred a smaller raster with smaller sigma, then upscaled; the export version blurred a larger raster with larger sigma; the visual result is equivalent).

**Acceptance Scenarios**:

1. **Given** a 1080p project containing a 4K video clip with Blur(sigma=20) applied, **When** the user enables proxy on the video clip and previews, **Then** the preview frame shows the same blurred video (proportions and softness) as the export frame, just rendered through a 960×540 intermediate raster.
2. **Given** the same project with text overlays, **When** rendered with proxy enabled on the video, **Then** the text overlays render at full 1920×1080 quality regardless of the video's proxy setting.
3. **Given** the project rendered without proxy (export-time), **Then** every render-node-operation reports `CorrectionScale = Identity` and behavior is byte-equivalent (SSIM-equivalent if guards fire) to today's output.

---

### User Story 2 - Extension authors (Drawable / FilterEffect / Shape) do not change their code (Priority: P1)

A third-party plugin author who has shipped a custom `Drawable` or `FilterEffect` rebuilds against the new `Beutl.Engine`. Their `*.cs` files do not change. Their authoring API (`IProperty<Size> Sigma { get; }`, `context.Blur(r.Sigma)`, `DrawRectangle(Rect)`, etc.) continues to take values in authoring space. The plugin automatically participates in per-clip proxy when downstream of a proxied source.

**Why this priority**: Beutl's ecosystem health depends on plugin stability across upgrades. A redesign that requires every plugin author to change their code is a non-starter.

**Independent Test**: Compile a representative sample of existing built-in effects (Blur, DropShadow, ColorShift, ShakeEffect — 13 effects per the original audit) and any third-party plugins included in the test corpus, against the new build. Run the existing test suite; it should pass unchanged. Then run the new per-clip proxy tests; the plugins should automatically pass them too.

**Acceptance Scenarios**:

1. **Given** the in-tree built-in effects (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform × 3, MosaicEffect, ShakeEffect, SplitEffect, Clipping — 13 effects per T001 audit), **When** the new build runs the existing test suite, **Then** all tests pass without modifying any effect's `.cs` file.
2. **Given** a custom Drawable that calls `context.DrawRectangle(rect, fill, pen)` with `pen.Thickness = 4` and `rect = (0, 0, 200, 100)`, **When** the drawable is inside a proxied sub-tree, **Then** the rendering pipeline produces correct output (200×100 region at 4 px stroke in authoring space, downscaled appropriately for the proxy raster, then upscaled by the compositor).

---

### User Story 3 - Existing projects without proxy settings render unchanged (Priority: P1)

A user opens a project saved before this feature. The project has no per-clip proxy settings (all clips render at full). The new build produces output visually equivalent (SSIM ≥ 0.97) to the previous build at export resolution. No project-file migration runs.

**Why this priority**: Backward compatibility is non-negotiable. Upgrade must not break existing renders.

**Independent Test**: Render a curated corpus of pre-feature projects on the new build and compare to baselines captured from the previous build. All scenes must match within tolerance. Serialization round-trip must be byte-equal.

**Acceptance Scenarios**:

1. **Given** any pre-feature project file, **When** opened on the new build with no proxy settings, **Then** every `RenderNodeOperation` reports `CorrectionScale = Identity` throughout the graph.
2. **Given** the same project rendered at export, **When** compared to the previous build's output, **Then** SSIM ≥ 0.97 per scene; JSON round-trip is byte-equal.

---

### Edge Cases

- **Nested scenes**: A `Scene` placed inside another `Scene` is a source-like node. The inner scene's renderer decides its own proxy strategy (independent of the outer scene's). The inner scene's operation carries its own `CorrectionScale`. The outer scene's compositor treats it like any other source.
- **Filter chains crossing source boundaries**: A `Blur` applied to a video source with `CorrectionScale = 4` runs on the 1/4 raster with `sigma_internal = sigma_authored / 4`. A `Blur` applied to a text source with `CorrectionScale = 1` runs with `sigma_internal = sigma_authored / 1 = sigma_authored`. A subsequent `DropShadow` after the `Blur` continues with the same upstream `CorrectionScale` as the `Blur` produced.
- **Multiple sources in one scene with different proxy levels**: A scene with a 4K video at proxy=1/4 (`CorrectionScale=4`), a 1080p image at proxy=1/2 (`CorrectionScale=2`), and text at full (`CorrectionScale=1`) all coexist. The compositor handles each operation's blit independently.
- **Transform across a proxied source**: A `TranslateTransform(X=100, Y=50)` applied above a proxied video. The transform adjusts the operation's `Bounds` in authoring space; the `CorrectionScale` and raster are untouched. The compositor still blits the raster with the correct upscale at the translated bounds.
- **Hit testing across proxied sources**: `RenderNodeOperation.HitTest(Point)` operates in the operation's `Bounds` coordinate space (authoring), not the proxy raster space. Hit testing is unaffected by proxy.
- **Sub-pixel and zero parameters**: Filter parameters that divide by `CorrectionScale` may produce sub-pixel values on heavy proxy. Filters MUST handle sub-pixel sigma / radius / etc. gracefully (let Skia handle, or clamp to rasterizer minimum). Zero values pass through exactly.
- **Effects whose output resolution differs from input** (`Dilate` / `Erode` extend bounds; `StrokeEffect` extends): these effects must adjust their output bounds in authoring space using the original authored parameters, even if their internal Skia call uses scaled parameters. Otherwise the `Bounds` reported by the operation will be wrong for the compositor's blit.
- **Pen.Thickness across proxy boundaries**: A `Pen.Thickness = 4` applied to a stroke inside a proxied source's renderer draws at `4 / CorrectionScale` raster pixels. Skia's default behavior is to scale stroke widths with the canvas matrix, so if the source's renderer sets up `SKCanvas.Scale(1/CorrectionScale)` at the root, this is automatic.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `RenderNodeOperation` MUST carry a `CorrectionScale` value (`RenderScale` struct, `(ScaleX, ScaleY)` floats, defaulting to `Identity = (1.0, 1.0)`). The value represents the ratio by which the operation's *Render output (raster)* must be upscaled to align with the operation's *Bounds* (in the parent's authoring coordinate space).
- **FR-002**: Source-producing `RenderNode` subclasses (`VideoSourceRenderNode`, `ImageSourceRenderNode`, `SceneDrawable` / sub-scene render, and any future proxy-aware source) MUST set the `CorrectionScale` on their produced operations to reflect their actual rendering choice. Sources that do not proxy set it to `Identity`.
- **FR-003**: Transformer `RenderNode` subclasses (`FilterEffectRenderNode`, `TransformRenderNode`, container nodes, `PushClip` / `PushLayer` / `PushOpacityMask` derived nodes) MUST:
  - Read the `CorrectionScale` of their upstream operations.
  - Adjust their own internal parameters proportionally before applying to the raster (filter sigma / radius / position; transform translation if the transform operates in raster space; clip rect if it intersects a raster).
  - Propagate the appropriate `CorrectionScale` on their output operations (typically the same as upstream, unless the transformer materializes a new raster at a different resolution).
- **FR-004**: The top-level renderer / compositor (`Renderer` / `ImmediateCanvas` consumer that produces the final raster) MUST consume `CorrectionScale` on each operation by blitting the operation's raster with the inverse scale (i.e. upscaling) onto the final canvas at the operation's `Bounds`.
- **FR-005**: Existing project files MUST open unchanged. Every operation in the resulting render graph reports `CorrectionScale = Identity` until proxy is enabled (which is out of scope for this PR — see § "Future work"). No project-file migration runs.
- **FR-006**: The 13 in-scope built-in effects (per T001 audit — Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform × 3 subclasses, MosaicEffect, ShakeEffect, SplitEffect, Clipping) MUST automatically participate in per-clip proxy without source modification. The corresponding `*RenderNode` (typically `FilterEffectRenderNode` or a related node) handles the parameter adjustment.
- **FR-007**: Shapes (`RectShape`, `EllipseShape`, `RoundedRectShape`), `TextBlock`, vector geometry (`Geometry`), and brush internals (`TileBrush`, `ImageBrush`) MUST automatically participate in per-clip proxy without source modification. These are typically rendered fresh per frame into the surrounding canvas; when that canvas belongs to a proxied source's renderer, they draw at the source's resolution naturally.
- **FR-008**: Plugins / extensions written against the new `Beutl.Engine` MUST NOT have to change their authoring code to benefit. The authoring API surface (`IProperty<T>`, `FilterEffectContext` helpers, `GraphicsContext2D` direct draw, `Pen`, `Transform` subclass `CreateMatrix`, `Shape.Render`, etc.) is unchanged from pre-feature. Only RenderNode-author-level extensibility (subclassing `RenderNode` / `RenderNodeOperation`) has new responsibilities — and that is a much rarer extension point.
- **FR-009**: Sub-pixel and zero handling — when a filter parameter is divided by `CorrectionScale` and produces a sub-pixel positive value, the rasterizer (Skia) handles it; zero stays zero; `NaN` / negative-where-nonsensical is rejected with `ArgumentException` / `ArgumentOutOfRangeException`.
- **FR-010**: Nested scenes — a `Scene` inside another `Scene` is treated as a source. The inner scene's renderer decides its own proxy strategy and produces operations with its own `CorrectionScale`. The outer scene's compositor blits the inner scene like any other source.
- **FR-011**: Existing automated tests MUST continue to pass without modification. New tests MUST cover (a) `CorrectionScale` propagation through representative render-node graphs, (b) per-effect parameter adjustment math for the 13 in-scope effects, (c) SSIM equivalence between proxy and non-proxy renders, (d) backward compatibility for the pre-feature project corpus.

### Key Entities

- **`RenderNodeOperation`**: existing type; gains a `CorrectionScale` property. The semantic: "my `Bounds` are in parent authoring coordinates; my `Render(canvas)` produces a raster at `Bounds.Size / CorrectionScale`; consumer must upscale by `CorrectionScale` when blitting."
- **`RenderScale`**: new value type, `(ScaleX, ScaleY)` floats with validation. Same shape as proposed in earlier design drafts, but the location changes — it lives on `RenderNodeOperation` now, not on `GraphicsContext2D` / `Renderer`.
- **`ProxySettings`** (location TBD by `tasks.md`): per-source / per-clip proxy configuration declared by the user (e.g. "video clip X uses proxy 1/4"). Read by source RenderNodes when constructing their operations. The persistence schema and UI for these settings is **out of scope** for this PR; for now, sources default to no-proxy (`CorrectionScale = Identity`) and the mechanism is exercisable only via test-only construction.
- **Source RenderNodes**: `VideoSourceRenderNode`, `ImageSourceRenderNode`, the render node produced by `SceneDrawable` for a nested scene, and any future source. These declare their own `CorrectionScale`.
- **Transformer RenderNodes**: `FilterEffectRenderNode`, `TransformRenderNode`, container nodes, push-state nodes. These consume upstream `CorrectionScale` and respond.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every in-scope built-in effect, a render of a representative test scene with proxy enabled on a synthetic source (test-only `CorrectionScale = 4`) produces output visually equivalent (SSIM ≥ 0.97) to the same scene rendered with proxy disabled, after the compositor's upscale blit. Holds at multiple proxy ratios (e.g. 1/2, 1/4, 1/8).
- **SC-002**: For a curated corpus of pre-feature project files, rendering at export resolution on the new build produces SSIM ≥ 0.97 against baselines captured from the previous build. JSON serialization round-trip is byte-equal.
- **SC-003**: Mixed-resolution scene test — a scene containing one source at `CorrectionScale = 4` and another at `CorrectionScale = 1` renders correctly (each source contributes its own properly-scaled raster; filters between them respond to the correct upstream).
- **SC-004**: Extension-author migration verified — the 13 in-scope built-in effects pass all the new per-clip proxy tests without any `.cs` modification. Spot-check confirms that at least one custom `FilterEffect` (the existing `CSharpScriptEffect` example) also passes.
- **SC-005**: RenderNode-author migration verified — a documentation entry explains how `RenderNode` subclasses handle `CorrectionScale` (source vs transformer pattern); at least one example `RenderNode` in tests demonstrates the correct pattern.

## Assumptions

- The proxy mechanism is exercised mechanically (via test-only construction of `CorrectionScale ≠ Identity` on operations) in this PR. End-user-facing proxy toggle UX is a separate feature.
- Skia's default behavior for `SKCanvas` matrix-based transformation of stroke widths, image-filter sigmas, and text font sizes is correctly identified during implementation as the mechanism that makes within-a-source rendering automatically resolution-aware. If Skia behavior diverges per backend or version, this is caught in `Block F` testing and resolved at implementation time.
- The existing render-node graph architecture is suitable for carrying `CorrectionScale` without major refactor — the audit step (T001-equivalent) confirms which nodes are source vs transformer.
- Per-clip proxy settings persistence is out of scope here; a follow-up feature handles that and the UI to toggle proxy.
- The `Geometry` / `TextBlock.Size` / `Brush` rectangle surfaces previously listed as "deferred follow-ups" are no longer deferred — they participate automatically via the per-clip proxy mechanism (their containing source's renderer renders them at the source's resolution).
- **`Beutl.Graphics3D` (3D rendering pipeline) is out of scope for this PR**. The 3D pipeline has its own resolution / framebuffer story; per-clip proxy for 3D clips is a separate follow-up feature. A 2D filter applied to the output of a 3D render is in scope (the 3D output appears to the 2D pipeline as a source raster with its own `CorrectionScale`, just like a video or image source).
- **Audio rendering is out of scope for this PR**. Audio has no concept of resolution / raster scale; the per-clip proxy mechanism does not apply. Audio code paths in `Beutl.Engine/Audio/` are not touched.
- **`Beutl.NodeGraph` (node editor UI) is out of scope for this PR**. The node-graph editor visualises and manipulates the render graph but does not participate in rendering itself; no node-graph changes are required for per-clip proxy.

## Design history (chronological)

The eventual design (per-clip proxy + bottom-up `CorrectionScale` propagation) emerged after several drafts. Recording for future reference:

1. **Initial draft** (commit `63dd67191`) — typed wrappers `PixelLength` / `PixelExtent` / `PixelOffset` with per-effect property migration. Abandoned.
2. **Helper-internal scaling pivot** (commit `a0c20556e`) — scale inside `FilterEffectContext` helpers with `*Raw` opt-out, no wrapper types. Abandoned.
3. **Scope expansion** (commit `d9eeabaab`) — extend pattern to `GraphicsContext2D` / `Pen` / `Transform` / `Shape`. Built on the wrong premise (scene-wide proxy).
4. **Codex review #1 / Transform pivot** (commit `02f1d98bc`) — Transform moves to render-node application time. Still scene-wide.
5. **Codex review #2 fixes** (commit `47574a22a`) — minor doc inconsistencies fixed.
6. **Codex review #3 fixes** (commit `d4728ede9`) — Pen.Thickness in-scope reconciliation.
7. **This rewrite** (this commit) — user-clarified mental model: proxy is **per-clip, not scene-wide**. `CorrectionScale` lives on `RenderNodeOperation` and propagates bottom-up. All prior helper-/context-level scaling mechanisms are abandoned.

The earlier history is preserved in git but is **not authoritative for the design** — this spec supersedes everything in commits before the rewrite. `research.md` retains the historical analyses as an appendix.
