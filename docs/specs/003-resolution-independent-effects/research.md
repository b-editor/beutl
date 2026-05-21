# Phase 0 Research: Resolution-Independent Pixel-Absolute Effects

This document resolves the unknowns called out in `plan.md` § Phase 0 before any code is written.

## R1 — Existing "proxy preview" infrastructure

**Question**: Does Beutl currently have a proxy-preview rendering mode separate from export?

**Finding**: No. The render pipeline always materializes at the scene's full `FrameSize`.

- `SceneRenderer(Scene scene, ...)` (`src/Beutl.ProjectSystem/SceneRenderer.cs:10`) calls `: base(scene.FrameSize.Width, scene.FrameSize.Height)`.
- `Renderer(int width, int height)` (`src/Beutl.Engine/Graphics/Rendering/Renderer.cs:45-58`) stores `FrameSize = new PixelSize(width, height)` and allocates the backing `RenderTarget` at exactly that size.
- The editor's `IPreviewPlayer` (`src/Beutl.Editor/Services/IPreviewPlayer.cs`) only exposes the already-rendered `Bitmap`; it does not configure raster size.

**Decision**: The feature ships only the **plumbing** for resolution-independence — a `RenderScale` carried by the rendering context plus typed wrappers on effect parameters. Today every renderer is constructed with `RenderScale = 1.0`, so the new code is exercised but does not change behavior. Future work can add a proxy-preview UX by constructing the renderer with a smaller target while keeping the same `ReferenceFrame`.

**Rationale**: Decoupling the two changes keeps each piece reviewable. Conflating them would mix UI work into an engine refactor and balloon the PR.

**Alternatives considered**:
- *Build proxy preview now too.* Rejected — out of scope per the spec's wording ("一致させる" is about correctness of the math, not adding a preview workflow). Doubles the surface area.
- *Defer this work until proxy preview is designed.* Rejected — the engine fix unblocks the UX work, and is itself independently testable (a unit-test can construct a `Renderer` with a smaller target than `ReferenceFrame` and assert visual equivalence).

## R2 — Helper-internal scaling vs. typed parameter wrappers

> **Update (post-T001-audit design pivot)**: The original analysis below evaluated three naming families for new wrapper types (`PixelLength` / `PixelExtent` / `PixelOffset`). That whole approach has been **abandoned in favour of a simpler design**: scaling is applied implicitly inside the existing `FilterEffectContext` helper methods (`Blur(Size)`, `DropShadow(Point, Size, Color)`, …), with a `*Raw` variant of each helper as an opt-out. No new wrapper types, no per-effect property migration, no new animators, no property-editor changes. The discussion below is retained for historical context.
>
> **Decision (current)**: scaling lives inside `FilterEffectContext` helpers. Each helper multiplies its length-typed argument by `RenderScale` before forwarding to the underlying Skia builder. `*Raw` variants (`BlurRaw(Size)`, `DropShadowRaw(Point, Size, Color)`, …) skip the multiplication and pass values through verbatim. Effect property declarations are untouched.
>
> **Why this design wins**:
> - **Zero churn at the EngineObject layer.** No `IProperty<T>` change, no animator registration, no property-editor work, no project-file migration concern.
> - **Universal coverage for free.** Every existing built-in effect *and* every third-party plugin that calls a standard helper automatically becomes resolution-independent — no plugin author has to do anything.
> - **Opt-out is explicit, opt-in is implicit.** The 99% case (just call `Blur(sigma)` and get resolution-independent behavior) needs no syntax; the 1% case (raw-raster-pixel semantics) is one method-name change to `BlurRaw`.
> - **Smaller surface, smaller blast radius.** The scaling logic lives in ~one file (`FilterEffectContext.cs`) with one tested rule, instead of being distributed across 3 wrapper structs + 3 animators + 3 editor viewmodels + 1 service registration + 13 per-effect call sites.
>
> **Cost accepted**: A casual reader of `Blur.cs` does not see "this is resolution-independent" in the type. Discoverability of the contract relies on `FilterEffectContext` documentation. We mitigate by documenting the rule prominently on every scaled helper's XML doc and on the plugin-author migration guide.
>
> The remainder of this section preserves the original wrapper-naming research for reference.

### Historical: wrapper naming (collision with existing integer `Beutl.Media` types)

**Question**: `Beutl.Media.PixelSize` (integer raster width × height) and `Beutl.Media.PixelPoint` (integer raster coordinate) already exist and are used inside `Beutl.Graphics.FilterEffects/` (e.g. `ContourTracer.cs`, `PartsSplitEffect.cs`). New wrappers called `PixelSize` / `PixelPoint` in `Beutl.Graphics` would collide. What names should the new wrappers take?

**Finding** — three families considered:

1. *Natural names + sub-namespace* (`Beutl.Graphics.Units.PixelSize` / `PixelPoint` / `PixelLength`). Files that import both `Beutl.Media` and `Beutl.Graphics.Units` need an alias on every using site — constant nuisance.
2. *Suffix kludges* (`PixelSize2D`, `PixelPointF`, etc.). Inconsistent across the family (`PixelLength` has no suffix) and ugly.
3. *Distinct geometric nouns* (`PixelLength`, `PixelExtent`, `PixelOffset`). One word per concept, all start with `Pixel`, none collide with `Beutl.Media.*`.

**Decision**: Use **family (3)** — three nouns, each describing the geometric role of the value:

- `PixelLength` — **single-axis length** in reference-frame pixels. Used for things like a dilation radius or a flat-shadow length.
- `PixelExtent` — **2-D anisotropic extent** (`Width` × `Height`) in reference-frame pixels. Used for symmetric "spread" quantities like blur sigma or mosaic tile size.
- `PixelOffset` — **2-D directional offset** (`X`, `Y`) in reference-frame pixels. Used for positional translations like a drop-shadow's offset from origin.

**Rationale**:

- Each suffix is a real English geometric noun that matches the parameter's role. Readers do not have to think about `2D` formal suffixes.
- `Pixel` prefix keeps the family discoverable (IntelliSense lists `Pixel*` together) and signals "this value is measured in pixel units".
- No collision with `Beutl.Media.PixelSize` / `PixelPoint` — none of `Length` / `Extent` / `Offset` are taken in `Beutl.Media`.
- No need for sub-namespaces or aliasing in effect files — they just add a `using Beutl.Graphics;` (already present) and reference the names directly.

**Property naming inside the wrappers**:

- `PixelLength.Value` — the raw `float` in reference-frame pixels.
- `PixelExtent.Width` / `.Height` — same axis names as `Beutl.Graphics.Size` it replaces.
- `PixelOffset.X` / `.Y` — same axis names as `Beutl.Graphics.Point` it replaces.

So a `.scene` file that previously serialized `"sigma": { "width": 20, "height": 20 }` to a `Size` keeps the exact same JSON shape when the property type changes to `PixelExtent`.

**Alternatives considered**:

- *Rename `Beutl.Media.PixelSize` / `PixelPoint`*: rejected — load-bearing through encoder settings, decoders, source-image / source-video; cost ≫ benefit.
- *Sub-namespace `Beutl.Graphics.Units`*: rejected — using-statement ambiguity inside every effect file that also uses `Beutl.Media`.
- *Suffix kludges `PixelSize2D` / `PixelXY`*: rejected — inconsistent family, formal-suffix ugly.
- *Drop `Pixel` prefix entirely* (`ReferenceLength` / `ReferenceSize` / `ReferencePoint`): considered. "Reference" is internally consistent with `IRenderer.ReferenceFrame` but the word is overloaded (C# `ref`, object references) and reads worse in property declarations. The geometric-noun family won on readability.
- *Borrow `Dip` (device-independent pixel)*: rejected — semantic mismatch (`Dip` traditionally means DPI-independent, not resolution-reference-independent) and Avalonia/WPF DIP precedent could mislead.

**Open**: none. Names pinned in `data-model.md` and `contracts/parameter-wrappers.md`.

## R3 — Nested-composition reference-frame propagation

**Question**: When `SceneDrawable.Render` enters a sub-scene, how do effects inside that sub-scene see the sub-scene's `FrameSize` as their reference frame (per FR-010)?

**Finding**: `GraphicsContext2D` already exposes a `Size` property and supports a `Push` / `Pop` discipline (`PushTransform`, `PushClip`, `PushLayer`, …). It does **not** currently propagate any "reference frame" concept.

**Decision**: Add `RenderScale` and `ReferenceFrame` to `GraphicsContext2D` via the same stack discipline:

- New `PushedState PushReferenceFrame(PixelSize referenceFrame)` API on `GraphicsContext2D` that swaps the current `(ReferenceFrame, RenderScale)` pair, returning a `PushedState` whose `Dispose` restores the previous value.
- `SceneDrawable.Render` wraps its draw call in `using (ctx.PushReferenceFrame(r.ReferencedScene.FrameSize)) { ... }`.
- `LayerEffect` (and any container that renders a sub-context) follows the same pattern.
- `FilterEffectContext` snapshots the current `(ReferenceFrame, RenderScale)` at construction time (in `GraphicsContext2D.PushNode(FilterEffectActivator…)` paths), so an effect's `ApplyTo` reads a stable value even if the outer context pushes/pops further.

**Rationale**: Matches the existing push/pop idiom — no new bookkeeping concept. Composable with `LayerEffect`. Snapshotting into `FilterEffectContext` avoids surprises if the outer code mutates the stack mid-apply.

**Alternatives considered**:
- *Thread the reference frame through every `ApplyTo` argument.* Rejected — every plugin author would have to update their signature. The push/pop approach keeps `ApplyTo(FilterEffectContext, Resource)` stable.
- *Carry the reference frame only on `Renderer` and look it up via ambient state.* Rejected — does not handle nested scenes correctly.

## R4 — Source-generator impact

**Question**: `Beutl.Engine.SourceGenerators` produces a lot of property / type registration code. Do new property types (`PixelLength`, `PixelExtent`, `PixelOffset`) need generator changes?

**Finding**: The generators iterate over `CoreProperty<T>` declarations. They are type-agnostic for `T` provided `T` plays nicely with serialization and animation. Spot-checks of `AbstractCoreObjectGenerator` and the related templates confirm no per-type switch on the property's value type.

**Decision**: No source-generator changes are anticipated. The new wrapper types must implement `IEquatable<T>` so the generator-emitted `INotifyPropertyChanged`-like change detection keeps working, plus standard `IFormattable` / `ISpanFormattable` for diagnostics. If `tests/SourceGeneratorTest` reveals an issue, the generator gets a follow-up patch — but the plan does not assume one.

**Rationale**: Lowest-risk path. The generators stay stable; the wrappers conform to existing implicit contracts.

**Alternatives considered**:
- *Add a generator hook that recognizes `Pixel*` types and emits scale-resolving accessors.* Rejected as premature — the runtime resolution at `FilterEffectContext` apply time is already simple two-multiplies-per-axis and does not need codegen.

## R5 — Animator registration

> **Update (post-T001-audit design pivot)**: With no new wrapper types, no new animators are needed. The existing `Size` / `Point` / `float` animators continue to drive the unchanged property declarations. **R5 is moot under the current design.** Retained for historical context.

**Question**: How are new value types animated through keyframes?

**Finding**: `AnimatorRegistry.CreateAnimator<T>()` (`src/Beutl.Engine/Animation/AnimatorRegistry.cs:46`) returns either a registered explicit `Animator<T>` or the fallback `_Animator<T>` (linear interp via runtime reflection / typed math). For numeric structs we can either:

- (a) Implement arithmetic operators (`+`, `-`, `*` scalar) on the wrapper so `_Animator<T>` works; or
- (b) Register an explicit `Animator<PixelLength>` etc. that delegates to the inner `float` / `Size` / `Point` animator.

**Decision**: (b) — explicit registration via `AnimatorRegistry.RegisterAnimator<PixelLength, PixelLengthAnimator>()`. The wrapper structs stay minimal (no operator overloading), and the animator is one small class per wrapper that unwraps, lerps the inner primitive, and re-wraps.

**Rationale**: Keeps wrappers as nominal types (no surprising arithmetic). The animator code is trivial and tested.

**Alternatives considered**:
- (a) Operator overloading. Rejected — invites accidental arithmetic mixing of `PixelLength` and `float` at call sites, undoing the nominal-type benefit.

## R6 — `StrokeEffect` thickness path

> **Update (post-T001-audit design pivot)**: With scaling living inside `FilterEffectContext` helpers, the question becomes "does the helper that `StrokeEffect` calls scale the `Pen` thickness?" The answer is: only if `StrokeEffect`'s code path runs the thickness through a scaling helper. Today it draws via `Canvas` / `Pen`, not through a `FilterEffectContext.Stroke*` helper. So thickness still stays raw-pixel in this PR — same conclusion as the original analysis, different mechanism.

**Question**: `StrokeEffect` uses a `Pen.Resource` (with `Thickness`) for the stroke. `Pen.Thickness` is a `float` shared with all `Pen` consumers (geometries, paths). Should `Pen.Thickness` itself become resolution-independent, or only `StrokeEffect`'s use of it?

**Finding**: `Pen` is used throughout the graphics layer (`DrawGeometry`, `DrawRectangle`, `DrawEllipse`, image / video source decoration, …). Changing `Pen.Thickness` would ripple into Drawables and is a much larger surface.

**Decision**: **Scope `Pen` out of this feature.** `StrokeEffect` adapts at the call site by scaling its own offset (`PixelOffset`) and accepting a `Pen` whose thickness remains raw pixels. The user-facing implication is documented: the stroke's *position* becomes resolution-independent immediately; the stroke's *thickness* requires the user to also adopt a (future) `Pen`-side change.

If pushback is strong during PR review, a follow-up feature can introduce `PixelThickness` on `Pen` — but it is deliberately not bundled here.

**Rationale**: Keeps blast radius bounded. `Pen` resolution-independence is its own design conversation (impacts Drawables, not just FilterEffects). The spec's StrokeEffect acceptance scenario (US1 #3) speaks of "covers the same proportion of the underlying shape" — that includes thickness, so this is a known partial-coverage gap; tasks.md will call it out as a follow-up.

**Alternatives considered**:
- *Bundle `Pen` change.* Rejected — too large and orthogonal. Splitting matches the "orthogonality first" design priority.
- *Drop `StrokeEffect` from scope entirely.* Rejected — the position fix is still valuable and isolated.

## R7 — Definitive in-scope effect list

> **Update (post-T001-audit design pivot)**: Under the helper-internal-scaling design, "in scope" no longer means "needs a property-type migration" — it means "the helpers this effect calls will silently scale length arguments". The audit-corrected list of 13 effects (per `data-model.md` § "In-scope built-in effect migrations") is still the reference set, but the per-effect change is **zero source modification**. What we instead need to enumerate is the set of `FilterEffectContext` helpers that those 13 effects call (`Blur`, `DropShadow(Only)`, `InnerShadow(Only)`, `Erode`, `Dilate`, …) — that is the surface to which we add `RenderScale` multiplication and `*Raw` counterparts. Original audit table below is retained.

**Question**: Which built-in effects under `src/Beutl.Engine/Graphics/FilterEffects/` have pixel-absolute parameters?

**Finding** (audit walk):

| Effect | Pixel-absolute parameters | In scope |
|---|---|---|
| `Blur` | `Sigma: Size` | YES |
| `DropShadow` | `Position: Point`, `Sigma: Size` | YES |
| `InnerShadow` | `Position: Point`, `Sigma: Size` | YES |
| `StrokeEffect` | `Offset: Point` (thickness via `Pen` — see R6) | YES (offset only) |
| `Erode` | `RadiusX: float`, `RadiusY: float` | YES |
| `Dilate` | `RadiusX: float`, `RadiusY: float` | YES |
| `FlatShadow` | `Length: float`, `Angle: float` (angle is dimensionless) | YES (length only) |
| `ColorShift` | per-channel `Offset` | YES |
| `DisplacementMapTransform` | `X / Y / CenterX / CenterY: float` | YES |
| `MosaicEffect` | tile-size length | YES |
| `ShakeEffect` | amplitude length | YES |
| `SplitEffect` | `HorizontalSpacing`, `VerticalSpacing` | YES |
| `PartsSplitEffect` | spacing | YES |
| `Brightness`, `Saturate`, `HueRotate`, `Gamma`, `Invert`, `Threshold`, `Negaposi`, `ColorGrading`, `HighContrast` | dimensionless amounts | NO |
| `ColorKey`, `ChromaKey`, `LutEffect` | colors / ratios | NO |
| `DelayAnimationEffect`, `PathFollowEffect` | time / path units | NO (different unit system) |
| `PixelSortEffect` | thresholds | NO (dimensionless) |
| `TransformEffect` | `Matrix` (with pixel translations) | YES — translation component only |
| `DisplacementMapEffect`, `LutEffect` | TBD per audit | revisit during `data-model.md` |
| `Clipping` | likely pixel `Rect` | YES |
| `CustomFilterEffectContext`, `FallbackFilterEffect`, `LayerEffect` | infrastructure, not user effects | N/A |
| `SKSLScriptEffect`, `GLSLScriptEffect`, `CSharpScriptEffect` | scripted; opt-in through wrappers | follow plugin contract |

**Decision**: The "YES" rows above are the baseline `tasks.md` migration list. Final list and per-effect parameter inventory live in `data-model.md`. Each migrated effect gets at least one ResolutionEquivalence test in `tests/Beutl.UnitTests`.

**Rationale**: This audit anchors the FR-002 scope so it is testable rather than aspirational.

**Alternatives considered**:
- *Migrate all effects in one pass without an audit.* Rejected — dimensionless effects (ColorGrading, Saturate, etc.) would get pointless churn and risk regressions.

## R8 — Scope expansion to non-FilterEffect rendering primitives

**Question**: Does the resolution-independent contract live solely on `FilterEffectContext`, or must it extend to other rendering API surfaces (`GraphicsContext2D` direct draw, `Pen.Thickness`, `Transform.CreateMatrix` translation, `Shape` width/height)?

**Finding**: Without extending the contract, the user-visible proxy-vs-export equivalence promised by US1 fails the moment a project uses anything other than filter effects. A `RectShape` of "200 px wide" draws as 200 raster pixels regardless of `RenderScale`, so on a 1/4 proxy it occupies 200 / 480 = 41% of the frame, while at export resolution the same project draws it at 200 / 1920 = 10% — completely different proportions.

**Decision**: Extend the same helper-internal-scaling design pattern to:

1. **`GraphicsContext2D` direct API** — every length-taking method (`DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushTransform(Matrix)` translation column, `PushTransform(Transform.Resource)` via materialized matrix, `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(..., Rect bounds, ...)`) multiplies internally; pair each with a `*Raw` twin.

2. **`Pen`** — scale `Thickness`, `DashOffset`, `Offset` at the consumption sites in the rendering pipeline (`ImmediateCanvas`, `PenHelper.GetRealThickness`, `Shape.GetRealThickness`, `StrokeEffect`). A shared `PenHelper.GetScaledThickness(pen, renderScale)` provides one place that knows the rule. Existing `pen.Thickness` reads in non-rendering code paths stay unchanged. `MiterLimit`, `TrimStart`, `TrimEnd`, `TrimOffset` stay raw.

3. **Transform path (revised by R10 below)** — `Transform.CreateMatrix(CompositionContext)` returns the authoring-space matrix unchanged from pre-feature. The translation column is scaled later, at render-node application time, inside `ImmediateCanvas.PushTransform(matrix, op, isRaw)`. `TransformRenderNode` gains an `IsRaw` flag for opt-out. `CompositionContext` is **not** extended. The original draft of this R8 section proposed scaling inside `CreateMatrix`; that was abandoned by R10 — see § R10 for rationale.

4. **`Shape` subclasses** — no code change needed. `RectShape.Width / Height`, `EllipseShape.Width / Height`, `RoundedRectShape.Width / Height / Smoothing / CornerRadius` flow into `DrawRectangle` / `DrawEllipse` and benefit automatically once those scale.

**Out-of-scope (deferred follow-ups)**:

- **`Geometry` path coordinates** — path data flows through `Geometry.Resource` into Skia paths; scaling requires either rewriting paths at materialization (expensive, breaks identity sharing) or wrapping every `DrawGeometry` / `PushClip(Geometry)` in an implicit `PushTransform(scale)` (subtle interaction with explicit transforms). Treated as a separate feature.
- **`TextBlock.Size / Spacing`** — font size scaling touches the typeface materialization path and glyph caching. Typography rendering has its own non-trivial interaction with DPI, hinting, and subpixel positioning. Treated as a separate feature.
- **`Brush` rectangles** — `TileBrush` source rect, `ImageBrush.SourceRect / DestinationRect`. These flow through `Brush.Resource` materialization and brush-application paths. Treated as a separate feature.

**Rationale**: The helper-internal-scaling pattern is already proven for FilterEffects. Extending it horizontally (more API surfaces) is mechanically the same change at each surface. The cost of including Shapes / Transforms / Pen / direct-draw helpers in this PR is moderate; the cost of *excluding* them is that US1 fails for any non-effect project. Geometry / Text / Brush have separate materialization paths whose design needs its own analysis; deferring them keeps this PR tractable while still delivering the bulk of the user-visible win.

**Alternatives considered**:

- *Restrict to FilterEffects only.* Rejected — fails US1 end-to-end. Users would observe broken parity the moment a project uses a transform.
- *All-in including Geometry / Text / Brush.* Rejected — Geometry and Text touch much larger materialization paths and risk regressions in typography rendering. Better to land in stages.
- *Scale at the SKCanvas level (the bottom of Skia)* — would require us to instrument every Skia call site or wrap `SKCanvas`. Rejected — too invasive and would conflict with the existing `*Raw` opt-out (we'd lose the per-helper bypass).

## R9 — Pen scaling implementation strategy

**Question**: `Pen.Thickness` is read at multiple consumption sites (`ImmediateCanvas.DrawEllipse`, `DrawRectangle`, `DrawGeometry`, `DrawText`; `PenHelper.GetRealThickness`; `Shape.GetRealThickness`; `StrokeEffect`). Where does the scaling happen?

**Finding** — three options:

- **(a) At materialization** — modify `Pen.Resource.Update` to capture `Thickness * renderScale`. Requires `CompositionContext` to expose `RenderScale`. Single point of scaling. **But**: `Pen.Resource.Thickness` then holds the *scaled* value, which is surprising for code that reads it for non-rendering reasons (e.g. computing bounding boxes). Bounds calculations would silently use scaled thickness.
- **(b) At every consumption site** — every `pen.Thickness` read in a rendering call path multiplies by the active `RenderScale`. Distributes the change across ~7 files. Fragile to future Pen consumers forgetting to scale.
- **(c) Via a shared helper** — add `PenHelper.GetScaledThickness(pen, renderScale)` and rewrite every consumption site to call it. Mixes (a)'s single-rule-in-one-place with (b)'s opt-in clarity. Future Pen consumers see the helper in code review.

**Decision**: (c) — shared helper, opt-in at each consumption site. Add `PenHelper.GetScaledThickness(Pen.Resource pen, RenderScale scale)` returning `pen.Thickness * scale.ApplyUniform(1)` (uniform scale since stroke thickness is a single number, not anisotropic). For Pen consumers that explicitly want raw thickness (e.g. bounding-box calculation that happens at the *project* level and should not include scaling), they call `pen.Thickness` directly. Document the rule on `Pen.Resource.Thickness` XML doc.

For `DashOffset` and `Offset`, mirror with `PenHelper.GetScaledDashOffset(...)` and `PenHelper.GetScaledOffset(...)`.

**Rationale**: (a) is leaky — `pen.Thickness` reading semantically shifts. (b) is fragile. (c) is the orthogonality-respecting middle ground: data stays raw on the resource, the rendering pipeline applies scaling at known boundaries via a single helper.

**Alternatives considered**:

- *Mutate `Pen.Resource.Thickness` to be a method that takes `RenderScale`.* Rejected — would propagate signature change to every Pen consumer.
- *Introduce a `ScaledPen.Resource` wrapper at consumption.* Rejected — yet another type to track; helper is simpler.

## R10 — Transform scaling: at CreateMatrix vs at PushTransform vs at render-node application

**Question**: `Transform.CreateMatrix(CompositionContext)` materializes a `Matrix`. Where should the translation column be scaled?

**Finding** — three options:

- **(a) Inside `CreateMatrix`** — each concrete Transform subclass produces a pre-scaled Matrix. `CompositionContext` carries `RenderScale`.
- **(b) Inside `GraphicsContext2D.PushTransform(Transform.Resource)`** — at the API consume site, read `transform.Matrix`, multiply translation column by `RenderScale`, push. `Transform.Resource.Matrix` stays unscaled.
- **(c) Inside `ImmediateCanvas.PushTransform` (render-node application)** — `TransformRenderNode` stores the project-space matrix verbatim; when the node is processed and the matrix is actually pushed to the underlying `SKCanvas`, `ImmediateCanvas.PushTransform` multiplies the translation column by its own `RenderScale`. `Transform.Resource.Matrix` and `TransformRenderNode.Transform` are both authoring-space; only the final SKCanvas push is scaled.

**Decision (revised 2026-05-21 after design review)**: **(c)** — at `ImmediateCanvas` / render-node application time.

**Reasons for (c) over (a) and (b)**:

- **`CompositionContext` stays unchanged.** No new property, no plumbing through `SceneCompositor` / `SceneRenderer`. Lower blast radius and avoids the propagation gap the design review flagged (`CompositionContext.RenderScale` was specified to come from "the active scene's `(FrameSize, ReferenceFrame)` pair" but the precise chain from `PushReferenceFrame` to `CompositionContext` was never closed — see `render-scale.md` propagation update).
- **Custom Transform subclasses automatically benefit.** Third-party plugins that override `CreateMatrix` no longer need to remember to multiply by `context.RenderScale` themselves — they just return a project-space matrix and the rendering pipeline scales at the end. This resolves the inconsistency the design review flagged in the plugin migration contract.
- **`Transform.Resource.Matrix` is semantically clean.** Bounding-box computation, animation evaluation, and any other non-rendering consumer reads the authored matrix verbatim. The "raw-Pen-thickness for bounds" pattern from R9 carries through symmetrically to Transform.
- **`TransformRenderNode` caches survive `RenderScale` changes.** Today nothing changes `RenderScale` at runtime, but when proxy preview ships and the user toggles proxy resolution, render-node caches stay valid — they're authoring-space.
- **`PushTransform(Matrix)` and `PushTransform(Transform.Resource)` become symmetric** at the render-application boundary: both record an unscaled `TransformRenderNode` at the `GraphicsContext2D` layer; both are scaled at `ImmediateCanvas.PushTransform` time. No "one overload scales at API time, the other doesn't" asymmetry.

**Where the scaling literally lives**: `ImmediateCanvas.PushTransform(Matrix matrix, …)` (the lowest-level matrix-push that talks to `SKCanvas`). The translation column is multiplied by `this.RenderScale` before the underlying `SKCanvas.SetMatrix` / `Concat` call. `ImmediateCanvas` exposes `RenderScale` as a property mirrored from the constructing `Renderer`.

**API-layer `GraphicsContext2D` consequences**:

- `GraphicsContext2D.PushTransform(Matrix matrix, …)` records `matrix` **verbatim** into `TransformRenderNode`. **No API-time scaling for the Transform path.** This is the one exception to "scale at the helper API call site" — the Transform-specific behavior the user requested.
- `GraphicsContext2D.PushTransform(Transform.Resource transform, …)` similarly records `transform.Matrix` verbatim.
- `GraphicsContext2D.PushTransformRaw(Matrix matrix, …)` / `PushTransformRaw(Transform.Resource, …)` records into a `TransformRenderNode` flagged "raw"; `ImmediateCanvas.PushTransform` consults the flag and skips scaling for raw nodes.
- Other `GraphicsContext2D` helpers (`DrawRectangle(Rect)`, `PushClip(Rect)`, …) continue to scale at API time — they're not on the Transform path and don't share the caching / consumer-flexibility benefit.

**`*Raw` opt-out for Transform**: implemented as a flag on `TransformRenderNode` ("don't scale at application time"). `GraphicsContext2D.PushTransformRaw(Matrix)` constructs a raw node; `ImmediateCanvas` bypasses scaling. Realistic use cases for raw transforms exist when a caller has already authored a matrix in raw-raster coordinates (e.g. snap-to-pixel-grid effects) and wants the result preserved.

**Alternatives considered**:

- *(a) Materialize-time scaling in `CreateMatrix`.* Rejected after review:
  - Required `CompositionContext.RenderScale` plumbing whose propagation chain from `PushReferenceFrame` to `CompositionContext` was never closed.
  - Bounding-box computation and other non-rendering consumers of `Transform.Resource.Matrix` would silently observe scaled values.
  - Third-party `Transform` subclasses would have to opt in manually by editing their `CreateMatrix` overrides — surface-by-surface inconsistency with the rest of the plugin contract.
- *(b) `GraphicsContext2D.PushTransform(Transform.Resource)` consume-site scaling.* Rejected:
  - Asymmetric with `PushTransform(Matrix)` (which would either also scale at API time, double-scaling problems, or not scale, divergence problems).
  - Render-node caches would contain pre-scaled matrices and need invalidation on `RenderScale` change.
