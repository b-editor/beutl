# Quickstart: Resolution-Independent Rendering Pipeline

**Feature**: 003 | Audience: a developer implementing or validating the feature. Pairs with [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/).

## The mental model in one paragraph

There is no scale in the 2D pipeline today: `1 logical unit == 1 device pixel` is hard-wired as `ToSize(1)` / `(int)bounds.Width` / `PixelRect.FromRect(bounds)`. This feature is **supply-driven** (three scales): the render request carries an **output scale `s_out`** (the *final* target only — `RenderNodeContext.OutputScale`); each operation carries an **`EffectiveScale`** = the density its pixels exist at (vector = `Unbounded`); each effect computes a **working scale `w` = `ResolveWorkingScale(inputs, s_out, policy)`** (default `Inherit` → `w` = the input's density: a 0.5 proxy stays 0.5, a 2.0 source stays 2.0; `s_out` is **not** a clamp) and multiplies its spatial-length parameters by `w`. The root surface is `ceil(FrameSize × s_out)` with one `Matrix.CreateScale(s_out)`; an op whose `e ≠ s_out` is resampled once at the final-stage blit; a global ceiling bounds preview memory. Preview uses `s_out ≤ 1` (Full/Half/Quarter/Fit); export uses `1.0` or `s_out > 1` supersampling. At `s_out = 1.0` vector / Skia-filter / unscaled-bitmap content is byte-identical to today; *(2026-06-08 amendment)* a transform re-scales a bitmap's density and a scaled bitmap into an effect is intentionally not byte-identical (coherent density model, FR-019).

## Validate it (the acceptance loop)

All pixel tests are **Vulkan-gated** (`VulkanTestEnvironment.EnsureAvailable()` → `Assert.Ignore` on GPU-less CI), like the existing `ImmediateCanvasVulkanTests`. SC-008 (no `ToSize(1)`) runs without a GPU.

```bash
# byte-equality + SSIM golden tests (need a GPU; skipped on CI agents without one)
dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~Rendering.Golden

# migration-completeness search test (always runs, no GPU)
dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~NoPixelCouplingOnRenderPathTest

# regenerate golden baselines after an INTENTIONAL change (writes .bin instead of asserting)
BEUTL_GOLDEN_UPDATE=1 dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~Rendering.Golden

# benchmark (explicit; ratio gate, not in default CI)
dotnet test Beutl.slnx -f net10.0 --filter "Category=Benchmark"
```

### The four gates (pinned numbers — see research.md D5)

| Gate | What | Threshold |
|---|---|---|
| Byte-equality (SC-001/SC-005) | raw RgbaF16 `Snapshot()` vs pre-feature `.bin` baseline at `s=1.0` | zero epsilon (origins **toward-zero**, extents **ceil**) |
| Reduced-scale exact (SC-004) | render `s=0.5`, Mitchell-upscale, vs `s=1.0` | SSIM ≥ **0.985** AND MAE ≤ **0.02** |
| Mixed-scale (SC-005) | full-res over reduced-res nested vs full reference | SSIM ≥ 0.985, MAE ≤ 0.02, seam ≤ **0.05** |
| Supersample (SC-009) | `s ∈ {2.0}` (also 1.5/4.0), downscaled to FrameSize | size == `ceil(FrameSize)` exact; lower aliasing energy AND `SSIM(s≥2) − SSIM(s=1) ≥ 0.01` |

### Harness usage sketch

```csharp
[Test]
public void Blur_is_scale_exact_at_half()
{
    var full   = RenderAtScale(scene, time, 1.0f);
    var reduced= RenderAtScale(scene, time, 0.5f);          // device surface = ceil(FrameSize*0.5)
    var up     = MitchellResampleTo(reduced, full.Width, full.Height);
    AssertReducedScaleExact(full, up, GoldenThresholds.Default); // SSIM>=0.985 && MAE<=0.02
}

[Test]
public void Scene_is_byte_identical_at_scale_one()
{
    var frame = RenderAtScale(representativeScene, time, 1.0f);
    AssertByteIdentical(frame, "Representative/scale1.0");     // zero-epsilon RgbaF16 SequenceEqual
}
```

## Use it (preview scale, editor)

Preview scale is per-edit-view, non-persisted (FR-035): `EditViewModel.PreviewScale` (`ReactivePropertySlim<RenderScale>`, default `Full`). Changing it rebuilds the `SceneRenderer` + `FrameCacheManager` on the render dispatcher (atomic; D4) and repaints. It never enters `SaveState`/`RestoreState` — reload always returns to Full.

Export supersampling (FR-034): `OutputViewModel` passes a supersample factor (`1.0` default, `2.0` for AA) into `new SceneRenderer(Model, factor, disableResourceShare:true)`; `FrameProviderImpl.RenderCore` Mitchell-downscales to `FrameSize` when `RenderScale > 1` and asserts size before encode.

## Implementation slice order (independently testable; see plan.md)

1. **Slice 0 — plumbing skeleton** (no behavior change): add `OutputScale` + `ResolveWorkingScale`/`ResolutionPolicy`/`EffectiveScale` (value type, default `Unbounded`) to `RenderNodeContext`/`RenderNodeOperation`, `OutputScale` to `RenderNodeProcessor`, `RenderScale` to `Renderer`; switch the 3 sinks to `PixelRect.FromRect(rect, w)` with `w=1`. **Gate: byte-identical at 1.0** + the golden harness lands here.
2. **Slice 1 — reduced-scale preview for vector + Skia-filter** (root surface `ceil(FrameSize×s_out)`, root `CreateScale(s_out)`, re-shaped text, scale-keyed `RenderNodeCache`). First user-visible slice.
3. **Slice 2 — render-target (Custom) effects**: `FilterEffectActivator`/`CustomFilterEffectContext` allocate `ceil(bounds×w)`; `FilterEffectContext` primitives × `WorkingScale`; SKSL/GLSL `iScale`/`uScale` = `w`; per-effect `ResolutionPolicy` honored.
4. **Slice 3 — mixed-scale compositing + nested scenes + DrawableBrush + particles + audio visualizers + 3D-as-bitmap**: per-`EffectTarget` scale; `ImmediateCanvas.DrawSurface`/`DrawRenderTarget` Mitchell-resample on mismatch.
5. **Slice 4 — media decoupling (proxy foundation)**: `SourceImage`/`SourceVideo` logical size ≠ decoded px; `DrawBitmap` logical dest rect. (Decode-scale hint stays deferred; `MediaOptions` kept extensible.)
6. **Slice 5 — editor + export**: `PreviewScale` control + rebuild wiring; export supersampling + `SourceSize` from `DeviceSize`; finalize logical hit-test/handles.

**Recommended first ship: Slice 0 + Slice 1** — smallest independently-testable, user-visible increment (reduced-scale preview for the common case) with the byte-identical regression guard.

## Gotchas

- **Origins round toward zero, not floor.** Negative-origin effect bounds (post-blur/shadow) differ if you "fix" to floor — keep `(int)` cast (research.md best-practices).
- **Filter-effect sinks** (`FilterEffectActivator`/`CustomFilterEffectContext`) use component-wise `(int)` truncation that differs from `FromRect`; migrating them is a scale-1.0 behavior change → golden-test it.
- **Don't fold render scale into the artistic matrix** (`Transform.CreateMatrix`/`TransformGroup`) — keep it the appended root scale, or `Matrix.TryDecomposeTransform`, editor handles, and serialized transforms corrupt (FR-027). For perspective, **append** scale, never prepend (the `S·P ≠ P·S` rule).
- **Cache is invalidated by renderer rebuild on scale change**; the `CachedWorkingScale` tag lets a tile be reused-with-downsample when `≥` the required scale and missed when `<` (can't invent detail), so a stale-scale tile is never blitted 1:1.
- **Effects multiply by the working scale `w`, not the output scale `s_out`.** A 0.5 proxy fed to a default-`Inherit` effect runs at 0.5 (no upsample); a 2.0 source runs at 2.0 (no downsample, downscaled only at the final stage). The default `Inherit` already keeps a high-res source through the effect; use `ClampToOutput` to opt a heavy effect out of carrying high res, or `Oversample(k)` to force SSAA (FR-036). *(`PreserveSource` was removed — it equaled `Inherit`.)*
