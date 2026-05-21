# Phase 0 Research: Per-Clip Proxy via RenderNodeOperation CorrectionScale

This document resolves the unknowns referenced by `plan.md` Phase 0.

> **Rewrite note**: This is a full rewrite (2026-05-22). Earlier R1–R10 sections explored designs that treated proxy as a scene-wide uniform scaling. The user clarified during design review that the actual mental model is **per-clip proxy with bottom-up scale propagation**, which renders all of R1–R10 moot. They are preserved in § "Appendix: Historical pivots" at the end as a record of how the design evolved.

## R1 — Where does `CorrectionScale` live? (the data flow)

**Question**: How is per-clip resolution information represented and threaded through the render-node graph?

**Decision**: Add a `CorrectionScale: RenderScale` virtual property on the existing `RenderNodeOperation` (`src/Beutl.Engine/Graphics/Rendering/RenderNodeOperation.cs`). Default = `RenderScale.Identity`. Source nodes set it; transformer nodes read upstream's and propagate; compositor consumes it during the final blit.

**Rationale**:

- `RenderNodeOperation` is **already the boundary object** between RenderNodes in the existing graph. It already carries `Bounds` (where the content goes in parent coordinates) and `Render(canvas)` (how to draw the content). `CorrectionScale` completes the picture: at what scale ratio was the raster produced relative to the bounds.
- Bottom-up propagation matches the natural data flow: a source declares its choice, transformers above it inherit. No global state needed.
- Decentralised: each RenderNode subclass decides its scale locally based on its own configuration. No central "scene RenderScale" that must be consulted from every helper.
- Symmetric with existing `Bounds`: just as `Bounds` is a property of each operation describing where it lives, `CorrectionScale` describes how it relates raster-to-authoring.

**Alternatives considered**:

- *Global `Renderer.RenderScale` set at construction.* This was the pivot 2 design. **Rejected** — fundamentally wrong for per-clip proxy. It treats the whole scene as one resolution.
- *Per-`GraphicsContext2D` push/pop stack.* Pivot 3 design. **Rejected** — same problem; can't represent "this source is proxied, that source isn't" in a top-down propagation.
- *Carry `CorrectionScale` separately as a side-table keyed by RenderNode identity.* Considered but rejected — `RenderNodeOperation` is already the right level of granularity; co-locating the data avoids a parallel lookup.

## R2 — Which RenderNodes are "sources" vs "transformers"?

**Question**: Each RenderNode must classify itself: does it declare its own scale (source), or does it inherit from upstream and respond (transformer)?

**Decision**: Classification by inspection of each existing `RenderNode` subclass under `src/Beutl.Engine/Graphics/Rendering/`. The implementing audit task (T-audit in `tasks.md`) finalizes the list.

**Initial classification** (audit-confirmed during implementation):

| Node | Category | Scale source / behavior |
|---|---|---|
| `VideoSourceRenderNode` | Source | Reads its `VideoSource.Resource` and any per-clip proxy settings; declares `CorrectionScale` accordingly. |
| `ImageSourceRenderNode` | Source | Reads its `ImageSource.Resource` and any per-clip proxy settings; declares `CorrectionScale` accordingly. (Static images rarely benefit from proxy; default to Identity.) |
| `DrawableRenderNode` (Scene-as-drawable, via `SceneDrawable`) | Source-like | Inner scene's renderer decides its own proxy. The operation produced has the inner scene's `CorrectionScale`. |
| `FilterEffectRenderNode` | Transformer | Reads upstream `CorrectionScale`; divides filter parameters (Blur sigma, DropShadow position/sigma, Erode/Dilate radius, etc.) by upstream `CorrectionScale` before invoking Skia. Output has the same `CorrectionScale` as input. |
| `TransformRenderNode` | Transformer | Reads upstream `CorrectionScale`. Transform's translation column is in authoring space; `Bounds` updates by the translation; `CorrectionScale` propagates unchanged (the transformer does not re-rasterize). |
| `ContainerRenderNode` | Transformer | Wraps children; aggregates their operations; in the common case, propagates the children's `CorrectionScale` upward. When child operations differ in `CorrectionScale`, the container must decide a policy — typically each child's operation is forwarded independently. |
| `ClipRenderNode` / `LayerRenderNode` / `OpacityMaskRenderNode` (push-state derivatives) | Transformer | Clip rect / layer bounds are in authoring space; intersect with upstream `Bounds`. `CorrectionScale` propagates. If the node materializes a new raster (e.g. `PushLayer` with a saveLayer), it can choose its own scale, in which case it is source-like for downstream. |
| Direct-draw operations (`RectShape.Render` → `DrawRectangle`, `EllipseShape.Render` → `DrawEllipse`, etc.) | Source-like | These render into the surrounding `ImmediateCanvas` at the canvas's scale; their operations report `Bounds` in authoring space and `CorrectionScale` matching the surrounding canvas. |

**Audit task** (`tasks.md` T001-equivalent): walk `src/Beutl.Engine/Graphics/Rendering/` and confirm the per-subclass classification, file follow-ups for any unclassified case.

## R3 — Filter parameter adjustment math

**Question**: When `FilterEffectRenderNode` receives an upstream operation with `CorrectionScale = s` and the filter has authored parameter `p`, what does Skia receive?

**Decision**: For length-typed filter parameters, Skia receives `p / s.ApplyUniform(1)` (for scalars like sigma, radius) or `(p.X / s.ScaleX, p.Y / s.ScaleY)` (for 2D position / extent).

**Rationale**:

- Skia filter parameters are interpreted in the **current raster's pixel units**. If the raster is at `1/s` of authoring resolution, then 1 raster pixel = `s` authoring pixels.
- A filter authored as "Blur(sigma=20)" expresses "blur with 20-authoring-pixel sigma". To achieve this on a raster that is `1/s` smaller, the actual Skia sigma must be `20 / s` raster pixels.
- This is the math the user described in the design clarification: "FilterEffectRenderNode などの中間ノードではパラメータが 1/4 にされます" — divide by upstream `CorrectionScale`.

**Per-effect map** (the 13 in-scope effects per the original T001 audit; same effects, different mechanism):

| Effect | Parameter(s) | Math at FilterEffectRenderNode |
|---|---|---|
| `Blur` | `Sigma: Size` | `Skia.Blur(sigma.Width / s.ScaleX, sigma.Height / s.ScaleY)` |
| `DropShadow` | `Position: Point`, `Sigma: Size` | `Skia.DropShadow(position.X / s.ScaleX, position.Y / s.ScaleY, sigma.Width / s.ScaleX, sigma.Height / s.ScaleY, …)` |
| `InnerShadow` | `Position`, `Sigma` | same as DropShadow |
| `StrokeEffect` | `Offset: Point`, Pen (`Thickness`, `DashOffset`, `Offset`) | `Skia` stroke geometry at `Offset/s`, `Thickness/s`, etc. |
| `Erode`, `Dilate` | `RadiusX`, `RadiusY: float` | `Skia.Erode/Dilate(rx/s.ScaleX, ry/s.ScaleY)` |
| `FlatShadow` | `Length: float` (Angle dimensionless) | `length / s.ApplyUniform(1)` |
| `ColorShift` | per-channel `PixelPoint` offsets | `(rx/s.ScaleX, ry/s.ScaleY)` per channel |
| `DisplacementMapTransform` (×3 subclasses) | `X / Y / CenterX / CenterY / Depth: float` | each scalar divided by the appropriate axis |
| `MosaicEffect` | `TileSize: Size` | `(w/s.ScaleX, h/s.ScaleY)` |
| `ShakeEffect` | `StrengthX / StrengthY: float` | divided per axis |
| `SplitEffect` | `HorizontalSpacing / VerticalSpacing: float` | divided per axis |
| `Clipping` | `Left / Top / Right / Bottom: float` | each edge divided |

Dimensionless parameters (color, opacity, angle, percentage, count) pass through unchanged.

## R4 — Effects whose bounds change with parameters

**Question**: Some effects (Erode/Dilate, DropShadow, FlatShadow, StrokeEffect, Clipping) extend or shrink the bounds of their input. The output bounds in authoring space must be correct for the compositor to position the raster, but the internal Skia call uses scaled parameters. How are these reconciled?

**Decision**: Each `FilterEffectRenderNode` computes output `Bounds` in **authoring space** using the **authored** (un-divided) parameters, regardless of the upstream `CorrectionScale`. The internal Skia call uses divided parameters and operates on the (smaller) raster. The compositor then upscales the result by `CorrectionScale` to place it in the bounds.

**Example**: `DropShadow(Position = (10, 10), Sigma = (15, 15))` on input with `CorrectionScale = 4`, input bounds = `(0, 0, 1920, 1080)`, input raster = `(480, 270)`:

- Output bounds = `(-15, -15, 1920 + 30, 1080 + 30)` = `(-15, -15, 1950, 1110)` — extended by the authored sigma in authoring space.
- Internal Skia call: blur sigma = `(15/4, 15/4) = (3.75, 3.75)`, position offset = `(10/4, 10/4) = (2.5, 2.5)` — divided by upstream CorrectionScale.
- Output raster = `(488, 278)` (raster grew by 8 pixels to cover the blur halo at proxy resolution).
- Compositor blits this `(488 × 278)` raster onto bounds `(-15, -15, 1950, 1110)` with the 4× upscale.

The bounds-in-authoring-space rule keeps the compositor honest: it always works in the parent's authoring coordinate space.

## R5 — SKCanvas matrix at the source level

**Question**: When a source's renderer constructs an `ImmediateCanvas` for a proxied source (e.g. video at 1/4), does the SKCanvas get a Scale matrix applied at construction, or does the source render at literal proxy coordinates?

**Decision**: For sources that "draw into" a raster (anything that emits per-frame draw calls — Shape, TextBlock, vector content, nested Scene), the source's renderer constructs `ImmediateCanvas` with **`SKCanvas.Scale(1/CorrectionScale)` applied at construction**. Everything inside renders in **authoring space** and Skia transforms to raster space automatically.

For sources that present an already-rasterized image (`VideoSourceRenderNode` reading from a decoded frame, `ImageSourceRenderNode` reading from a bitmap), there is no SKCanvas matrix involved — the image is decoded at the chosen resolution and shipped as the operation's raster.

**Rationale**: Within a single source's render pass, Skia's matrix-based transformation handles all length-typed values automatically (Rect, stroke width, font size, geometry path coords, image-filter sigma, brush internal matrices). This is the "single-point at SKCanvas matrix root" mechanism that the user discussed during the design review — **but applied per source, not globally**. The result: zero per-helper plumbing within a source's render; the cost is paid once at the source's renderer construction.

**Composes correctly with FilterEffectRenderNode**: filters operate on the produced raster *outside* the source's renderer (they're transformer nodes in the parent graph). Their parameters must be divided by the source's CorrectionScale (per R3) because at filter-application time the SKCanvas is no longer the source's scaled canvas.

## R6 — Compositor / final blit

**Question**: How does the top-level `Renderer` (or whatever produces the final raster) consume `CorrectionScale` from each operation?

**Decision**: The compositor iterates the final operations and, for each, calls `ImmediateCanvas.DrawRenderTarget` (or equivalent) with a transform matrix that scales by the operation's `CorrectionScale` at the operation's `Bounds.TopLeft`. The existing `ImmediateCanvas.DrawSurface / DrawRenderTarget` paths are extended to accept this scale (or wrapped by a helper that pushes the appropriate transform).

**Rationale**: The compositor is the only place where "the operation has a small raster but its bounds say it should look bigger" gets resolved. Doing it here keeps every other node in the graph honest about its bounds-in-authoring-space contract.

## R7 — Backward compatibility (existing projects without proxy)

**Question**: How are pre-feature projects unaffected?

**Decision**: All existing `RenderNode` subclasses default to producing operations with `CorrectionScale = Identity` until per-clip proxy settings are wired in. With `CorrectionScale = Identity` throughout the graph:

- Source nodes produce rasters at authoring resolution (current behavior).
- Transformer nodes' parameter-division-by-CorrectionScale is `param / Identity = param` (no-op).
- Compositor's upscale blit is `scale by Identity` (no-op).
- Final render is SSIM-equivalent (likely byte-equivalent, modulo new NaN/negative guards) to pre-feature.

This satisfies FR-005 / SC-002 mechanically — the new code path is a no-op when no proxy is enabled.

## R8 — Per-clip proxy settings UX and persistence (out of scope here)

**Question**: How does the user toggle proxy on a clip? Where are settings persisted?

**Decision**: **Out of scope** for this PR. The engine-side mechanism (operations carrying `CorrectionScale`, sources reading their settings) is what this PR delivers. The settings themselves are wired by a follow-up feature.

For testing purposes during this PR, the mechanism is exercised by:

- A test harness that constructs `RenderNodeOperation` instances with `CorrectionScale ≠ Identity` directly.
- A test-only source node subclass that lets tests inject a specific `CorrectionScale`.

The follow-up feature is responsible for project-file schema changes, UI toggles, automatic proxy generation (offline pre-render of proxy versions of heavy media), and any orchestration of when proxy is enabled (e.g. only during preview, never during export).

## R9 — Test strategy for SSIM equivalence

**Question**: How do tests verify "proxy frame matches export frame"?

**Decision**: Same SSIM-based approach as proposed in earlier drafts, but the test setup is different. The test constructs a scene with a known proxy-enabled source (using the test harness from R8), renders it once at proxy (operation CorrectionScale ≠ Identity), renders it once at full (operation CorrectionScale = Identity), and compares the final compositor output of each. Both go through the compositor's blit path; SSIM must be ≥ 0.97 after bicubic upscaling of the smaller raster if comparing pre-blit, or directly if both produce the same-sized final raster.

The SSIM helper (`SsimHelper.cs`) and bicubic upscaler (`BicubicResampler.cs`) from prior drafts are still useful and carry over.

## Appendix: Historical pivots

The design went through several incorrect or partially-correct iterations before reaching the current per-clip proxy model. Recorded for institutional memory:

- **Pivot 1 — Typed wrappers** (`PixelLength` / `PixelExtent` / `PixelOffset`): would have required per-effect property migration and three new animators / property editors. Abandoned for being too invasive.
- **Pivot 2 — Helper-internal scaling on `FilterEffectContext`**: scale at API time inside `Blur(Size)`, `DropShadow(...)`, etc. Built on the assumption that there's a single scene-wide RenderScale.
- **Pivot 3 — Scope expansion to `GraphicsContext2D` / `Pen` / `Transform`**: same scene-wide RenderScale assumption, extended to more API surfaces. Resulted in 3 scaling sites + 6 new contract documents.
- **Pivot 4 — Transform moved to render-node application time**: still scene-wide. Caught and addressed several Codex design-review findings (CompositionContext propagation, plugin migration consistency, etc.).
- **Pivot 5 — Pen.Thickness in-scope reconciliation**: minor doc fixes after Codex review.
- **Pivot 6 (this rewrite) — Per-clip proxy via `RenderNodeOperation.CorrectionScale`**: discovered during design review when the user clarified the actual workflow ("重い4K動画は1/4スケール、テロップは等倍"). All prior helper-/context-level scaling mechanisms abandoned; the entire spec is rewritten around bottom-up propagation through the render-node graph.

The lesson for future Spec-Kit work: **clarify the user's actual workflow / mental model before designing the mechanism**. Three rounds of Codex review on a fundamentally-wrong premise caught implementation issues but not the strategic mistake; only direct user clarification did.
