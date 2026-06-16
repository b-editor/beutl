# Resolution-Independent 2D Pipeline — Rendering Analysis Dossier

> ⚠️ **STALE — scaffold-era research, superseded by the spec/contracts (banner added 2026-06-15, S7).** This
> dossier (incl. its §12 addendum) predates the June 2026 design reversals and was not updated for them. Positions
> the shipped design has since **repudiated**: the `ResolutionPolicy` enum (removed — supply-driven only); a
> universal byte-identity-at-`s_out=1` guarantee (abolished — content-scoped, FR-019); an **atomic
> render-dispatcher** renderer/cache swap (it is two independent UI-thread swaps with a self-healing tear window —
> FR-031); a GLSL **`uScale`** push constant (not shipped — GLSL derives `w` from device-px `Width`/`Height` —
> shader-uniforms.md); **floor**-origin rounding (it is toward-zero `(int)` truncation — FR-007); and
> `PerlinNoiseBrush.BaseFrequency` **÷ scale** (left unchanged — FR-010). **For anything that conflicts, the
> authoritative sources are `spec.md`, `contracts/*.md`, and `data-model.md`, not this dossier (and not its "§12
> wins" rule below, which only orders the dossier against itself).** Treat it as historical *where-in-the-code*
> context only.
>
> Feature 003. Research dossier feeding the formal spec. **Not an implementation plan.**
>
> Scope: Beutl's 2D rendering pipeline (`Beutl.Engine` graphics, `Beutl.ProjectSystem` scene/export, `Beutl` editor view-models). Synthesizes per-subsystem surveys, a per-effect scale matrix, and design-question analyses. All file paths are repo-relative.
>
> **The single load-bearing fact:** there is no scale concept anywhere in the 2D pipeline today. `1 logical unit == 1 device pixel` is an *implicit invariant*, materialized as literal `ToSize(1)` / `(int)bounds.Width` / `PixelRect.FromRect(bounds)` calls scattered across the render path. The feature is "stop assuming `scale == 1`; thread the real scale; multiply by it exactly once at the leaves that touch device pixels."
>
> **Post-review note (Codex code-verification pass):** an addendum (**§12**) records factual corrections and newly-found coupling sites — particles, audio visualizers, the concrete 3D bridge, render-dispatcher concurrency, `Resource.Version` / source-generator invalidation, and shader-uniform naming. **Where §12 conflicts with the body, §12 wins.**

---

## 1. Current architecture summary — how logical maps to pixels today

### 1.1 The render-node tree

The renderer builds a per-frame, structurally-immutable graph of `RenderNode`s (`src/Beutl.Engine/Graphics/Rendering/RenderNode.cs`). `Drawable.Render(ctx, resource)` walks the scene, and the retained-mode recorder `GraphicsContext2D` (`src/Beutl.Engine/Graphics/Rendering/GraphicsContext2D.cs`) diffs and constructs the tree. `RenderNodeProcessor.Pull(node)` (`RenderNodeProcessor.cs`) recursively pulls each node's children into a flat `RenderNodeOperation[]`, wraps them in a `RenderNodeContext`, and calls `node.Process(context)`. Each `Process` returns `RenderNodeOperation[]` — leaf draw ops (rectangle/text/image/video/geometry) and decorators (transform/clip/opacity/blend/filter/layer).

The crucial structural fact: **every coordinate in the entire tree is implicitly `1 unit == 1 device pixel`.** This includes `RenderNodeOperation.Bounds` (a float `Rect`), the matrices in `TransformRenderNode`, the rects in `RectClipRenderNode`/`RectangleRenderNode`, all `Drawable` geometry, and every FilterEffect parameter.

### 1.2 Where pixels actually get fixed (the three sinks)

The logical→pixel mapping happens only at three sinks, all of which truncate `Bounds` to a pixel surface via `PixelRect.FromRect(op.Bounds)` + `RenderTarget.Create(width, height)`:

1. **Top-level `Renderer`** (`src/Beutl.Engine/Graphics/Rendering/Renderer.cs`): ctor takes `(int width, int height)` → `FrameSize = new PixelSize(width, height)` (Renderer.cs:45-57) and allocates a single `ImmediateCanvas` over a surface sized exactly to the requested width/height. `SceneRenderer` (`src/Beutl.ProjectSystem/SceneRenderer.cs:10-11`) feeds `scene.FrameSize.Width/Height` straight in. The `ImmediateCanvas` (`src/Beutl.Engine/Graphics/ImmediateCanvas.cs`) initializes `_currentTransform` from `Canvas.TotalMatrix` (identity), so logical `(x,y)` lands on device pixel `(x,y)`.
2. **`RenderNodeProcessor.RasterizeToRenderTargets` / `Rasterize` / `RasterizeAndConcat`** (RenderNodeProcessor.cs:20-90): each does `PixelRect.FromRect(op.Bounds)` then `RenderTarget.Create(rect.Width, rect.Height)` + `Matrix.CreateTranslation(-op.Bounds.X, -op.Bounds.Y)`. Cache and thumbnail/export rasterization run here.
3. **`FilterEffectActivator.Flush`** (`src/Beutl.Engine/Graphics/FilterEffects/FilterEffectActivator.cs:29`) and `CustomFilterEffectContext.CreateTarget` (`CustomFilterEffectContext.cs:52`): intermediate effect surfaces via `RenderTarget.Create((int)bounds.Width, (int)bounds.Height)`, drawn with a translate-only transform.

### 1.3 The keystone identity: `ToSize(1)`

`Drawable.Render` (`src/Beutl.Engine/Graphics/Drawable.cs:77`) does `Size availableSize = context.Size.ToSize(1);`. `PixelSize.ToSize(float scale)` (`src/Beutl.Engine/Media/PixelSize.cs:187`) is literally `new Size(Width/scale, Height/scale)`, so `scale == 1` hard-wires device==logical. The same `ToSize(1)` appears in `Shape.Render`, `DrawableGroup.Render`, `DrawableDecorator.Render`, `SourceImage.MeasureCore` (`SourceImage.cs:27`), and `SourceVideo.MeasureCore` (`SourceVideo.cs:139`).

**Important latent infrastructure:** scale-aware primitives already exist but are unused on the render path: `PixelSize.ToSize(float scale)` / `ToSize(Vector)` (PixelSize.cs:187-201), `PixelSize.FromSize(Size, float/Vector)`, `Size.ToSize(...)`, and `PixelRect.FromRect(Rect, float/Vector scale)` (PixelRect.cs:391-404). The codebase appears pre-shaped for this feature; the change is largely "stop passing `1`; thread the real scale through these existing seams."

### 1.4 Key types (the spine the scale must travel through)

| Type | File | Role |
|---|---|---|
| `RenderNode` | `Rendering/RenderNode.cs` | Abstract base; `abstract RenderNodeOperation[] Process(RenderNodeContext)` is the extension point a scale flows through. No resolution notion today. |
| `RenderNodeOperation` | `Rendering/RenderNodeOperation.cs` | Flattened draw/decorator unit: `Rect Bounds` + `Render(ImmediateCanvas)` + `HitTest(Point)`. Bounds are device px. The maintainer's per-op Scale lives here. |
| `RenderNodeContext` | `Rendering/RenderNodeContext.cs` | Per-`Process` context: `Input[]` + `IsRenderCacheEnabled` + `CalculateBounds()` (Union of inputs). **No Scale field** — the cleanest top-down insertion point. |
| `RenderNodeProcessor` | `Rendering/RenderNodeProcessor.cs` | Drives `Pull`/`PullToRoot`; the sole compositor; owns all three rasterization sinks. The "final stage" (design item 3) belongs here / at the Renderer boundary. |
| `ContainerRenderNode` | `Rendering/ContainerRenderNode.cs` | Base decorator; default `Process` returns `context.Input` unchanged. Scale must propagate through every subclass. |
| `GraphicsContext2D` | `Rendering/GraphicsContext2D.cs` | Recorder. Constructed with `PixelSize canvasSize`; `DrawBackdrop` bakes `new Rect(canvasSize.ToSize(1))` at record time. |
| `TransformRenderNode` | `Rendering/TransformRenderNode.cs` | Holds `Matrix Transform`; `Process` does `r.Bounds.TransformToAABB(Transform)` + `canvas.PushTransform(Transform)`; hit-test inverts the matrix. |
| `FilterEffectRenderNode` | `Rendering/FilterEffectRenderNode.cs` | Bridge to the FilterEffect pipeline. Builds `FilterEffectContext(context.CalculateBounds())`; emits ops translated by `t.Bounds.X - t.OriginalBounds.X`. |
| `FilterEffectContext` | `FilterEffects/FilterEffectContext.cs` | Builder every effect talks to. Holds `Bounds`/`OriginalBounds`; exposes Blur/DropShadow/Erode/Dilate/Transform/MatrixConvolution. Densest pixel-coupling site. |
| `FilterEffectActivator` | `FilterEffects/FilterEffectActivator.cs` | `Flush()` rasterizes each `EffectTarget` at `(int)OriginalBounds`. The canonical render-target-mode coupling point. |
| `RenderNodeCache` / `RenderNodeCacheHelper` | `Rendering/Cache/*.cs` | Caches rasterized `(RenderTarget, Rect)` keyed by pixel bounds + `RenderCacheRules.Match(pixels)`. Scale-blind. |
| `ImmediateCanvas` | `Graphics/ImmediateCanvas.cs` | Maps logical coords to the SKSurface. `Size` is device px; `DrawSurface`/`DrawRenderTarget` blit at a `Point` (the only resampling hook). |
| `Renderer` | `Rendering/Renderer.cs` | Top-level entry; binds surface size 1:1 to `FrameSize`; `Snapshot()` reads back at native size. The natural home for a root scale + final normalization, neither of which exists. |

### 1.5 Output path (preview and export)

Both preview and export render at full `FrameSize` and call `Snapshot()`:
- **Preview**: `EditViewModel` (`src/Beutl/ViewModels/EditViewModel.cs:51`) rebuilds `SceneRenderer(Scene)` on `FrameSizeProperty` change; `PlayerViewModel.RenderOnRenderThread` (PlayerViewModel.cs:1299-1301) renders + snapshots. Editor display zoom is applied to the already-rendered bitmap at the UI layer and is orthogonal to render scale.
- **Export**: `OutputViewModel.StartEncode` (`src/Beutl/ViewModels/Tools/OutputViewModel.cs:73-74,241,264`) sets `VideoEncoderSettings.SourceSize == DestinationSize == Model.FrameSize`, builds `SceneRenderer(disableResourceShare: true)`, drives `FrameProviderImpl` (`src/Beutl/Models/FrameProviderImpl.cs:47-52`) which renders + snapshots each frame. The encoder already supports a Source→Destination rescale (FFmpeg: `FFmpegEncodingController.cs:177-178,240-241`), but the pipeline never feeds a Source different from Destination.

The existing `FrameCacheConfigScale` (Original/FitToPreviewer/Half/Quarter, `src/Beutl.Configuration/EditorConfig.cs:17-26`) only downsizes *already-rendered full-res cache bitmaps* for memory (`FrameCacheManager.cs:264`). **It is NOT a render scale** and must be kept distinct.

### 1.6 `RenderTarget` — the pixel allocation primitive

`src/Beutl.Engine/Graphics/Rendering/RenderTarget.cs` wraps an `SKSurface`/`ITexture2D` sized in device pixels (`SKImageInfo(width, height, RgbaF16, ...)`). `Create(int width, int height)` and `Snapshot()`/`CreateNull` use integer pixel dims exclusively, no DPI/scale/logical concept. Every main frame, effect intermediate, and cache tile shares this one primitive.

---

## 2. Pixel-coupling inventory (ranked)

Severity: **HIGH** = directly fixes resolution / silently produces wrong output at scale≠1; **MED** = secondary coupling that breaks if HIGH items are partially applied; **LOW** = consistency/quality detail.

| # | Location (file:line) | What couples | Severity | What must change |
|---|---|---|---|---|
| 1 | `RenderNodeProcessor.RasterizeToRenderTargets/Rasterize/RasterizeAndConcat` (RenderNodeProcessor.cs:20-90) | `PixelRect.FromRect(op.Bounds)` + `RenderTarget.Create(rect.W,rect.H)` + translate-only matrix; logical bounds taken as pixel dims, integer-truncated | HIGH | Size surfaces to `Bounds*scale`, pre-scale the canvas by `scale`, round (not truncate). Switch to the existing `PixelRect.FromRect(rect, scale)` overload. |
| 2 | `RenderNodeContext` (whole type) | No Scale field; every `Process` sees this object | HIGH | Add `Scale` (Vector, default (1,1)); seed from Renderer; propagate in `Pull` like `IsRenderCacheEnabled`. |
| 3 | `RenderNodeOperation.Bounds` + `CreateFromRenderTarget`/`CreateFromSurface` (RenderNodeOperation.cs) | Bounds is the universal currency (composite/hit-test/cache/raster) and is device px everywhere; ops carry no scale | HIGH | Add a read-only `EffectiveScale` the op was rasterized at; keep Bounds logical; blit src-scale-aware. |
| 4 | `Renderer` ctor + `Render`/`UpdateFrame` initial Push (Renderer.cs:45-57,129-141,169,263-267) | Surface bound 1:1 to FrameSize; root Push at identity; no `CreateScale(scale)`; `GraphicsContext2D(node, FrameSize)` | HIGH | Split logical frame size from device surface size = `ceil(frame*scale)`; inject root scale transform; thread scale into `GraphicsContext2D`. |
| 5 | `FilterEffectActivator.Flush` (FilterEffectActivator.cs:29-36) | `RenderTarget.Create((int)OriginalBounds.W, (int)H)` + translate by `-OriginalBounds`; 1unit==1px, integer-truncated | HIGH | Allocate `ceil(bounds*scale)`; push `CreateScale(scale)`; round to avoid sub-pixel drift across effect chains. |
| 6 | `CustomFilterEffectContext.CreateTarget` (CustomFilterEffectContext.cs:52) + `Open` | `RenderTarget.Create((int)bounds.W,(int)H)`; `Open` returns an unscaled canvas. Every CustomEffect (Mosaic/SKSL/Displacement/InnerShadow/Blend-with-brush) draws into 1:1 | HIGH | Size by scale; pre-scale the canvas; expose device pixel size to shaders. |
| 7 | `FilterEffectContext` pixel primitives (Blur 119-137, DropShadow 97-117, InnerShadow 140-184, Dilate/Erode 229-243, MatrixConvolution 195-227, Transform 186-193) | sigma/position/radius/kernelOffset are device px passed verbatim to Skia; `transformBounds` (e.g. `Inflate(sigma*3)`) uses the same units | HIGH | Multiply pixel-magnitude args by scale centrally in these methods; leave color/blend/opacity/angle invariant; ensure bounds math uses the scaled value. |
| 8 | `FilterEffectRenderNode.Process` (FilterEffectRenderNode.cs:30,52-54) | Seeds `new FilterEffectContext(context.CalculateBounds())` (device px) and re-registers filtered output with translate-only matrix; reads no scale | HIGH | Pass per-node scale into the FilterEffectContext; scale the re-registration translation; survive integer truncation. |
| 9 | `GraphicsContext2D.DrawBackdrop` (GraphicsContext2D.cs:339-356,345) + `SnapshotBackdropRenderNode` | `new Rect(canvasSize.ToSize(1))` bakes full pixel canvas at record time; snapshot captures the whole device surface | HIGH | Derive backdrop bounds from `canvasSize.ToSize(scale)` (overload exists); tag snapshot with its capture scale; resample/re-snapshot on mismatch. |
| 10 | `RenderNodeCache.StoreCache/UseCache` + `RenderNodeCacheHelper.CreateDefaultCache` (RenderNodeCache.cs:96-121, RenderNodeCacheHelper.cs:90-115) | Cache entries `(RenderTarget, Rect)` sized in px from Bounds, gated by `RenderCacheRules.Match(pixels)`; **no scale in the key** | HIGH | Make scale part of cache identity; invalidate on scale change (or store scale + resample on replay); re-express `Match` thresholds in logical px (÷ scale²). |
| 11 | `ImmediateCanvas.DrawSurface/DrawRenderTarget` (ImmediateCanvas.cs:106-126) | `Canvas.DrawSurface(surface, pt.X, pt.Y)` — pure 1:1 blit, no scale matrix, no resampler; only resamples if the active CTM happens to scale | HIGH | Resample when source scale ≠ dest scale (scale matrix + `SKSamplingOptions`); reuse Mitchell (already in `DrawBitmap`, line 166). This is the literal mixed-scale convergence point. |
| 12 | `SourceImage.MeasureCore` (SourceImage.cs:27), `SourceVideo.MeasureCore` (SourceVideo.cs:139), `ImageSourceRenderNode.cs:12/25`, `VideoSourceRenderNode.cs:18/31` | `Source.FrameSize.ToSize(1)` → drawable logical size == decoded pixel count; node Bounds bake native px | HIGH | Decouple a source's logical size from its decoded bitmap pixel size so proxy↔full produce identical Bounds. |
| 13 | `ImmediateCanvas.DrawBitmap/DrawImageSource/DrawVideoSource` (ImmediateCanvas.cs:153-194) | Blits at native `bmp.Width/Height` at `(0,0)`, no dest rect | HIGH | Draw into a dest rect = `logicalSize*scale` so proxy/full resample to the same region. This is where proxy media is reconciled visually. |
| 14 | `SceneDrawable.SceneBitmapRenderNode.Process` (SceneDrawable.cs:178-194) | Nested scene spins up its **own** `Renderer` at the inner scene's `FrameSize`; `DrawRenderTarget(...)` at default position | HIGH | Inner renderer must inherit the outer scale (render at `innerFrame*scale`) or blit via a scale-aware dest rect. Concrete mixed-scale origin. |
| 15 | `MediaOptions` + `IDecoderInfo.Open` + `MediaReader.Open` + `ImageSource/VideoSource.Resource.Update` (MediaOptions.cs; ImageSource.cs:74-93; VideoSource.cs:105-125) | No decode-target-size channel; `MediaOptions` carries only `StreamsToLoad`; always native decode | HIGH (for proxy) | Add optional target-size/scale hint (additive, default native); honor in FFmpeg IPC + worker. Keystone for true proxy decode. |
| 16 | `EngineObject.Resource.Version` + `Capture()/Compare()` (EngineObject.cs:384, ResourceExtension.cs) | Render-graph invalidation keys on `Version` + reference; scale is not a property and does not bump Version | HIGH | Fold render scale (and proxy identity) into cache/invalidation key so scale switches force re-measure/re-decode. |
| 17 | `MosaicEffect` SKSL (MosaicEffect.cs:61-83) + `SKSLShader.ApplyToNewTarget` (71-92) | `tileSize` is device px; `origin.ToPixels(image.Width,Height)`; `DrawRect(0,0,bounds.W,bounds.H)` assumes bounds==px | HIGH | Scale `tileSize` by s; leave relative `origin` untouched (auto-scales via image dims). Per-effect classification problem. |
| 18 | `ColorShift` (ColorShift.cs:30-120) | `Red/Green/Blue/AlphaOffset` are `PixelPoint` (int device px) shader uniforms; `TransformBoundsCore` uses `ToPoint(1)` (hard scale=1) | HIGH | Multiply offsets by s (and `minOffset`); `TransformBounds` must use the active scale; consider widening `PixelPoint`→float `Point` for sub-pixel scaled offsets. |
| 19 | Script effect uniforms — `SKSLScriptEffect.cs:99-104`, `GLSLScriptEffect.cs:91-98` | `width/height/iResolution` set from `Bounds.W/H`; `fragCoord` in device px (SkSL) / 0..1 UV (GLSL); user pixel literals can't be auto-scaled | HIGH | Keep width/height/iResolution = device size of the scaled target; add an explicit `scale` uniform; document the contract; never repurpose width/height to logical. |
| 20 | `Renderer.HitTest` (Renderer.cs:274-301) + editor mapping `PlayerView.axaml.MouseControl.OnPressedHitTest` (338-364) and 3D path (line 1223) | Pointer mapped to `FrameSize` px and passed straight into `op.HitTest`; per-op Bounds in px | HIGH | Define a canonical hit-test space (logical) end-to-end; convert the pointer and every `op.Bounds`/`HitTest` into it; keep render scale out of handle math. |
| 21 | `TransformRenderNode` matrix translation (TransformRenderNode.cs:32-35) | Translation parts are logical-px; parallel explicit `TransformToAABB` Bounds math must stay consistent with the canvas matrix | MED | A root pre-scale handles translation automatically; keep explicit Bounds math numerically consistent for hit-test/cache. |
| 22 | `PenHelper.ConfigureStrokePaint`/`GetBounds`/`CreateStrokePath` (PenHelper.cs:55-102,10-53,132-253) | `StrokeWidth = thickness` (px); dash entries `* thickness`; offset paths in px; `Geometry.GetCachedStrokePath` keyed on (pen,version) **not scale** | MED→HIGH | Multiply thickness/offset/dash by s (or generate stroke logical + rely on canvas matrix); add scale to the stroke-path cache key. |
| 23 | `Rotation3DTransform` perspective (Rotation3DTransform.cs:55-95) + `Matrix.Transform` perspective branch (Matrix.cs:304) | `Depth`/`CenterX/Y/Z` are logical-px; perspective `M34=-1/depth`; uniform scale does **not** commute with the projective part (S·P ≠ P·S) | MED | Apply scale outside the projective term or scale Center/Depth proportionally; perspective children are not bit-identical across mixed scales. |
| 24 | `Matrix.TryDecomposeTransform`/`ComposeTransform` (Matrix.cs:524,583) + `CanonicalTransformLayout` | Decompose/compose assume a flat unitless space; folding render-scale into the artistic Matrix surfaces it as a user `ScaleTransform` | MED | Keep render-scale structurally separate from `Transform.CreateMatrix`/`TransformGroup`; never fold into the artistic matrix. |
| 25 | `DrawableGroup`/`DrawableDecorator` `CustomTransformRenderNode.ScreenSize` + `CalculateTranslate` (DrawableGroup.cs:30,227-265) | Alignment/centering against `ScreenSize` captured in px and child Bounds in px; `TransformOrigin.ToPixels(bounds)` | MED→HIGH | Operate alignment in one logical space so mixed-scale children composite correctly. |
| 26 | `BrushConstructor.CreatePerlinNoiseShader` (BrushConstructor.cs:335-368) / `PerlinNoiseBrush.BaseFrequencyX/Y` | Frequency is cycles-per-device-pixel; runs in device space → coarser/finer noise at different scale | MED→HIGH | Divide `BaseFrequency` by s (centralize in `CreatePerlinNoiseShader`); Octaves/Seed unchanged. Non-obvious, easy to miss; also feeds Displacement/FlatShadow. |
| 27 | `BrushConstructor.CreateTileShader` / `TileBrushCalculator.IntermediateSize` (BrushConstructor.cs:215-324, TileBrushCalculator.cs:41) | Tile/image/drawable intermediate bitmap sized from `Bounds.Size`/`DestinationRect.Size` (logical) → blurry tiles at export | MED→HIGH | Multiply intermediate px size by s; keep SourceRect/DestinationRect relative math intact. |
| 28 | `BrushConstructor.CreateTileShader` DrawableBrush child raster (BrushConstructor.cs:227-255) | `new GraphicsContext2D(node, new PixelSize((int)Bounds.W,(int)H))` renders child at integer logical px; child does not inherit parent scale | HIGH | Propagate parent scale into the nested child render; hard resolution boundary today. |
| 29 | `OpacityMaskRenderNode.MaskBounds` → `PushOpacityMask` → `BrushConstructor` (OpacityMaskRenderNode.cs:8,43-56; ImmediateCanvas.cs:382-391) | Mask brush resolved against `MaskBounds` (px); SaveLayer at device res; mask gradient/tile geometry relative to MaskBounds | MED | Thread the same scale; keep MaskBounds in the masked content's space. |
| 30 | `RectClipRenderNode.Clip` / `GeometryClipRenderNode` (RectClipRenderNode.cs:35, GeometryClipRenderNode.cs:43) | Clip rect/geometry applied in current canvas space (= device px at scale 1) | HIGH | One rule: logical params + canvas scale transform, OR pixel params scaled at push. Clip+mask+content must align. |
| 31 | `RoundedRectGeometry` CornerRadius clamp (RoundedRectGeometry.cs:196-225) + `PenHelper.CreateStrokePath` maxAspect loop (167) | CornerRadius `Math.Clamp` vs px max; multi-pass stroke generation — **non-linear** in size | HIGH | A uniform post-scale matrix is NOT pixel-identical to rebuilding the path at scale; spec must choose rebuild-at-scale vs matrix-scale with quality caveats. |
| 32 | `FormattedText.ToSKFont()` (FormattedText.cs:182-193) + `Measure()` (201-283) | `new SKFont(typeface, Size)` at logical Size; `Hinting=Full`, `Subpixel=true`; shaped artifacts cached keyed on font props only (no scale) | HIGH | Re-shape at `Size*scale` per target resolution; matrix/bitmap scaling is NOT equivalent (hinting bakes resolution-specific grid-fitting); make the shaping cache scale-aware. |
| 33 | `FrameCacheConfigScale` / `FrameCacheManager` (EditorConfig.cs:17-26, FrameCacheManager.cs:264) | Half/Quarter/FitToPreviewer downscales already-full-res cached bitmaps for memory — overlaps semantically with render scale | MED | Model two distinct axes (render scale vs cache-bitmap scale); fold render scale into the frame-cache key/invalidation. |
| 34 | Coordinate primitives `Point/Vector/Size/Rect/Thickness` (Beutl.Graphics, float, no unit type) | None encode logical-vs-device; whole codebase relies on "they're the same" | LOW | No type-level distinction is enforceable by the compiler — centralize the boundary in one helper; document the contract; risk of double/un-applied scale. |
| 35 | `Scene.FrameSize` semantics + Serialize/Deserialize Width/Height (Scene.cs:45,111,250-251,306-309) | Stored as raw `PixelSize`; is both logical canvas AND device output | MED | Decide: FrameSize = logical project size (recommended) + separate render scale; define serialization migration. |

---

## 3. The scale model (recommended)

### 3.1 Reconciling with the maintainer's stated design

The maintainer stated four items:
1. All user-facing/drawable properties become **logical** sizes.
2. `RenderNodeOperation` gains a **SCALE** factor.
3. The **final stage** normalizes scale to the actual output resolution.
4. This enables proxy/reduced-scale preview and full-scale export.

The research converges on a refinement that honors all four while staying implementable on the existing seams:

- **Three distinct sizes, defined precisely:**
  - **(a) Logical frame size** = `Scene.FrameSize` (the resolution-independent project canvas). All drawable/effect properties stay in these units. *Do not change their meaning.*
  - **(b) Render scale `s`** = a property of the *render request*, not the scene. Preview/proxy uses `s<1`; export uses `1.0`; supersample uses `s>1`.
  - **(c) Device target size** = `ceil(FrameSize * s)`. The only place a physical pixel buffer size is chosen.
- **Where scale lives (the hybrid):**
  - **Top-down propagation** rides on **`RenderNodeContext.Scale`** (paralleling `IsRenderCacheEnabled`'s flow through `RenderNodeProcessor.Pull`); it drives all allocation decisions (RenderTarget sizes, shader uniforms) locally where they happen.
  - **`RenderNodeOperation` exposes a read-only `EffectiveScale`** — the scale it was rasterized at. This satisfies the maintainer's literal "scale on RenderNodeOperation" for the reconciliation/normalization stage **without** being the writable propagation mechanism (which would force every op-creation site to set it correctly).
- **Logical layout stays matrix-driven.** Geometry/transform/text/brush already flow through `ImmediateCanvas.Transform → Canvas.SetMatrix`, so a single root `Matrix.CreateScale(s)` makes ~80% of vector drawing resolution-independent for free. **Keep render-scale OUT of `Transform.CreateMatrix`/`TransformGroup`** so `Matrix.TryDecomposeTransform` and editor handles keep seeing only artistic values. `ScaleTransform` (a pure percent ratio) proves artistic scale is already resolution-independent and must not be conflated with render-scale.

### 3.2 Why a root matrix alone is insufficient

A canvas-only root transform breaks every effect and source that **allocates an intermediate RenderTarget or reads integer pixel dimensions**, because those bypass the CTM: `FilterEffectActivator.Flush` / `CustomFilterEffectContext.CreateTarget` size buffers from logical bounds; SKSL/GLSL shaders read `width/height/iResolution`; `ContourTracer`/`Clipping` scan actual pixels. A CTM-only approach would rasterize effects at logical (low) resolution then upscale — degrading exactly the effects users care about (blur, shadow, mosaic, stroke) **with no performance win**. Hence the context-scale must reach those allocation sites.

### 3.3 The final-adjustment stage

Because logical×`s` == device by construction, **no separate normalization pass is needed for vector content** — the root buffer is already device size. The "final stage" is therefore the root call:
- The root `RenderNodeProcessor`/`Renderer.RenderDrawable` (Renderer.cs:174-182) enforces `targetScale = outputScale` and pushes one `Matrix.CreateScale(s)` (or renders at scaled surface size). Preview passes reduced `s`; export passes `1.0`.
- `Renderer.Snapshot()` (Renderer.cs:394-398) returns the device surface. An explicit *final resample* is wanted only when a **proxy media buffer**'s native resolution differs from `logical*s` — handled at the `DrawBitmap` dest-rect, not by a global pass.

### 3.4 Uniform vs anisotropic

Recommend **uniform `float`** for v1 (covers the stated proxy/preview-downscale case) with storage types that can widen to `Vector` later (the `ToSize(Vector)` / `FromRect(rect, Vector)` overloads already exist). Anisotropic scale breaks commutativity with rotation/perspective and complicates isotropic-radius effects (Blur sigma, Erode/Dilate). **Open decision — see §10.**

### 3.5 Rounding policy

Replace every `(int)` cast and `PixelRect.FromRect` truncation with one shared helper using a consistent convention (ceiling for sizes, floor for origins). Sub-pixel error compounds across nested scaled rasterizations and effect chains (e.g. Blur/DropShadow inflate by `sigma*3`), causing 1px seams and registration drift. **Requirement: scale=1.0 output must stay byte-identical to today's** (within encoder tolerance) — scaling affects only `s≠1` paths.

---

## 4. Mixed-scale compositing (scenario A)

### 4.1 The single reconciliation rule

**Compositing always happens in logical coordinate space.** `RenderNodeProcessor` is the single enforcement point. When `Pull(container)` gathers child ops of heterogeneous `EffectiveScale`, it selects exactly **one enforced `targetScale`**, and any op whose device buffer was rasterized at a different scale is **resampled to `targetScale` when its pixels are drawn onto the shared canvas** — once, at the boundary where the lower-scale op enters the composite, never per-effect.

- **`targetScale = max(child effective scales, parentRequestedScale)`**, capped at the device/output scale. *Max, not min:* a half-res proxy under a full-res shape is **upsampled** for that composite rather than dragging the sharp sibling down. The rule must be **one line in one place**. (A quality/perf knob — see §10.)
- The "final stage" is the root call enforcing `targetScale = outputScale`, so the top-level composite lands at full export (or reduced preview) resolution.

### 4.2 Compositing space, sampler, responsibility

- **Space:** logical. `op.Render(canvas)` still draws in logical units; what changes is the canvas backing store's device density and the single `Matrix.CreateScale(s)` pushed at the container/root boundary — *not* baked into each op's Bounds.
- **Who resamples:** the "too low res" op, via the canvas blit. Everything ultimately reaches `ImmediateCanvas.DrawRenderTarget`/`DrawSurface`. If `op.EffectiveScale != targetScale`, the processor wraps the blit with `Matrix.CreateScale(targetScale / op.EffectiveScale)` + a chosen `SKSamplingOptions`.
- **Quality:** reuse `SKCubicResampler.Mitchell` (already the `ImmediateCanvas.DrawBitmap` default, line 166). Never force every op to the lowest scale. Brush intermediates (tiles, drawable rasters, mask layers) rasterize at the **max** scale among the ops they composite with, then downsample, to avoid softness when a low-scale proxy op meets a full-scale op.
- **Where enforced:** `RenderNodeProcessor.Pull`/`PullToRoot` and the three rasterization sinks (`RasterizeToRenderTargets`/`Rasterize`/`RasterizeAndConcat`, switched to `PixelRect.FromRect(op.Bounds, targetScale)`). Nodes that rasterize children (`FilterEffectRenderNode`, `OpacityMaskRenderNode`, `RectClipRenderNode`, `BrushConstructor.CreateTileShader`, `RenderNodeCacheHelper.CreateDefaultCache`) each receive the enforced scale via `RenderNodeContext.Scale` instead of assuming 1.0.

### 4.3 Concrete mixed-scale scenarios (enumerated)

1. **Nested scenes** (`SceneDrawable.SceneBitmapRenderNode`, SceneDrawable.cs:178-194): a referenced scene renders via its own `Renderer` at its own `FrameSize`, snapshots, and `DrawRenderTarget`s into the parent at the inner pixel rect. Already a different resolution resampled at draw time today. **Fix:** inner renderer inherits the outer effective scale (render at `innerFrame*scale`) or blit via a scale-aware dest rect.
2. **Proxy media + vector siblings**: a half-res `SourceVideo` (proxy) over a full-res `Shape`/`Title`. Today `DrawBitmap` blits 1:1 at native px, so they composite at mismatched physical sizes. **Fix:** `DrawBitmap` targets a logical dest rect; reconcile the proxy as a low-`EffectiveScale` op upsampled to `targetScale`.
3. **Render cache replay** (`RenderNodeProcessor.Pull` → `CreateFromRenderTarget` → `DrawRenderTarget`): a cached tile captured at scale A composited 1:1 into a scale-B pass. **No scale in the cache key today → silent corruption.** Fix: scale-keyed cache + invalidate/resample.
4. **Effect intermediates** (`FilterEffectRenderNode` / `FilterEffectActivator.Flush`): multiple input ops wrapped as separate `EffectTarget`s, processed with one shared `SKImageFilterBuilder` and one bounds space; a blur sigma in px means a *different logical blur* per input if inputs differ in scale. `EffectTargets.CalculateBounds` (`EffectTargets.cs:27`) does a plain `Union` with no scale normalization. **Fix:** attach scale per `EffectTarget`; normalize divergent-scale targets to a common device scale before any Skia filter or union/compose; the InnerShadow/BlendMode/Mosaic CustomEffects that snapshot-and-recreate same-size targets must preserve the per-target scale.
5. **`LayerEffect`** (`FilterEffects/LayerEffect.cs`): the primary in-effect reconciliation point — flattens N targets (each potentially a different scale) into one new target with `PushTransform` + `t.Draw` and **no resample-to-common-scale step**. `EffectTarget` exposes only Bounds/OriginalBounds/RenderTarget — **no scale field** to even detect a mismatch. **Fix:** add a per-target scale (or guarantee all inputs share the context scale) and resample to `max` input scale before flatten.
6. **`DelayAnimationEffect`** (`FilterEffects/DelayAnimationEffect.cs`): spins up an independent child `FilterEffectContext` + `FilterEffectActivator` per delayed time and caches `DelayedResources` keyed by time only. If the child runs at a different (default/unscaled) scale than the parent, the trailing "ghost" frames rasterize at the wrong density. **Fix:** thread the parent scale into the child context; include scale in the resource cache key.
7. **`DrawableBrush`** (`BrushConstructor.CreateTileShader`, lines 227-255): a nested compositing boundary rasterizing a child Drawable subtree at `(int)Bounds.W/H` with **no scale propagated** — a hard resolution boundary. **Fix:** child inherits parent effective scale.
8. **3D bridge** (`Scene3DRenderNode`, `src/Beutl.Engine/Graphics3D/Scene3DRenderNode.cs:27,102`): renders at its own `RenderWidth/RenderHeight`, emits an SKSurface at `Bounds=(0,0,RenderWidth,RenderHeight)` composited 1:1 — a second independent resolution and a second mixed-scale source. **Fix:** scale the 3D sub-render in lockstep (re-render at `s`) rather than upsampling.

**Why this is tractable:** the engine already does heterogeneous-resolution compositing (nested `SceneDrawable`, effect intermediates), reconciled by rasterize-then-resample at the canvas blit. The whole job is generalizing that proven primitive plus adding per-op/per-target scale so the compositor can *detect* the mismatch. Vector content (Shapes/Text/Geometry) re-rasterizes losslessly at the target scale; raster content (SourceImage/SourceVideo, cached tiles) cannot and needs resampling.

---

## 5. Per-effect scale matrix

Legend — **Resolution-sensitive? (Y/N):** Y = the effect's *look* changes with raster resolution and parameter scaling alone can only approximate it across scales; N = parameter scaling (or invariance) makes it scale-equivalent. **Mixed-scale risk** describes what happens when inputs of differing scale meet at this effect.

### 5.1 Blur & shadows

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **Blur** (`Blur.cs`) | Sigma (Size: W,H), device px | Multiply Sigma.W, Sigma.H by `s` before `CreateBlur`; `Inflate(sigma*3)` bounds must use scaled sigma | N | If input rasterized at s1 but sigma computed for s2, radius wrong by s1/s2; Gaussian is linear→smooth, reconcile by re-raster to max scale then blur once | Sigma is a logical length; value-preserving at scale=1, no numeric migration |
| **DropShadow / DropShadowOnly** (`DropShadow.cs`) | Position (Point), Sigma (Size); Color/ShadowOnly invariant | Scale Position.X/Y AND Sigma.W/H by `s` (lengths); Color/flag unchanged | N | Position & Sigma must scale by the SAME `s` as the surface or shadow detaches (wrong offset/softness) | Value-preserving at scale=1 |
| **InnerShadow / InnerShadowOnly** (`InnerShadow.cs`) | Position, Sigma; CustomEffect | Scale Position & Sigma by `s` AND allocate the custom `CreateTarget` at `bounds*s` (two coupling sites must move together) | N | Higher: surface size, draw offset, and blur sigma computed independently inside the custom action can desync under scale | Value-preserving; note the custom-target allocation is a second migration site |
| **FlatShadow** (`FlatShadow.cs`) | Length (px AND iteration count), Angle (deg, inv.), Brush, ShadowOnly | Scale Length by `s`; run the stamp loop in device px (count = `\|Length*s\|`); trace contour on a `bounds*s` surface; Angle/Color/ShadowOnly unchanged | **Y** | Per-device-pixel stamping over a contour traced from the raster: edge fidelity & exact shape differ by scale; O(Length·s) DrawPath calls | Length value-preserving; preview at low scale looks different from export — may warrant forcing higher scale for this effect |
| **Dilate** (`Dilate.cs`) | RadiusX/Y (px) | Multiply RadiusX/Y by `s` before `CreateDilate`; `Inflate(radius)` uses scaled value | **Y** | Skia rounds radius to an integer structuring element → not a perfectly scaled copy; small fractional radii can vanish/snap | Value-preserving; small logical radii render differently once sub-1 preview scale exists |
| **Erode** (`Erode.cs`) | RadiusX/Y (px); transformBounds is identity | Multiply RadiusX/Y by `s`; no bounds inflation needed | **Y** | Same integer-structuring-element quantization; thin features can disappear at low scale | Value-preserving; warn small radii erode differently |

### 5.2 Mosaic, noise, displacement, lighting, pixel-sort

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **MosaicEffect** (`MosaicEffect.cs`) | TileSize (Size, px); Origin (RelativePoint, relative) | Multiply TileSize by `s`; Origin needs NO scaling (resolves against scaled image dims automatically) | **Y** | Pure spatial quantization: one fragCoord space can't make blocks land correctly on two densities. Must run before any mixed-scale merge | Treat stored TileSize as logical 1:1 (no per-project authoring resolution to convert from) |
| **PerlinNoise** (helper, `PerlinNoise.cs`) | sample coords; lattice period 1.0, wraps at 256; **no IProperty/ApplyTo** | Caller must sample at logical coords: `Perlin(deviceX/s, deviceY/s)`; class needs no change | **Y** | Per-sample procedural noise; `&255` wrap with no seed/offset → two scales can't reconcile to one continuous field | No serialized props; concern is at call sites that store a pixel-term frequency |
| **DisplacementMapEffect** (`DisplacementMapEffect.cs`) | DisplacementMap (Brush, rasterized at Bounds px); ShowDisplacementMap/SpreadMethod/Channel/Signed invariant | Rasterize the map brush at scaled bounds (gradient auto-correct); flags untouched; magnitude lives on the Transform | N | Image/SourceImage map content is res-sensitive; gradient/solid maps fine | No length prop here; CreateTarget must apply scale so the map rasterizes at output res |
| **DisplacementMapTranslateTransform** (`DisplacementMapTransform.cs`) | X/Y (px, `uTranslation`) | Multiply X,Y by `s`; displacement (0..1) and Channel/Signed unchanged; `uPivot` unused for translate | N | X*s correct for only one scale if content pre-merged at two scales | X,Y treated as logical going forward (no-op at scale=1) |
| **DisplacementMapScaleTransform** (`DisplacementMapTransform.cs`) | Scale/ScaleX/ScaleY (ratio, inv.); CenterX/Y (px pivot offset) | Leave uScale unchanged (dimensionless); multiply CenterX/Y by `s`; pivot uses scaled bounds | N | Scale ratio mixed-scale-safe; pivot offset lands at wrong physical point for off-scale source | Scale* need no migration; CenterX/Y treated as logical |
| **DisplacementMapRotationTransform** (`DisplacementMapTransform.cs`) | Rotation (rad, inv.); CenterX/Y (px) | Angle unchanged; multiply CenterX/Y by `s`; pivot from scaled bounds | N | Angle safe; pivot offset wrong for off-scale source | Rotation no migration; CenterX/Y logical |
| **Lighting** (`Lighting.cs`) | — (5×4 color matrix) | Fully invariant; write same Multiply/Add, no transform | N | None — per-pixel point op composites correctly anywhere | No pixel params; no migration |
| **PixelSortEffect** (`PixelSortEffect.cs`) | Direction/Width/Height (texture px); ThresholdMin/Max (% of key range, inv.); SortKey/Ascending (inv.) | Width/Height auto-track scaled texture; all params transfer unchanged | **Y** | Severe: sorts contiguous runs of pixels; a logical segment spans `s×` as many pixels → different segment set/streak length. No scalar reconciles it. Requires Vulkan 3D (silent no-op otherwise) | Thresholds are %; no length to migrate. Qualitative: preview≠export; spec must decide whether it forces full-scale rendering of its subtree |

### 5.3 Basic color (all invariant)

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **Brightness** (`Brightness.cs`) | — (Amount %, color matrix) | None; leave Amount unchanged | N | None (per-pixel matrix) | None |
| **Gamma** (`Gamma.cs`) | — (Amount, Strength %) | None; pointwise SKSL, no neighbor taps | N | None | None |
| **Saturate** (`Saturate.cs`) | — (Amount %) | None; saturation color matrix | N | None | None |
| **HueRotate** (`HueRotate.cs`) | — (Angle deg) | None; angular, hue-rotate matrix | N | None | None |
| **Invert** (`Invert.cs`) | — (Amount %, ExcludeAlphaChannel) | None; pointwise SKSL | N | None | None |
| **Negaposi** (`Negaposi.cs`) | — (Red/Green/Blue 0..255 color, Strength %) | None; R/G/B are COLOR units (not spatial) | N | None | None — ensure automated migration doesn't treat the int color fields as pixels |

### 5.4 Advanced color & LUT (all invariant)

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **ColorGrading** (`ColorGrading.cs`) | — (EV/%, degrees, 0-1 pivots/ranges, RGB grading vectors) | None; pointwise; LowRange/HighRange are luma thresholds not positions | N | Low; only a half-pixel Decal edge fringe differs across scales | None |
| **Curves** (`Curves.cs`) | — (CurveMap control points in 0-1) | None; curve LUT baked at fixed 10000×1, independent of render res | N | Low; LUT precision constant across scales | None |
| **HighContrast** (`HighContrast.cs`) | — (Contrast %, InvertStyle enum, Grayscale bool) | None; `SKColorFilter.CreateHighContrast`, no spatial extent | N | None | None |
| **HighContrastInvertStyle** (`HighContrastInvertStyle.cs`) | — (enum) | N/A | N | None | None |
| **LumaColor** (`LumaColor.cs`) | — (parameter-free) | None; fixed luma-to-alpha matrix | N | None | None |
| **LutEffect** (`LutEffect.cs`) | — (Source .cube, Strength %) | None; LUT indexed by color value; grid size intrinsic to file | N | Low; mapping identical at any scale | None |
| **Threshold** (`Threshold.cs`) | — (Value, Smoothness, Strength %) | None; Smoothness is a width in LUMA space (0..1), NOT spatial — do NOT scale | N | Low; soft band thickness follows the source luma gradient (source-res effect), not a param | None |

### 5.5 Keying, shift, contour

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **ChromaKey** (`ChromaKey.cs`) | — (Color, HueRange/SaturationRange/Boundary in deg/%) | None; HSV color-domain comparison, fragCoord 1:1 sample | N | Effect invariant; only smoothstep edge softness depends on AA edge pixel count (edge-fidelity, not a param) | None |
| **ColorKey** (`ColorKey.cs`) | — (Color, Range/Boundary % of luma) | None; Rec.709 luma diff per pixel | N | Same edge-fidelity-only note | None |
| **ColorShift** (`ColorShift.cs`) | Red/Green/Blue/AlphaOffset (PixelPoint, int device px); minOffset derived | Multiply every offset by `s`; `TransformBoundsCore` must use active scale (currently `ToPoint(1)`); ideally widen type to float `Point` for sub-pixel scaled offsets | N | Relies on fragCoord == source pixel grid; if source produced at a different scale, separation distance wrong relative to content; int minOffset re-anchoring incompatible with fractional offsets | **HIGH**: stored PixelPoint reinterpreted as logical at authoring res then ×`s` at draw; if type widens to float, migrate PixelPoint 1:1 (value preserved, type widened) |
| **ContourTracer** (`ContourTracer.cs`, static utility) | output PixelPoint vertices (bitmap px grid) | Runs on the buffer at its raster scale; output vertices in `logical*s` px; self-consistent IF callers' lengths (pen thickness, shadow length) also ×`s` | **Y** | Vertex density / corner sharpness / which thin features survive all depend on raster resolution; two scales → different contours; holes <1 low-res px not traced | No serialized value (computed at render time); concern is in consumers (Stroke/FlatShadow/PartsSplit lengths) |

### 5.6 Structural & transform

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **TransformEffect** (`TransformEffect.cs`) | Transform.Matrix translation column (px); TransformOrigin (relative=safe, absolute=px) | Scale only the matrix translation by `s` (linear part rotate/scale/skew invariant); equivalent `M_scaled = S·M·S⁻¹`; relative origins auto-correct, absolute origins ×`s` | N | Resamples its input — magnifying a low-scale (proxy) target yields soft/aliased output even when geometry is correct (quality, not geometry) | Translation in px → logical reinterpretation value-preserving only if legacy=scale 1.0; absolute origins same concern |
| **StrokeEffect** (`StrokeEffect.cs`) | Offset (Point px); Pen.Thickness (px), Pen.Offset (px), DashArray (thickness-relative), MiterLimit (ratio, inv.) | Multiply Thickness, Pen.Offset, Offset by `s`; DashArray needs no separate change (relative to thickness); MiterLimit/Trim unchanged | **Y** | Border path traced from the rasterized alpha mask (`ContourTracer`) → fewer/blockier vertices at low scale; not a scaled-down version (stair-stepping, lost thin features) | Thickness/Offset divided by legacy reference scale on migration; DashArray multipliers & MiterLimit% need no conversion |
| **Clipping** (`Clipping.cs`) | Left/Top/Right/Bottom (px insets); AutoClip (computed px from alpha scan); AutoCenter (geometric) | Multiply L/T/R/B by `s`; AutoCenter/AutoClip flags unchanged; sub-pixel snapping self-adjusts on scaled thickness | **Y** | (1) manual clip relies on integer px snapping → cut position can shift ~1 logical px at low scale; (2) AutoClip scans the device alpha mask → AA fringe present at full scale may vanish at proxy scale, different box | L/T/R/B divided by legacy scale; AutoClip results inherently non-portable across scales (recompute at render time) |
| **SplitEffect** (`SplitEffect.cs`) | HorizontalSpacing/VerticalSpacing (px); Divisions (counts, inv.) | Multiply spacings by `s`; keep Divisions as counts (divWidth auto-scales via Bounds) | **Y** | Guard `(int)divWidth<=0` drops a tile whose logical size < 1 device px → tiles lost at proxy scale; integer-offset seams shift sub-pixel per scale | Spacings divided by legacy scale; Divisions no conversion |
| **PartsSplitEffect** (`PartsSplitEffect.cs`) | — (no numeric params; geometry from raster contour) | Nothing to multiply; scale-equivariant in principle | **Y** | Strongly res-sensitive: number/connectivity/bounds of detected parts all depend on the per-pixel alpha mask; thin bridges merge/break, tiny components vanish at proxy scale → a *different set of parts* | No numeric migration; downstream keyframes targeting parts-by-index are not portable across scales |
| **BlendEffect** (`BlendEffect.cs`) | Brush (solid=safe, gradient=safe; tile/image DestinationRect/SourceRect/tile size = px); BlendMode (enum, inv.) | Solid/gradient: nothing to scale; tile/image brush px geometry ×`s` so pattern covers same logical area | N | Low for solid/gradient (per-pixel blend); tile/image brush pattern period fixed in px → different tile counts per differently-scaled sibling | Solid/gradient no migration; tile/image brush px geometry ÷ legacy scale; legacy `Color`→SolidColorBrush path unaffected |

### 5.7 Motion, group & scripted

| Effect | Pixel-dependent params | Scale transform rule | Res-sensitive? | Mixed-scale risk | Migration note |
|---|---|---|---|---|---|
| **ShakeEffect** (`ShakeEffect.cs`) | StrengthX/Y (px displacement); Speed (time-domain, inv.) | Multiply StrengthX/Y by `s`; Perlin lattice args (time/index/offset seeds) unchanged → same frame-jump pattern; Speed unchanged | N | Only translates Bounds; amplitude expressed at wrong scale shifts content by wrong px count | Strength px values value-correct if logical extent == old pixel extent; else rescale by oldRes/newBase; Speed safe |
| **PathFollowEffect** (`PathFollowEffect.cs`) | Geometry path coords (px); Progress (% arc length, inv.); FollowRotation→angle (rad, inv.) | Keep math logical (path logical, target logical, CreateTarget allocates `ceil(bounds*s)`); translation & pivot from Bounds/2 auto-scale; angle/progress unchanged | N | Re-rasterizes via CreateTarget; mismatched density → blur/aliasing of moved layer; path units at wrong scale → wrong travel distance | Path point coords: no conversion if logical==old pixel extent; Progress/FollowRotation no migration |
| **DelayAnimationEffect** (`DelayAnimationEffect.cs`) | — (Delay ms, inv.); wraps a child effect | Scale-invariant itself; MUST thread parent context scale into the child `FilterEffectContext`/activator (line 71) so the wrapped effect rasterizes at the same density | N | Key mixed-scale site: independent child pipeline; child at default scale ≠ parent → ghost frames at wrong density; `DelayedResources` cache keyed by time only | No pixel params; resource cache must be invalidated on scale change |
| **LayerEffect** (`LayerEffect.cs`) | — (no params; flattens all targets) | No math change if Bounds logical + CreateTarget applies `s`; union bounds & relative offsets scale uniformly | N | **Primary scenario-A point**: composites N targets each potentially a different scale into one surface with no resample-to-common-scale; `EffectTarget` has no scale field to detect mismatch | No serialized params; prerequisite is a per-target scale (or guaranteed shared context scale) |
| **CSharpScriptEffect** (`CSharpScriptEffect.cs`) | Script (user C#; typically px constants via Context.Blur etc.); Progress/Duration/Time (inv.) | Engine cannot auto-transform user code; expose a `Scale`/`RenderScale` global so authors write `Context.Blur(new Size(10*Scale,...))`; OR keep FilterEffectContext APIs logical so unmodified scripts become scale-correct | N (engine) / arbitrary (script) | Arbitrary: script can call any context method with hand-picked px constants; engine cannot validate/correct | Pixel literals in source can't be migrated; will silently mean logical units after the change; document + expose Scale global |
| **GLSLScriptEffect** (`GLSLScriptEffect.cs`) | width/height push constants (device px); fragCoord (0..1 UV, inv. alone); progress/duration/time (inv.) | Keep width/height = device size of the scaled target (texel math stays correct); shaders using `1.0/width` auto-correct; hard-coded pixel radii need an explicit `scale` uniform (can't rewrite SPIR-V) | **Y** | Per-texel kernels (blur/edge/sharpen) produce different content-radius on differently-scaled targets; not byte-identical across res | Stored as source text; keep width/height device-pixel meaning; add a scale uniform rather than changing their meaning |
| **SKSLScriptEffect** (`SKSLScriptEffect.cs`) | width/height/iResolution + fragCoord (all DEVICE px in SkSL); progress/duration/time/iTime (inv.) | Keep width/height/iResolution & fragCoord in the device space of the scaled target; expose a `scale` uniform for absolute-pixel literals | **Y** | fragCoord ranges & iResolution differ per target → pixel-space shader yields different absolute effect sizes per layer; only UV-normalized subset is scale-stable | Source text; preserve the device-pixel meaning of width/height/fragCoord; add a scale uniform; cannot migrate embedded literals |

### 5.8 ⚠️ Callout — inherently resolution-sensitive effects

These are **Y** in the matrix and **cannot be made bit-identical across scales by parameter scaling** — they trace, sort, scan, or step in device pixels, so their look differs between proxy preview and full export. The spec MUST decide a per-effect contract (best-effort approximate vs force full-scale rendering of the subtree vs warn the user):

- **`FlatShadow`** — per-device-pixel stamping loop over a traced contour.
- **`Dilate` / `Erode`** — integer structuring element; thin features vanish/snap at low scale.
- **`PixelSortEffect`** — sorts contiguous pixel runs; streak length is a pixel-count function; also Vulkan-gated (silent no-op).
- **`ContourTracer`-based `StrokeEffect`, `FlatShadow`, `PartsSplitEffect`** — vertex density, corner sharpness, which features survive, and even the *number of detected parts* depend on raster resolution.
- **`Clipping` (AutoClip)** — scans the device alpha mask; AA fringe presence is scale-dependent.
- **`SplitEffect`** — drops tiles whose logical size < 1 device px at low scale.
- **`MosaicEffect`** — block grid is a pixel-quantization; representative-color sampling re-samples per scale.
- **`PerlinNoise` (and noise-driven Shake/Displacement/FlatShadow)** — per-sample procedural field; `&255` wrap, no seed → can't reconcile two scales continuously.
- **SKSL/GLSL custom shaders** — any per-texel kernel; plugin-authored shaders can't be introspected.

For these, "identical output, just at a different resolution" is only approximate. Recommendation: classify each as **exact** (parameter-scaling suffices) vs **best-effort** (document divergence) vs **force-full-scale** (correctness over speed), and gate preview accordingly.

---

## 6. Brushes / Pens / Text scale rules

### 6.1 Brushes (`BrushConstructor`, `TileBrushCalculator`)

Brush params split into two camps:

- **Already resolution-relative (scale for free if Bounds scale):** gradient StartPoint/EndPoint/Center/GradientOrigin (RelativePoint), TileBrush SourceRect/DestinationRect (RelativeRect), TransformOrigin, `RadialGradientBrush.Radius` (% of Bounds.Width), GradientStop.Offset (0..1), ConicGradientBrush.Angle (deg), Trim* (%). **No change needed** beyond ensuring the bounds they resolve against are device-scaled.
- **Absolute / pixel-baked (must transform):**
  - **`PerlinNoiseBrush.BaseFrequencyX/Y`** — cycles-per-device-pixel; **divide by `s`** so the noise period in logical units is invariant. Centralize in `BrushConstructor.CreatePerlinNoiseShader` so every consumer (fill, mask, displacement, shadow) gets it. *Least obvious, highest-impact brush transform.*
  - **Tile/Image/Drawable intermediate raster resolution** (`CreateTileShader`, `TileBrushCalculator.IntermediateSize`) — multiply the intermediate px size by `s`; keep relative SourceRect/DestinationRect math intact.
  - **`DrawableBrush` child subtree** — propagate parent effective scale instead of `(int)Bounds.W/H` (today a hard resolution boundary).
  - **`Bounds.Position` offsets / transform-origins** (~10 sites in `BrushConstructor`) — route all logical→shader-space mapping through one helper so shader-space and bounds-space stay consistent.

**Mixed-scale rule for brushes:** rasterize brush intermediates (tiles, drawable rasters, opacity-mask layers) at the **max** scale among the ops they composite with, then downsample, to avoid softness when a low-scale proxy op meets a full-scale op.

### 6.2 Pens (`PenHelper`, `Pen`)

- **Scale by `s`:** `Pen.Thickness` (→ `paint.StrokeWidth`), `Pen.Offset`, and the computed dash lengths/`DashOffset` (these are thickness-relative, so scaling thickness propagates). Inflate render bounds (`GetBounds`/`CalculateBoundsWithStrokeCap`/`GetRealThickness`) using scaled thickness.
- **Unchanged (invariant):** `MiterLimit` (ratio), StrokeCap/Join/Alignment, `TrimStart/End/Offset` (%).
- **Cache hazard:** `Geometry.GetCachedStrokePath` / `_cachedStrokePath` is keyed on `(pen, version)` only — **add render scale to the key** (or generate the stroke in logical space and rely on the canvas matrix). Stale stroke paths reused across scales is a real correctness bug.
- **Canonical decision (open):** generate stroke geometry in **logical** space (Pen scale-agnostic, only the matrix changes — simplest, but interacts with the per-pixel `maxAspect < thickness` stroke-splitting loop and TightBounds caching) vs **device** space (bake `s` in). See §10.

### 6.3 Text (`TextBlock`, `FormattedText`, `TextRenderNode`)

Text is uniquely sensitive: **re-shaping at scale S1 vs S2 produces genuinely different glyph outlines/hinting, not just resampled pixels.**

- `FormattedText.ToSKFont()` builds `new SKFont(typeface, Size)` at logical Size with `Hinting=Full` + `Subpixel=true`. `Hinting=Full` **bakes resolution-specific grid-fitting into outlines**, so "shape at logical size then scale the matrix" ≠ "shape at device size." Matrix-scaling or bitmap-upscaling text is NOT pixel-identical between preview and export.
- **Rule: always RE-SHAPE text at the target device scale** (rebuild SKFont at `Size*s`); never matrix-scale or bitmap-upscale. Treat per-op scale as a hint to choose shaping resolution, forcing convergence to the output scale at the final stage. For text the final stage is a no-op re-shape boundary, not a bitmap resample.
- **All logical typographic inputs scale together:** `Size`, `Spacing`, `Pen.Thickness` (stroke), and inline rich-text overrides (`<size N>`, `<cspace N>` via `TextElementsBuilder.PushSize/PushSpacing`). Funnel them through one scale-application point (`FormattedTextInfo` or the op-level scale) or relative drift appears at non-1 scales.
- **Caching:** `FormattedText` memoizes `_textBlob`/`_fillPath`/`_strokePath`/`_metrics` keyed on font props only — **make scale part of the shaping inputs/cache key** (or move shaping into a scale-parameterized method that doesn't memoize across scales), and make `RenderNodeCache` re-rasterize text at the active scale.
- **Metrics** (Ascent/Descent/Leading from `FontMetrics`) scale linearly with Size and stay correct as long as the whole text is re-shaped at scale.
- **Open decision:** keep `Hinting=Full` (sharp per-resolution, but preview≠export bit-wise) vs reduce to `None`/`Slight` (scale-stable outlines, perceptually equivalent). Defines "looks identical" for text. See §10.
- **Design note (orthogonality):** prefer keeping `FormattedText` purely logical and parameterizing shaping by scale at op-process time, over adding a scale field to `FormattedText` (public, enters `Equals`/`GetHashCode`/node diffing). Keep hit-test paths (`GetFillPath`/`GetStrokePath`) in logical space.

---

## 7. Proxy / optimized-media integration path (the WHY)

The resolution-independent pipeline is the **foundation** for proxy; once scale exists, proxy slots in along two axes that must be kept distinct:

- **Render scale `s`** — render the whole tree (vectors + effects) at reduced resolution for cheap preview. This is the cost/quality axis the pipeline change delivers.
- **Proxy media** — decode a source at reduced resolution. This is the decode/IO axis.

**How proxy reports its resolution today (implicitly):** media sources expose `FrameSize` (`VideoSource.Resource.FrameSize`, `ImageSource.Resource.FrameSize`) which drives node Bounds (`ImageSourceRenderNode.cs:12`). A proxy file simply has a smaller `FrameSize`, so a low-effective-scale source flows through the **same mixed-scale reconciliation path** (§4) as any other off-scale op: a source whose `FrameSize` is below the composition scale is treated as a low-`EffectiveScale` op to upsample.

**The keystone that's missing:** there is **no decoder-level scale request**. `MediaOptions` carries only `StreamsToLoad`; `MediaReader.Read`/`ReadVideo` always returns a full-`FrameSize` bitmap; `ImageSource` always does `Bitmap.FromStream` at full res. To enable true proxy *decode* (the real perf win — decode dominates video cost):
- Add an **optional, additive** target-size/scale hint to `MediaOptions` (default = native) so a caller can request reduced-resolution decode, while `VideoSource`/`ImageSource` keep reporting the **logical (full)** `FrameSize` to the drawable layer. The decoded bitmap's actual pixel size and the reported logical size become two distinct values.
- For FFmpeg this crosses the **GPL/MIT IPC boundary**: the field must be added to the IPC `OpenFile`/`ReadVideo` protocol (`DecodingMessages.cs`), honored in `Beutl.FFmpegWorker`, and covered by a contract test in `tests/Beutl.FFmpegIpc.Tests` — *not* worked around. Gate behind decoder capability; default native so existing decoders keep working.

**Two interpretations of "proxy media" the spec must pin down:**
1. **On-the-fly reduced decode** of the original at a target size (best perf, requires the `MediaOptions`/IPC change + per-decoder support).
2. **A pre-generated low-res media file** the user/optimizer substitutes (the URI/`Open` path supports this trivially — point at a different file).

**Preview vs export quality:** the correct quality path for export is to **re-decode at full (export) scale** via the same hint, not to upscale a proxy. Preview = proxy + upscale (Mitchell, adequate). Document that proxy is preview-only.

**Shared-resource cache:** `VideoSource._mediaReaderRef`/`ImageSource._bitmapRef` are keyed by URI via `WeakReference`. A single shared reader can't serve a full-res export and a proxy preview at once if decode size is baked into the reader — **key the cache by `(URI, decodeTargetSize/scale)`** or decode native and downscale per-consumer. The existing `DisableResourceShare` flag (which already isolates the export renderer) is the precedent to extend.

**A source's logical size with a proxy active** must be the **original native `FrameSize`** (or a user-set logical size), stable across proxy↔full so layout/Bounds/hit-testing don't shift. `MeasureCore` currently has only the decoded `FrameSize` — a second, stable logical dimension is required.

**Scope boundary (recommended):** feature 003 builds the *render-scale plumbing* (and implicit proxy-via-FrameSize reconciliation). The full proxy *file lifecycle / decoder-level scale request* can be a follow-up, but the design must **not foreclose it** — keep `MediaOptions` extensible. **Confirm with maintainer — see §10.**

---

## 8. Serialized-project migration & backward-compatibility

### 8.1 The anchor: scale 1.0 == today

Existing `.belm`/`.bobj` files were authored against the full-resolution canvas, which **is** render scale 1.0, where `1 logical unit == 1 device pixel` is the current invariant. So the stored numbers (Width/Height/CornerRadius/Pen.Thickness/Blur.Sigma/positions/effect params) **already are the logical values**.

**Recommendation: NO file-format version bump and NO value rewrite.** Define "render scale 1.0 == 1 logical unit = 1 pixel at the project FrameSize." Export at scale 1.0 is bit-for-bit unchanged (within encoder tolerance); only reduced-scale **preview** changes, which is the only place change is wanted. A format bump that rewrites pixel values to logical is an **identity transform** at scale 1.0 — unnecessary churn that also breaks external tooling reading the files.

This holds **only if** logical units are pinned to "1 unit = 1 px at FrameSize." Redefining logical as a normalized fraction of the frame (cleaner long-term) forces rescaling every stored value and a migration. Recommend the former. **Confirm — see §10.**

### 8.2 Engine-API breakage (separate from file compat)

While *files* need no migration, the *engine public surface* changes are breaking: `RenderNodeContext`, `RenderNodeOperation`, `FilterEffectContext`, `CustomFilterEffectContext`, `EffectTarget`, `Renderer`/`IRenderer`/`SceneRenderer` ctor, `GraphicsContext2D` ctor. These are consumed by out-of-tree plugin effects/drawables and by `Beutl.NodeGraph` (`NodeGraphFilterEffectRenderNode`, `RenderNodeDrawable`). Per AGENTS.md "adopt better designs, break with feat!/BREAKING CHANGE":
- Ship as **`refactor!:` / `feat!:`** with a **`BREAKING CHANGE:`** footer naming the affected projects (`Beutl.Engine`, `Beutl.NodeGraph`, `Beutl.Extensibility`).
- Update all in-tree call sites in the same change. No `[Obsolete]` shims (AGENTS.md policy) except a published extensibility contract with an explicit deprecation window.
- Route the public-API changes through `beutl-design-reviewer`.

### 8.3 Properties whose *unit* may be reinterpreted

A handful of effect props are typed `PixelPoint`/`PixelSize` (e.g. `ColorShift` offsets, `MatrixConvolution` kernel/offset, `Mosaic` TileSize). Whether reclassified to logical (cleaner) or kept as device-pixel-at-scale-1 (smaller diff), and especially if `ColorShift`'s `PixelPoint`→float `Point` widening happens, serialized values migrate 1:1 (value preserved, type widened). This is the orthogonality-vs-compat call AGENTS.md asks to surface — **decide explicitly.**

---

## 9. Editor/hit-test/handles, render-cache, and 3D interactions

### 9.1 Hit-testing & handles

The editor already has a clean logical/device seam: `PlayerView` computes `frameScale = Image.Bounds.Width / Scene.FrameSize.Width` and `TransformHandlesOverlay.LocalToImage` (`src/Beutl/Views/TransformHandlesOverlay.cs:149-153`) multiplies by `frameScale`. **The trap:** today `frameScale` conflates *display zoom* with *render resolution* because they are equal. Once render scale exists, these split into three distinct factors (display zoom, render scale, artistic Transform scale).

**Rule:** hit-testing and handle math run in **logical** units, independent of render scale.
- `Renderer.HitTest` (Renderer.cs:274-301) and per-node `HitTest` must take/return logical coords.
- The editor pointer is divided by *display-zoom only*; render scale is applied/removed inside the renderer.
- `Matrix.TryDecomposeTransform` must remain scale-invariant across render scales (keep render-scale out of the artistic matrix), or editor gizmos and serialized transforms corrupt.
- **Requirement:** hit-testing the same logical point at two render scales returns the same Drawable; transform-handle drags produce identical document Transform values regardless of preview scale. Cover both 2D (`OnPressedHitTest`) and 3D (`PlayerView` line 1223, `GizmoHitTest`) paths.

### 9.2 Render cache

- `RenderNodeCache` stores `(RenderTarget, Rect)` with **no scale** and replays via `CreateFromRenderTarget` → `DrawSurface` 1:1. A scale change without invalidation blits stale-scale tiles at the wrong size with no resample — **silent visual corruption, not an exception.**
- `RenderCacheRules.Match` gates on absolute device-pixel count (`MaxPixels=1e6`, `MinPixels=1` from `EditorConfig.NodeCacheMaxPixels/MinPixels`); the same logical content caches differently per scale.
- **Fix:** make scale part of cache identity (invalidate-on-scale-change is the simple correct default; scale-keyed multi-entry is the smoother-but-memory-heavier alternative — see §10). Express thresholds in logical units (÷ scale²). When adding a scale field to any node/cache entry, thread it through `Update(...)` change-detection and `Equals`/`GetHashCode` (per the subtree CLAUDE.md, graph diffing silently skips updates otherwise).
- `BlendModeRenderNode` sets `IsRenderCacheEnabled = (BlendMode == SrcOver)` — non-SrcOver subtrees are always re-rasterized at the pass scale while SrcOver siblings may come from a differently-scaled cache: a built-in mixed-scale trigger.
- `EditViewModel` rebuilds Renderer/Composer/FrameCacheManager only on `FrameSizeProperty` change — a render-scale change needs the same rebuild + cache clear trigger.

### 9.3 3D interactions

- `Scene3DRenderNode` (`Graphics3D/Scene3DRenderNode.cs:27,102`) renders at its own `RenderWidth/RenderHeight` and composites an SKSurface 1:1 — a second independent resolution and mixed-scale source. Under a global render scale, the 3D sub-render should **scale its RenderWidth/Height in lockstep** (re-render at `s`) rather than be uniformly upsampled, or 3D content will be sharper/softer than 2D at the same preview scale.
- 3D gizmo hit-tests use the same `frameScale` assumption and must map screen coords using **display-zoom only**.
- The scale concept is **2D-only**; bound it cleanly so it doesn't leak wrong assumptions into `Graphics3D`. **Open: is 3D in scope for 003 or deferred? — see §10.**
- `Rotation3DTransform` perspective matrices do **not** commute with uniform scale (S·P ≠ P·S); a perspective child resampled into a different-scale parent is not pixel-identical to native rendering. Needs an explicit rule (scale outside the projective term, or scale Center/Depth) and tolerance-based tests.

---

## 10. Risk register & open questions

### 10.1 Risk register (ranked)

| # | Risk | Sev | Mitigation |
|---|---|---|---|
| 1 | **Per-effect appearance drift**: ~30+ effects with bespoke unit semantics; missing one pixel-magnitude param (e.g. Blur's `sigma*3` bounds inflation must also use scaled sigma) yields clipped/mispositioned output only at non-unit scales — no compile-time signal. | HIGH | Centralize scaling in `FilterEffectContext` primitives (covers forwarders for free); per-effect golden-image parity tests (scale 1.0 vs 0.5-upscaled) are mandatory; explicit per-param classification table. |
| 2 | **Render-cache poisoning**: cache keyed without scale → stale low-res tiles composited at high res (or vice versa); silent, hard to notice. | HIGH | Scale in cache identity + invalidate on scale change; test that builds a cache at scale A and renders at scale B. |
| 3 | **Skia-filter-mode vs render-target-mode asymmetry**: Skia-filter effects defer to the canvas CTM; render-target effects bake a fixed surface at Flush. A naive scale on one path makes blur right but mosaic wrong — visually subtle, easy to ship broken. | HIGH | One scale owned by `FilterEffectContext` applied uniformly: surfaces ×`s` with scaled CTM AND filter params ×`s`; golden tests per effect chain. |
| 4 | **Shader effects silently wrong**: SKSL/GLSL read raw pixel coords/uniforms; wrong scale produces wrong-looking output, not exceptions. Plugin shaders can't be introspected. | HIGH | Keep width/height device-meaning + add explicit `scale` uniform; document the contract; golden-image tests; per-effect "IsResolutionDependent" capability flag. |
| 5 | **Integer truncation / sub-pixel drift**: `(int)` casts and `PixelRect.FromRect` truncation compound across nested scaled targets → 1px seams, registration drift between an op and its cached copy, especially at fractional scales. | HIGH | One shared rounding helper (ceil sizes / floor origins); inflate-by-1 conventions; require scale=1.0 byte-identical to today. |
| 6 | **Hit-test divergence**: render at reduced scale but pointer mapped in full-res px → clicks land wrong; conflation of display-zoom + render-scale + artistic-scale in `frameScale`. | HIGH | Canonical logical hit-test space end-to-end; pointer ÷ display-zoom only; tests at multiple scales; cover 2D + 3D. |
| 7 | **Mixed-scale = no clean high-quality answer**: reconciling subtrees at different scales requires lossy resampling; `max` protects quality but upsamples proxies every frame; `min` degrades sharp siblings. A real quality/perf trade, not correctness-only. | HIGH | Define one policy (recommend `max`, capped at output scale); reuse Mitchell; golden tests for scenario A. |
| 8 | **Inherently res-sensitive effects can't be made identical** (PixelSort, contour-based Stroke/FlatShadow/PartsSplit, AutoClip, integer convolution, per-texel shaders): proxy preview genuinely ≠ export. | HIGH | Classify exact/best-effort/force-full-scale per effect; tolerance-based tests; possibly force full-scale rendering of those subtrees even in preview. |
| 9 | **Export buffer/size mismatch**: encoder assumes produced bitmap size == `SourceSize`. If render scale changes snapshot size without updating `SourceSize` in lockstep → stride/size mismatch, corruption or crash. | HIGH | Derive `SourceSize` from the actual rendered surface size in one place (`FrameProviderImpl`/`OutputViewModel`); assert `bitmap.W/H == SourceSize` before encode. |
| 10 | **Public-surface / plugin breakage**: `RenderNodeContext`/`Operation`/`FilterEffectContext`/`EffectTarget`/`Renderer` are out-of-tree extensibility points + used by NodeGraph. | HIGH | `refactor!:`/`feat!:` + `BREAKING CHANGE:` footer; migrate in-tree call sites same change; `beutl-design-reviewer`. |
| 11 | **Text quality**: `Hinting=Full`+`Subpixel` bake resolution-specific outlines; matrix/bitmap scaling ≠ device-shaping → preview/export not pixel-identical. | HIGH | Always re-shape at device scale; decide Hinting policy; scale-aware shaping cache; tests at two scales. |
| 12 | **Stale stroke-path cache**: `Geometry._cachedStrokePath` keyed on (pen, version) only. | HIGH | Add scale to the key; regression test toggling scale with same pen asserts path bounds change proportionally. |
| 13 | **Perlin frequency easy to miss**: scale geometry but forget to divide `BaseFrequency` by `s` → coarser/finer noise; amplified downstream (Displacement/FlatShadow). | HIGH | Centralize in `CreatePerlinNoiseShader`; golden test at two scales. |
| 14 | **Non-linear geometry generation**: RoundedRect CornerRadius clamp + PenHelper stroke generation aren't linear in size; uniform post-scale ≠ rebuild-at-scale at corners/thick strokes. | HIGH | Spec separates "rebuild at scale" (correct, more CPU) from "matrix post-scale" (cheap, approximate) per case; golden tests for corners/Inside-Outside strokes. |
| 15 | **SourceImage logical size from decoded FrameSize**: proxy↔full swap changes measured size → content jumps between preview and export. | HIGH | Define logical bounds from a resolution-independent source descriptor; thread scale into the node to map logical→decoded-proxy px at draw; test identical Bounds for proxy vs full. |
| 16 | **Pervasive partial migration**: `ToSize(1)` + pixel assumptions across ~8 drawable/container files + every `*RenderNode`; a partial change leaves some nodes pixel-coupled. | HIGH | Treat scale plumbing as one atomic change through `GraphicsContext2D`→`RenderNodeOperation/Context`→Renderer final stage; grep-enforce no `ToSize(1)` remains; render-at-0.5x integration test asserting pixel-equality with downscaled 1x. |
| 17 | **Backdrop/snapshot scale binding**: `canvasSize.ToSize(1)` literal + whole-surface capture hard-bind backdrop to one resolution. | MED | Scale-aware backdrop bounds; tag snapshot with capture scale; resample/re-snapshot on mismatch; tests at two scales. |
| 18 | **GPL/MIT IPC ripple for proxy decode**: target-size field must be added to the IPC protocol + honored in the GPL worker, not worked around. | MED | Additive optional field on `MediaOptions`/protocol; contract test in `tests/Beutl.FFmpegIpc.Tests`; gate behind decoder capability. |
| 19 | **`FrameCacheConfigScale` collision**: existing Half/Quarter cache-bitmap scale overlaps semantically with render scale. | MED | Model two distinct axes; fold render scale into the frame-cache key. |
| 20 | **Perspective non-commutativity / non-uniform scale**: 3D-rotated content not bit-identical across mixed scales; anisotropic scale breaks rotation commutativity. | MED | Constrain render scale to uniform in v1; explicit perspective rule; golden tests for 3D-rotated content. |
| 21 | **Float accumulation / shimmer**: non-integer scales in alignment/transform math → half-pixel shifts invisible at full scale, visible as shimmer in animated reduced-scale preview. | LOW | Centralize rounding; minimize scale boundaries (rasterize once at leaf at target scale). |
| 22 | **Memory/perf framing**: `max`-scale reconciliation upsamples a proxy under a full-res element → proxy saves decode cost but not composite cost for that subtree; RgbaF16 + cache tiles per scale increase memory. | LOW | Communicate the perf model; reuse `SKSurfaceCounter` ref-counting; dispose scaled intermediates promptly. |

### 10.2 Open questions needing human decision

1. **Scale type — uniform `float` vs `Vector` (anisotropic).** Recommend uniform for v1 (covers proxy/preview-downscale; storage primitives can widen later). Vector enables anamorphic/non-square pixels but breaks rotation/perspective commutativity and complicates isotropic-radius effects. *Maintainer call before plumbing — it touches every signature.*
2. **Literal "scale on RenderNodeOperation."** Confirm scale **propagates** via `RenderNodeContext` while `RenderNodeOperation` only **exposes** a read-only `EffectiveScale`, vs a writable Scale field as the primary mechanism.
3. **Mixed-scale composition-scale policy.** `max(children)` capped at device (recommended, sharpest) vs always-device vs `min`. Determines eager vs lazy proxy upsampling — a quality/perf decision.
4. **Per-param scaling — central vs per-effect hook.** Centralize in `FilterEffectContext` primitives (covers built-ins; out-of-tree CustomEffect authors on their own) vs a per-effect `ScaleParameters(scale)` virtual (explicit, extensible, ~30 effects + permanent author burden). Recommend central + a per-effect escape hatch for pixel-reading shader/contour effects. Affects the plugin contract.
5. **Proxy scope in 003.** Render-scale plumbing only (proxy via implicit FrameSize) vs add decoder-level scale request to `MediaOptions`/IPC now. Maintainer framed proxy as a *future* workflow — confirm out of scope for 003 but ensure the design doesn't foreclose it.
6. **Logical-unit anchor.** "1 logical unit = 1 px at FrameSize" (recommended, **zero file migration**) vs "logical = normalized fraction of frame" (cleaner long-term, **forces a migration**). Determines whether a format bump is needed at all.
7. **Render cache under scale change.** Invalidate-on-change (simple, costs a re-render when toggling) vs scale-keyed multi-entry (more memory, smoother scrubbing). Tie to the proxy UX.
8. **Where the output/render scale originates.** A new `Scene` property vs a `Renderer`/`SceneRenderer` ctor parameter vs per-`EditViewModel` preview setting. Affects cache and nested-renderer plumbing.
9. **Res-sensitive effects contract.** Best-effort approximate vs force-full-scale-render-for-subtree vs warn-the-user, per effect (PixelSort, contour-based Stroke/FlatShadow/PartsSplit, AutoClip, byte-LUT). A product/fidelity decision.
10. **Text hinting.** Keep `Hinting=Full` (sharp per-resolution, preview≠export bit-wise) vs `None`/`Slight` (scale-stable, perceptually equivalent). Defines "looks identical" for text. Also: scale lives on `FormattedText` (public, enters Equals) vs on the op/context (keeps FormattedText logical — preferred).
11. **Where the final normalization attaches.** A new terminal node, a method on `RenderNodeProcessor`, or in `Renderer.RenderDrawable`/`Renderer.Render(CompositionFrame)` — and whether `CompositionFrame` needs both `logicalSize` and `scale`.
12. **Pen.Thickness / Clipping insets unit intent.** Confirm these "logical-but-currently-equals-pixels" values are intended to scale with render scale (they should, for visual equivalence) vs stay constant device-px. Drives the migration story.
13. **Stroke geometry space.** Generate in logical (Pen scale-agnostic, only matrix changes; interacts with the `maxAspect` stroke-splitting loop + TightBounds caching) vs device (bake `s` in).
14. **3D in scope?** Should `Scene3D.RenderWidth/Height` derive from the 2D render scale automatically (in scope) or remain an independent authored resolution (deferred)?
15. **Acceptance metric/tolerance.** Define the per-channel max-delta / PSNR / SSIM threshold for "identical at a different resolution" (exact pixel equality is impossible across resamples), and whether scale=1.0 export must be byte-identical to the pre-feature renderer (gating golden tests).
16. **Supersampling (`s>1`).** Supported for export AA? If so the device buffer is `FrameSize*s` with a downscale to `FrameSize` at encode — reintroduces a final resample for that case.

---

## 11. Suggested phasing / MVP slices

Each slice is independently testable (golden-image: render at scale `s`, upscale, compare to scale-1.0 within tolerance) and delivers a user-visible capability without requiring the whole pipeline at once.

**Slice 0 — Scale plumbing skeleton (no behavior change).**
Add `Scale` to `RenderNodeContext` (default (1,1)); seed from `Renderer`; thread through `Pull`; add read-only `EffectiveScale` on `RenderNodeOperation`. Switch the three `RenderNodeProcessor` rasterization sinks to the existing `PixelRect.FromRect(rect, scale)` overload with `scale=1`. **Acceptance:** scale=1.0 output byte-identical to today (regression guard). This is the atomic foundation; nothing user-visible yet.

**Slice 1 — Whole-frame reduced-scale preview for vector + Skia-filter content (the first shippable, user-visible slice).**
Allocate the root surface at `ceil(FrameSize*s)`, push root `Matrix.CreateScale(s)`, render shapes/geometry/text (re-shaped at scale)/pens/gradients + all Skia-filter-mode effects (Blur, DropShadow, color effects). Make `RenderNodeCache` scale-keyed/invalidated. **User-visible:** "preview at 0.5x" renders faster for vector-heavy scenes. **Testable:** golden parity per built-in effect that is purely Skia-filter or invariant; hit-test parity at two scales.

**Slice 2 — Render-target-mode (CustomEffect) effects.**
Make `FilterEffectActivator.Flush` / `CustomFilterEffectContext.CreateTarget` allocate at `bounds*s` with a scaled CTM; thread scale into `FilterEffectContext` so pixel params (Mosaic tileSize, ColorShift offsets, InnerShadow, Displacement) scale. Add the `scale` uniform to SKSL/GLSL. **Testable:** golden parity per CustomEffect; explicit classification table asserted by tests. Resolves the Skia-filter-vs-render-target asymmetry (Risk #3).

**Slice 3 — Mixed-scale compositing + nested scenes + DrawableBrush.**
Add per-`EffectTarget` scale; implement the `RenderNodeProcessor` normalize-on-composite rule (`max`, capped); make `ImmediateCanvas.DrawSurface/DrawRenderTarget` resample on scale mismatch; propagate scale into nested `SceneDrawable` Renderer and `DrawableBrush` child. **User-visible:** correct compositing of differently-scaled subtrees. **Testable:** scenario-A golden tests (full-res shape over reduced-res nested scene).

**Slice 4 — Proxy media via reduced decode.**
Decouple `SourceImage`/`SourceVideo` logical size from decoded pixel size; make `DrawBitmap` target a logical dest rect; add the additive target-size hint to `MediaOptions` + FFmpeg IPC protocol + worker; key the shared-resource cache by `(URI, scale)`. **User-visible:** the actual proxy workflow — preview uses proxy media, export re-decodes full. **Testable:** proxy vs full produce identical Bounds; export at full re-decodes; IPC contract test.

**Slice 5 — Editor integration & 3D.**
Wire preview render-scale selection in `EditViewModel`/`PlayerViewModel` with renderer/cache rebuild on change; reconcile `FrameCacheConfigScale`; finalize logical hit-test/handle space; scale the `Scene3DRenderNode` sub-render in lockstep. **User-visible:** a preview-quality control. **Testable:** handle-drag parity across scales; 3D sharpness parity.

> Recommended first ship: **Slice 0 + Slice 1.** It is the smallest independently-testable, user-visible increment (reduced-scale preview for the common vector+Skia-filter case), carries the byte-identical-at-1.0 regression guard, and de-risks the cache and hit-test seams before the harder CustomEffect/mixed-scale/proxy work.

---

## 12. Post-review corrections & additions (independent Codex code-verification pass)

An independent pass re-verified the dossier's concrete claims against the code and re-read the draft spec. Most core claims were confirmed (scale-blind `RenderNodeContext`/`RenderNodeOperation`, the rasterization sinks, the *already-existing-but-unused* scale primitives `PixelSize.ToSize(float)` / `PixelRect.FromRect(Rect, float/Vector)`, and the spot-checked effect rows for Blur / DropShadow / Mosaic / ColorShift / FlatShadow / PerlinNoise). The body above is preserved as written; the items below **supersede** it where they conflict.

### 12.1 Factual corrections

- **"The three sinks all use `PixelRect.FromRect(op.Bounds)`" is imprecise.** Only the `RenderNodeProcessor` rasterization sink uses `PixelRect.FromRect`. The filter-effect sinks cast directly to `int`: `FilterEffectActivator.Flush` uses `(int)target.OriginalBounds.Width/Height` (`FilterEffectActivator.cs:29`) and `CustomFilterEffectContext.CreateTarget` uses `(int)bounds.Width/Height` (`CustomFilterEffectContext.cs:52`). These round *differently* from `PixelRect.FromRect` and each need their own migration to the shared rounding helper.
- **`PixelRect.FromRect` does not "truncate".** It floors / toward-zero the origin but **ceils** the bottom-right extent — asymmetric rounding (`PixelPoint.cs:226`, `PixelRect.cs:378,505`). Read every "integer-truncated" phrasing in §1–§2 as "asymmetrically rounded (floor origin, ceil extent)". The FR-007 shared-rounding-helper MUST reproduce this asymmetry at scale 1.0 to stay byte-identical.
- **Scope alignment (proxy decode).** §7 and Slice 4 describe adding a decode-target hint to `MediaOptions` + the FFmpeg IPC protocol / worker. Per the confirmed 003 scope this is **deferred**: 003 delivers only (a) the logical-vs-decoded size decoupling and (b) the `DrawBitmap` logical dest-rect, and keeps `MediaOptions` additively extensible. The IPC / worker decode-hint and the `(URI, scale)` shared-reader re-keying move to the follow-up proxy feature. (`MediaOptions` today carries only `StreamsToLoad` + an obsolete `SampleRate`: `MediaOptions.cs`.)

### 12.2 Missed coupling sites (add to the §2 inventory)

- **Particles — BLOCKER-grade omission.** `ParticleRenderNode` is a fully independent nested-raster path: hard-coded `new PixelSize(1920, 1080)` (`ParticleRenderNode.cs:139`), unscaled `PixelRect.FromRect(bounds)` (`:148`), `RenderTarget.Create` + translate-only render (`:157`). Must honor render scale → spec **FR-029**. Severity **HIGH**.
- **Audio visualizers.** `AudioVisualizerDrawable` and the waveform / spectrum shapes carry pixel-magnitude params and hard-coded minimums (e.g. `BlockWaveformShape.BlockGap` / `BarWidth`, `0.5f` / `1f` floors: `AudioVisualizerDrawable.cs:62`, `BlockWaveformShape.cs:20,51`). Must classify under the scale contract → spec **FR-030**. Severity **MED→HIGH**.
- **3D is a concrete mixed-scale bridge today, not a future question.** `Scene3DRenderNode` resizes `Renderer3D` to authored `RenderWidth/RenderHeight` (int) and emits a surface op with no scale metadata (`Scene3DRenderNode.cs:49,63,102`). It already enters the mixed-scale path → spec **FR-033**.
- **Concurrency / render dispatcher.** Rendering is dispatcher-affine (`Renderer.cs:117` `VerifyAccess`); export frames are produced on a background task and marshaled via `RenderThread.Dispatcher.InvokeAsync` (`FrameProviderImpl.cs:40,60`). Scale change + cache invalidation must be atomic on that dispatcher → spec **FR-031**.
- **Version / source-generator invalidation.** Render nodes compare captured `Resource.Version` (`ResourceExtension.cs:7`, `EngineObject.cs:384`); scale is not a property, so it must be an explicit external invalidation key. Resource update / compare code is source-generated per `IProperty` (`ResourceClassEmitter.cs:188,209`) — scale-dependent generated fields / keys need generator + `tests/SourceGeneratorTest` changes → spec **FR-032**.
- **Shader uniform contract — concrete names.** SKSL exposes `width` / `height` / `iResolution` from `effectTarget.Bounds` (`SKSLScriptEffect.cs:99`); GLSL exposes `Width` / `Height` push constants (`GLSLScriptEffect.cs:91`). Neither has a scale uniform. Keep these device-meaning, ADD a named `iScale` / `uScale`, document that scale-unaware shaders behave as scale 1.0 → spec **FR-014**.
- **Text cache scale-awareness (emphasis).** `FormattedText` memoizes `_textBlob` / paths / `_metrics` keyed on font props only (`FormattedText.cs:26,182`) and `TextRenderNode` reuses them for render + hit-test (`TextRenderNode.cs:30`); non-scale-aware keys silently degrade text at reduced scale → spec **FR-012, FR-032**.

### 12.3 Net effect on the plan

None of the corrections change the architecture or any of the four confirmed decisions. They (1) enlarge the per-effect/property matrix to include **particles** and **audio visualizers**, (2) add **concurrency-atomicity** and an explicit **scale-invalidation key** as first-class requirements, (3) pin the rounding helper to reproduce `PixelRect.FromRect`'s **asymmetry** at scale 1.0, and (4) confirm **3D** enters the mixed-scale path now (lockstep optional). All are reflected in spec **FR-029..FR-033** and the tightened SC / FR wording. `/speckit-plan` MUST treat §5's effect matrix as *incomplete* until particles and audio visualizers are added.
