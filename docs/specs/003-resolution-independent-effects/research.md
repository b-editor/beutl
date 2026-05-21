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

## R2 — Wrapper naming (collision with existing integer `Beutl.Media` types)

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

**Question**: How are new value types animated through keyframes?

**Finding**: `AnimatorRegistry.CreateAnimator<T>()` (`src/Beutl.Engine/Animation/AnimatorRegistry.cs:46`) returns either a registered explicit `Animator<T>` or the fallback `_Animator<T>` (linear interp via runtime reflection / typed math). For numeric structs we can either:

- (a) Implement arithmetic operators (`+`, `-`, `*` scalar) on the wrapper so `_Animator<T>` works; or
- (b) Register an explicit `Animator<PixelLength>` etc. that delegates to the inner `float` / `Size` / `Point` animator.

**Decision**: (b) — explicit registration via `AnimatorRegistry.RegisterAnimator<PixelLength, PixelLengthAnimator>()`. The wrapper structs stay minimal (no operator overloading), and the animator is one small class per wrapper that unwraps, lerps the inner primitive, and re-wraps.

**Rationale**: Keeps wrappers as nominal types (no surprising arithmetic). The animator code is trivial and tested.

**Alternatives considered**:
- (a) Operator overloading. Rejected — invites accidental arithmetic mixing of `PixelLength` and `float` at call sites, undoing the nominal-type benefit.

## R6 — `StrokeEffect` thickness path

**Question**: `StrokeEffect` uses a `Pen.Resource` (with `Thickness`) for the stroke. `Pen.Thickness` is a `float` shared with all `Pen` consumers (geometries, paths). Should `Pen.Thickness` itself become resolution-independent, or only `StrokeEffect`'s use of it?

**Finding**: `Pen` is used throughout the graphics layer (`DrawGeometry`, `DrawRectangle`, `DrawEllipse`, image / video source decoration, …). Changing `Pen.Thickness` would ripple into Drawables and is a much larger surface.

**Decision**: **Scope `Pen` out of this feature.** `StrokeEffect` adapts at the call site by scaling its own offset (`PixelOffset`) and accepting a `Pen` whose thickness remains raw pixels. The user-facing implication is documented: the stroke's *position* becomes resolution-independent immediately; the stroke's *thickness* requires the user to also adopt a (future) `Pen`-side change.

If pushback is strong during PR review, a follow-up feature can introduce `PixelThickness` on `Pen` — but it is deliberately not bundled here.

**Rationale**: Keeps blast radius bounded. `Pen` resolution-independence is its own design conversation (impacts Drawables, not just FilterEffects). The spec's StrokeEffect acceptance scenario (US1 #3) speaks of "covers the same proportion of the underlying shape" — that includes thickness, so this is a known partial-coverage gap; tasks.md will call it out as a follow-up.

**Alternatives considered**:
- *Bundle `Pen` change.* Rejected — too large and orthogonal. Splitting matches the "orthogonality first" design priority.
- *Drop `StrokeEffect` from scope entirely.* Rejected — the position fix is still valuable and isolated.

## R7 — Definitive in-scope effect list

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
