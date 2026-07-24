# Quickstart: Resolution-Independent Rendering Pipeline

**Feature**: 003 | Audience: a developer implementing or validating the feature. Pairs with [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/).

## The mental model in one paragraph

The 2D pipeline has no scale today: `1 logical unit == 1 device pixel`, hard-wired as `ToSize(1)` / `(int)bounds.Width` / `PixelRect.FromRect(bounds)`. This feature is **supply-driven** with three scales: the render request carries an **output scale `s_out`** (the *final* target only — `RenderNodeContext.OutputScale`); each operation carries an **`EffectiveScale`** = the density its pixels exist at (vector = `Unbounded`); and each buffer-allocating boundary chooses a **working scale `w`**. The standard `RenderScaleContract.MaterializeAtWorkingScale` used by built-in effects computes `w = min(max(s_out, densest concrete input density), MaxWorkingScale)`, so `s_out` is its floor but never its ceiling. There is no closed `ResolutionPolicy` enum, but a custom `FilterEffectRenderNode.GetWorkingScaleContract()` may declare different semantics; in particular an explicit `Custom` contract may intentionally choose `w < s_out`. Thus a 0.5 proxy under the standard policy runs at 0.5 in a 0.5 preview and at 1.0 in a 1.0 delivery, while an explicit custom 0.5 contract remains 0.5 in either request (subject to `MaxWorkingScale` and per-buffer bounds clamps). Vector-only standard inputs fall back to `s_out`. The FR-008 coordinate-space rule applies at `w`: logical-space geometry under the CTM is unchanged, device-buffer dimensions and device-space shader uniforms convert once (`× w`), readback-derived geometry converts back (`÷ w`). The root surface is `ceil(FrameSize × s_out)` with one `Matrix.CreateScale(s_out)`; an op whose `e ≠ s_out` is resampled once at the final-stage blit; `MaxWorkingScale` caps `w` (FR-037: preview `2 × s_out`; export imposes **no** quality ceiling — `+∞`, with the per-buffer dimension clamp as the sole allocatability bound). Preview uses `s_out ≤ 1` (Full/Half/Quarter/Fit); export uses `1.0` or `s_out > 1` supersampling. At `s_out = 1.0` vector / Skia-filter / unscaled-bitmap content is byte-identical to today; *(2026-06-08 amendment)* a transform re-scaling a bitmap's density, and a scaled bitmap into an effect, are intentionally not byte-identical (coherent density model, FR-019).

## Validate it (the acceptance loop)

All pixel tests are **Vulkan-gated** (`VulkanTestEnvironment.EnsureAvailable()` → `Assert.Ignore` on GPU-less CI), like the existing `ImmediateCanvasVulkanTests`. The non-GPU guards (supply-driven density math) run without a GPU. *(The SC-008 `NoPixelCouplingOnRenderPathTest` search test was deferred — T007; it needs an allowlist for the load-bearing logical-`ToSize(1)`/`(int)`-at-`w=1` sites.)*

```bash
# byte-equality + SSIM golden tests (need a GPU; skipped on CI agents without one)
dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~Rendering.Golden

# supply-driven density math (always runs, no GPU)
dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~ResolutionScaleTests

# regenerate golden baselines after an INTENTIONAL change (writes .bin instead of asserting)
BEUTL_GOLDEN_UPDATE=1 dotnet test Beutl.slnx -f net10.0 --filter FullyQualifiedName~Rendering.Golden

# benchmark (explicit; paired significance gate, not in default CI)
dotnet test Beutl.slnx -f net10.0 --filter "Category=Benchmark"
```

### The four gates (pinned numbers — see research.md D5)

| Gate | Status | What | Threshold |
|---|---|---|---|
| Byte-equality (SC-001/SC-005) | **shipped** (determinism form) | raw RgbaF16 `Snapshot()` rendered twice at `s=1.0` (HEAD vs HEAD) + code-inspected `w == 1` fast paths; the frozen **pre-feature `.bin` baseline** comparison is **deferred** (T017, pending the CI GPU-lane decision — see the SC-001 substantiation note) | zero epsilon (origins **toward-zero**, extents **ceil**) |
| Reduced-scale exact (SC-004) | shipped | render `s=0.5`, Mitchell-upscale, vs `s=1.0` | SSIM ≥ **0.985** AND MAE ≤ **0.02** |
| Mixed-scale (SC-005) | shipped | full-res over reduced-res nested vs full reference | SSIM ≥ 0.985, MAE ≤ 0.02, seam ≤ **0.05** |
| Supersample (SC-009) | shipped | `s ∈ {2.0, 4.0}` (export factors Off/2×/4×; 1.5× is exercised only on the reduced-scale path, not in this suite), downscaled to FrameSize | size == `ceil(FrameSize)` exact; MAE-to-ground-truth strictly lower than `s=1`; SSIM degrades by no more than 0.01 (`SSIM(s≥2) − SSIM(s=1) ≥ −0.01`) — the amended SC-009 dropped the aliasing-energy metric |

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

Preview scale is per-edit-view, non-persisted (FR-035): `EditViewModel.PreviewScale` (`ReactivePropertySlim<RenderScale>`, default `Full`). Changing it rebuilds the `SceneRenderer` + `FrameCacheManager` as **two independent reactive swaps on the UI thread** (NOT atomic — amended FR-031; the narrow tear window self-heals because the change re-queues a render that reads both fresh) and repaints. It never enters `SaveState`/`RestoreState`, so reload always returns to Full.

Export supersampling (FR-034): `OutputViewModel` passes a supersample factor (Off `1` default, `2`/`4` for AA — `SupersampleFactors`) into `new SceneRenderer(Model, factor, disableResourceShare: true, maxWorkingScale: WorkingScaleCeiling.Export())` (= `+∞` — no quality ceiling; the per-buffer dimension clamp bounds allocatability); `FrameProviderImpl.RenderCore` downscales to `FrameSize` (Mitchell ≤ 2×, trilinear + mipmaps at 4×) when the renderer's `OutputScale > 1` and asserts size before encode.

## Implementation slice order (independently testable; see plan.md)

1. **Slice 0 — plumbing skeleton** (no behavior change): add `OutputScale` + `ResolveWorkingScale` + `EffectiveScale` (value type, default `Unbounded`) to `RenderNodeContext`/`RenderNodeOperation`, `OutputScale` to `RenderNodeProcessor`, `OutputScale` to `Renderer` (the 2D request scale `s_out`; the 3D per-surface density is `IRenderer3D.SurfaceDensity`); switch the 3 sinks to `PixelRect.FromRect(rect, w)` with `w=1`. **Gate: byte-identical at 1.0** + the golden harness lands here.
2. **Slice 1 — reduced-scale preview for vector + Skia-filter** (root surface `ceil(FrameSize×s_out)`, root `CreateScale(s_out)`, re-shaped text, scale-keyed `RenderNodeCache`). First user-visible slice.
3. **Slice 2 — render-target (Custom) effects**: `FilterEffectActivator`/`CustomFilterEffectContext` allocate `ceil(bounds×w)`; `FilterEffectContext` primitives × `WorkingScale`; SKSL `iScale` = `w` (GLSL derives it from the device-px `Width`/`Height`); the supply-driven `w` is honored at the effect boundary.
4. **Slice 3 — mixed-scale compositing + nested scenes + DrawableBrush + particles + audio visualizers + 3D-as-bitmap**: per-`EffectTarget` scale; `ImmediateCanvas.DrawSurface`/`DrawRenderTarget` Mitchell-resample on mismatch.
5. **Slice 4 — media decoupling (proxy foundation)**: `SourceImage`/`SourceVideo` logical size ≠ decoded px; `DrawBitmap` logical dest rect. (Decode-scale hint stays deferred; `MediaOptions` kept extensible.)
6. **Slice 5 — editor + export**: `PreviewScale` control + rebuild wiring; export supersampling + `SourceSize` from `DeviceSize`; finalize logical hit-test/handles.

**Recommended first ship: Slice 0 + Slice 1** — smallest independently-testable, user-visible increment (reduced-scale preview for the common case) with the byte-identical regression guard.

## Gotchas

- **Origins round toward zero, not floor.** Negative-origin effect bounds (post-blur/shadow) differ if you "fix" to floor — keep `(int)` cast (research.md best-practices).
- **Filter-effect sinks** (`FilterEffectActivator`/`CustomFilterEffectContext`) use component-wise `(int)` truncation that differs from `FromRect`; migrating them is a scale-1.0 behavior change → golden-test it.
- **Don't fold render scale into the artistic matrix** (`Transform.CreateMatrix`/`TransformGroup`) — keep it the appended root scale, or `Matrix.TryDecomposeTransform`, editor handles, and serialized transforms corrupt (FR-027). For perspective, **append** scale, never prepend (the `S·P ≠ P·S` rule).
- **Cache is invalidated by renderer rebuild on scale change.** The node cache rasterizes at the renderer's `OutputScale` (with `MaxWorkingScale` forwarded), and replay tags each cached tile with its creation density, so a stale-density tile is never blitted 1:1. Multi-scale cache **reuse** (`CachedWorkingScale`: reuse-with-downsample when `≥` the required scale, miss when `<`) is **not shipped** — deferred to a follow-up (T025).
- **Effects multiply by the working scale `w`, not the output scale `s_out`.** Under the standard materializing contract, a 0.5 proxy runs at 0.5 in a 0.5 preview and at 1.0 in a 1.0 delivery; a 2.0 source runs at 2.0 and is downscaled only when it enters a lower-density target. There is **no closed per-effect policy enum**. An effect needing a different `w` (intentional sub-output rendering, clamp for performance, or SSAA) overrides `GetWorkingScaleContract()` in a custom `FilterEffectRenderNode`; an explicit `Custom` result is not raised to the standard `s_out` floor. Override `Process` only for genuinely different topology/lowering. *(An earlier `Inherit`/`ClampToOutput`/`Oversample`/`PreserveSource` policy was removed.)*
