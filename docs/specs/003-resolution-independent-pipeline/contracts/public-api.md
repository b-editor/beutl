# Contract: Public API breaking surface

**Feature**: 003 | Consumers: `Beutl.NodeGraph`, `Beutl.Extensibility`, out-of-tree plugin drawables/effects, `Beutl.ProjectSystem`, `Beutl` (editor).

This feature ships as a **breaking change**: `refactor!:` / `feat!:` with a `BREAKING CHANGE:` footer naming `Beutl.Engine`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`. **No `[Obsolete]` shims** (AGENTS.md); all in-tree call sites updated in the same change. Route through `beutl-design-reviewer` (FR-028). **No file-format change** (FR-001/SC-002).

## Breaking symbols

| Symbol | File | Today | Proposed | Reason / consumers |
|---|---|---|---|---|
| `RenderNodeContext` ctor | `Rendering/RenderNodeContext.cs:3` | `ctor(RenderNodeOperation[] input)` | `ctor(input, float outputScale = 1f)` + `float OutputScale { get; }` + `static float ResolveWorkingScale(...)` (FR-036) | Sole construction at `RenderNodeProcessor.cs:121`; reaches ~28 `Process` overrides (FR-004) |
| `RenderNodeOperation` | `Rendering/RenderNodeOperation.cs:6-64` | 4 static factories, `Bounds`/`Render`/`HitTest` | + `EffectiveScale EffectiveScale { get; }` (**value type**, default `Unbounded`); factories gain `EffectiveScale effectiveScale = default`. **No `LosslessReRasterizable` bool** — `IsUnbounded` subsumes it | Subclasses + `FilterEffectRenderNode.cs:47,73`, `RenderNodeProcessor.cs:102`, `ParticleRenderNode.cs:83`, `SceneDrawable.cs:185` |
| `EffectiveScale` (new type) | `Rendering/EffectiveScale.cs` | — | **As shipped:** a non-positional `readonly record struct` with a private inverted `_bounded` flag (NOT the positional `(float Value, bool IsUnbounded)` form — that would make `default` wrongly `At(0)`); `Unbounded`/`At(float)`/`IsUnbounded`/`Value`; `default == Unbounded` | FR-018; produced by flushed effect buffers (`At(w)`), `CreateTarget`, 3D surfaces; consumed by `EffectTarget.Draw` + the op factories |
| `ResolutionPolicy` (new type) | `Rendering/ResolutionPolicy.cs` | — | **As shipped:** `readonly record struct ResolutionPolicy(ResolutionPolicyKind Kind, float Factor = 0f)` (Factor defaults `0f`, NOT `1f`, so `default(ResolutionPolicy)` value-equals `Inherit`; only `Oversample(factor)` supplies a Factor); `Inherit`/`ClampToOutput`/`Oversample(k)`/`PreserveSource` | FR-036 |
| `FilterEffect.ResolutionPolicy` | `FilterEffects/FilterEffect.cs` | — | + `virtual ResolutionPolicy ResolutionPolicy => Inherit` | FR-036; quality effects override (default `Inherit`). **`RenderNode.ResolutionPolicy` was NOT added** — it would have been a dead duplicate (no node-level boundary consumes it; `FilterEffectRenderNode` reads the `FilterEffect`'s policy). |
| `RenderNodeProcessor` ctor | `Rendering/RenderNodeProcessor.cs:6` | `ctor(RenderNode root, bool useRenderCache)` | `ctor(root, useRenderCache, float outputScale = 1f)` + `float OutputScale { get; }`; sinks compute `w = ResolveWorkingScale(...)` and use `PixelRect.FromRect(bounds, w)` | `Renderer.cs:174`, `NodeGraphFilterEffectRenderNode.cs:42`, `ParticleRenderNode.cs:144` |
| `Renderer` ctor | `Rendering/Renderer.cs:45` | `ctor(int width, int height)` | `ctor(int width, int height, float renderScale = 1f)` (the output scale `s_out`) + `RenderScale`/`DeviceSize` getters; width/height **logical**, surface `ceil(FrameSize×s_out)` | FR-003/FR-026; `SceneDrawable.cs:181`, `EditViewModel.cs:51`, `OutputViewModel.cs:264` |
| `IRenderer` | `Rendering/IRenderer.cs:7` | — | + `float RenderScale { get; }`, + `PixelSize DeviceSize { get; }` (default-interface-impl → `1f`/`FrameSize` to soften third-party impls, mirroring `GetBoundary` default at `:30`) | third-party `IRenderer` implementers |
| `SceneRenderer` ctor | `ProjectSystem/SceneRenderer.cs:10` | `ctor(Scene scene, bool disableResourceShare = false)` | `ctor(Scene scene, float renderScale = 1f, bool disableResourceShare = false)` | `EditViewModel.cs:51`, `OutputViewModel.cs:264` |
| `GraphicsContext2D` ctor | `Rendering/GraphicsContext2D.cs:9` | `ctor(ContainerRenderNode, PixelSize canvasSize = default)` | + `float outputScale = 1f` + `OutputScale`; `DrawBackdrop` uses `ToSize(outputScale)` | FR-021; **most-consumed changed ctor** — full call-site list below |
| `FilterEffectContext` | `FilterEffects/FilterEffectContext.cs:39` | no scale | + ctor `(outputScale, workingScale)` + `float WorkingScale { get; }` + `float OutputScale { get; }`; pixel primitives × **`WorkingScale`** | FR-009/FR-015; `FilterEffectRenderNode.cs:30` |
| `CustomFilterEffectContext` | `FilterEffects/CustomFilterEffectContext.cs:52` | `CreateTarget` `(int)bounds.W/H` | + `float WorkingScale { get; }`; `CreateTarget` `ceil(bounds×WorkingScale)`; `Open` pre-scaled | FR-009/FR-015; migrate off `(int)` cast (scale-1.0-sensitive) |
| `FilterEffectActivator.Flush` | `FilterEffects/FilterEffectActivator.cs:29` | `(int)OriginalBounds.W/H` | `ceil(×w)` via shared helper + `CreateScale(w)`; normalize divergent `EffectTarget.Scale` to `w` first | FR-009/FR-019; scale-1.0-sensitive |
| `EffectTarget` | `FilterEffects/EffectTarget.cs:6` | `Empty`/`Size` (obsolete) | + `EffectiveScale Scale { get; set; }` (default `Unbounded`); **remove** `Empty`/`Size` | FR-019; LayerEffect mixed-scale detection (dossier §4.5) |
| `SKSLScriptEffect` / `GLSLScriptEffect` | `FilterEffects/SKSLScriptEffect.cs:99` / `GLSLScriptEffect.cs:91` | device-px uniforms | keep device meaning; **add** `iScale` (SKSL) / `Scale`/`uScale` PushConstants (GLSL `:37-43`) | FR-014; see `shader-uniforms.md` |

## Rounding-helper migration (scale-1.0-sensitive)

The main rasterization sink already calls `PixelRect.FromRect(op.Bounds)`; switch to the `(rect, w)` overload — at `w = 1.0` this is identical (byte-safe). The **filter-effect sinks** (`FilterEffectActivator.cs:29`, `CustomFilterEffectContext.cs:52`) do component-wise `(int)Width`/`(int)Height` truncation — a different rounding from `FromRect`'s corner-based result (a 100.7-wide bound → 100 px component-cast vs 101 px `FromRect`). To preserve byte-identity (FR-005, FR-007), these sinks **keep their component-wise `(int)` truncation at `w = 1.0`** and apply `ceil(× w)` only for `w ≠ 1.0`; they are NOT unified with `FromRect` at scale 1. Golden-test (`AssertByteIdentical`) the filter-target paths at `w = 1.0`. Origins round **toward zero** (`(int)` cast), not floor — reproduce exactly (D5/best-practices).

## Complete call-site inventory for the changed ctors

The `float scale = 1f` defaults preserve source compatibility, so every site below recompiles and stays correct at scale 1.0; the task slices must visit each to pass a real scale where the path is scaled (vs. legitimately fixed at 1.0).

- **`new Renderer(...)`** — `SceneDrawable.cs:181` (nested-scene renderer; inherit outer scale, FR-022).
- **`new SceneRenderer(...)`** — `EditViewModel.cs:51` (preview scale, FR-035), `OutputViewModel.cs:264` (export, FR-034).
- **`new GraphicsContext2D(...)`** (all sites) — scaled: `Renderer.cs:169,265` (root, pass `s_out`), `BrushConstructor.cs:233` (DrawableBrush child, inherit the negotiated `w`, FR-022), `ParticleRenderNode.cs:139` (FR-029), `ImmediateCanvas.cs:131` (`DrawNode`, pass active scale), `DrawableTextureSource.cs:64` (3D texture, FR-033), `TextNode.cs:87` (NodeGraph); legitimately fixed at 1.0 (verify, may stay `1f`): `SourceVideo.Thumbnails.cs:140` (thumbnail), `PlayerViewModel.cs:1414` (fixed-size capture), `AvaloniaTypeConverter.cs:274` (fixed 1920×1080 preview). Inventory via `rg "new GraphicsContext2D\("`.

## Migration summary for downstream consumers

- **`Beutl.NodeGraph`**: `NodeGraphFilterEffectRenderNode.cs:42` must pass `context.OutputScale` into the inner `RenderNodeProcessor`; `RenderNodeDrawable.cs:30-40`, `OperationWrapperRenderNode.cs:22`, `Nodes/TextNode.cs` recompile against the new ctors.
- **Out-of-tree plugins** (`tests/PackageSample/SampleDrawable.cs` is the in-repo exemplar): custom `Drawable`/`FilterEffect`/`RenderNode` authors recompile; new optional scale params default to `1f` so a plugin that ignores scale still renders correctly at scale 1.0 but will not benefit from reduced-scale preview until it adopts the [effect scale contract](./effect-scale-contract.md).
- **`Beutl` editor**: `EditViewModel.cs:51` (preview-scale rebuild), `OutputViewModel.cs:73-74,241,264` + `FrameProviderImpl.cs:51` (`SourceSize` from `DeviceSize`, FR-026/FR-034), `PlayerViewModel.cs:1223` (hit-test logical, FR-027).
