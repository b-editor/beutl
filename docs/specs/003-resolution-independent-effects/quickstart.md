# Quickstart: Resolution-Independent Pixel-Absolute Effects

This is the dev / reviewer checklist for the feature. Anyone picking up `tasks.md` should be able to follow these steps and end with a green build / green tests / a hand-verified visual comparison.

## 1. Build

```bash
dotnet build Beutl.slnx
```

Or via the skill:

```text
/beutl-build
```

Both must succeed for both target frameworks (`net10.0` and `net10.0-windows`).

## 2. Run unit tests

Whole-solution test (fast enough for laptop):

```bash
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings
```

Or scope-narrowed via the skill:

```text
/beutl-test ResolutionEquivalence
/beutl-test RenderScaleTests
/beutl-test FilterEffectContextScaling
```

Expected new tests (created during `tasks.md`):

- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderScaleTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/ReferenceFramePropagationTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/GraphicsContext2DScalingTests.cs` (Rect / Matrix translation scaling + `*Raw` twins)
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/PenScalingTests.cs` (`PenHelper.GetScaled*` helpers)
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/TransformScalingTests.cs` (`TransformRenderNode.IsRaw` flag, `ImmediateCanvas.PushTransform` translation-column scaling, nested-scene inner-canvas consistency)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/FilterEffectContextScalingTests.cs` (scaled helpers + `*Raw` + sub-pixel/zero/NaN guards)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` (per-primitive `[TestCase]`s — effects, shapes, transforms, direct draws, combined)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRenderingTests.cs` (corpus-driven regression vs. baseline)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRoundTripTests.cs` (proves no project-file rewrite on load)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/CrossResolutionTests.cs` (US3 — same project at different export sizes)
- `tests/Beutl.Graphics3DTests/FilterEffects/Render3DWithFilterResolutionTests.cs`

Each `ResolutionEquivalenceTests` case follows this pattern:

```csharp
[TestCase("blur-soft.json")]
[TestCase("dropshadow-default.json")]
public void Effect_ProxyMatchesExport_WithinSSIMTolerance(string fixture)
{
    var scene = LoadFixture(fixture);                 // export size, e.g. 1920×1080
    var export = Render(scene, scene.FrameSize);      // 1.0 scale
    var proxy  = Render(scene, scene.FrameSize / 4);  // 0.25 scale, ReferenceFrame = scene.FrameSize
    var proxyUp = BicubicUpscale(proxy, scene.FrameSize);
    Assert.That(Ssim(export, proxyUp), Is.GreaterThanOrEqualTo(0.97f));
}
```

The fixture corpus lives in `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/ResolutionIndependence/` (added with the test code). Adding a new effect to the in-scope list means adding a fixture and a `[TestCase]`.

## 3. Format check

```bash
dotnet format Beutl.slnx --verify-no-changes
```

Or:

```text
/beutl-format    # confirm scope = verify
```

No stylistic-only edits should land — that is the linter's job.

## 4. Coverage

```text
/beutl-coverage Beutl.Engine
```

Coverage on `src/Beutl.Engine/Graphics/FilterEffects/` should not regress versus the pre-feature baseline (CI threshold from `.github/workflows/dotnet.yml`).

## 5. Eyeball verification (smoke project)

The PR description must include a side-by-side screenshot of:

- A demo scene with Blur (Sigma = 20 px), DropShadow (Position = (10, 10), Sigma = (15, 15)), and StrokeEffect (Offset = (4, 4)) applied to a single shape.
- Rendered at the scene's full export size.
- Rendered with `RenderScale = 0.25` (manually constructed via `new Renderer(W/4, H/4, scene.FrameSize)`), then upscaled with bicubic in an image viewer.

The two should be visually indistinguishable to a reviewer who is not told which is which. (The SSIM test makes this objective; the screenshot is for human reviewers who do not want to read SSIM numbers.)

## 6. Legacy corpus regression

A curated set of pre-feature project files lives under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`. Each project's baseline-rendered PNG (captured from the previous build before this feature shipped) sits alongside it. `LegacyRenderingTests` walks the corpus and asserts SSIM ≥ 0.97 between current output and baseline.

If you need to regenerate the baseline (e.g. you intentionally accepted a visual change in a separate PR), document the change in the commit message and update both the baseline PNGs and the relevant fixture metadata in the same commit.

## 7. Pre-PR self-checks

Before opening the PR:

```text
/beutl-pre-pr
```

This runs the same checks `claude-code-review.yml` and `beutl-reviewer` would run, but locally.

## 8. PR conventions

- Title: `feat: make rendering helpers resolution-independent` (Conventional Commit per `AGENTS.md`). The change covers `FilterEffectContext` length helpers, `GraphicsContext2D` Rect helpers, `PenHelper.GetScaled*`, and the Transform path (`TransformRenderNode.IsRaw` + `ImmediateCanvas.PushTransform`).
- The PR's biggest behavioural surface is the modified scaled helpers + their `*Raw` twins across `FilterEffectContext` and `GraphicsContext2D` Rect helpers, the `PenHelper.GetScaled*` family (including the new `GetScaledBounds`), and the `ImmediateCanvas.PushTransform` translation-column scaling. Mention `@beutl-design-reviewer` in the PR body.
- **Out of scope, follow-up PRs**: `Geometry` path coordinates, `TextBlock.Size / Spacing`, `Brush` pixel rectangles. Each gets its own design discussion.

## 9. Future work this enables

Once this lands:

- **Proxy preview workflow** (render at smaller raster while editing) can be added as a UI feature without touching any effect / drawable / transform code or `FilterEffectContext` / `GraphicsContext2D` / `ImmediateCanvas`. It just constructs the renderer with `new Renderer(proxyW, proxyH, scene.FrameSize)`.
- **A "multi-resolution master" template feature**, since the entire rendering pipeline now scales proportionally when the user changes the project's `FrameSize`.
- **The deferred follow-ups** (the surfaces this PR does NOT cover — see § 8 "Out of scope, follow-up PRs"): `Geometry` path coordinates, `TextBlock.Size / Spacing` (typography), and `Brush` pixel rectangles (`TileBrush`, `ImageBrush` SourceRect / DestinationRect). Each becomes its own spec when prioritized.

> **NOT future work** (already covered by this PR): `Pen.Thickness` / `DashOffset` / `Offset` scaling via `PenHelper.GetScaled*` (Pen contract); `Beutl.Graphics.Transformation.*` automatic scaling via `ImmediateCanvas.PushTransform` (Transform contract). Earlier drafts of this quickstart listed both as follow-ups; they are now in scope and shipping in this PR.
