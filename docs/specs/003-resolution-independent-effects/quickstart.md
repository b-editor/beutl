# Quickstart: Per-Clip Proxy via RenderNodeOperation CorrectionScale

This is the dev / reviewer checklist for the feature. Anyone implementing `tasks.md` should be able to follow these steps and finish with green build / green tests / a hand-verified visual comparison.

## 1. Build

```bash
dotnet build Beutl.slnx
```

Or:

```text
/beutl-build
```

Both `net10.0` and `net10.0-windows` must succeed.

## 2. Run unit tests

Whole-solution:

```bash
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings
```

Scope-narrowed via skill:

```text
/beutl-test RenderScaleTests
/beutl-test RenderNodeOperationCorrectionScaleTests
/beutl-test SourceNodeCorrectionScaleTests
/beutl-test TransformerNodeCorrectionScaleTests
/beutl-test CompositorBlitTests
/beutl-test ResolutionEquivalenceTests
/beutl-test LegacyRenderingTests
/beutl-test LegacyRoundTripTests
```

Expected new test files (per `tasks.md`):

- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderScaleTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/RenderNodeOperationCorrectionScaleTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceNodeCorrectionScaleTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/TransformerNodeCorrectionScaleTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/Rendering/CompositorBlitTests.cs`
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/ResolutionEquivalenceTests.cs` (per-in-scope-effect SSIM)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRenderingTests.cs` (corpus regression vs baseline)
- `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/LegacyRoundTripTests.cs` (JSON byte equality)

Test pattern (per-effect SSIM):

```csharp
[TestCase("blur-proxy.json", 4.0f)]
[TestCase("dropshadow-proxy.json", 4.0f)]
public void Effect_ProxyMatchesFullRender_WithinSSIMTolerance(string fixture, float proxyRatio)
{
    var scene = LoadFixture(fixture);

    // Render once with proxy injected on the (synthetic) source.
    var proxyOp = ProxyTestHarness.RenderWithSourceProxy(scene, proxyRatio);
    var proxyFinal = Composite(proxyOp);  // compositor applies upscale blit

    // Render once with no proxy.
    var fullOp = ProxyTestHarness.RenderWithSourceProxy(scene, 1.0f);
    var fullFinal = Composite(fullOp);

    Assert.That(Ssim(proxyFinal, fullFinal), Is.GreaterThanOrEqualTo(0.97f));
}
```

## 3. Format check

```bash
dotnet format Beutl.slnx --verify-no-changes
```

```text
/beutl-format
```

No stylistic-only edits should land — `.editorconfig` / `dotnet format` own style.

## 4. Coverage

```text
/beutl-coverage Beutl.Engine
```

Coverage on `src/Beutl.Engine/Graphics/Rendering/` should not regress vs the pre-feature baseline.

## 5. Eyeball verification

The PR description must include a side-by-side screenshot of:

- A demo scene with a 4K test pattern (or another heavy source) and a small set of overlays (text, rect shape).
- Rendered with proxy enabled on the heavy source (`CorrectionScale = (4, 4)`), composited through the new blit path.
- Rendered with no proxy (`CorrectionScale = Identity`).

The two should be visually indistinguishable to a reviewer who is not told which is which (the SSIM tests make this objective; the screenshot is for human reviewers).

## 6. Legacy corpus regression

A curated set of pre-feature project files lives under `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/`. Each project's baseline-rendered PNG (captured from the previous build before this feature shipped) sits alongside it. `LegacyRenderingTests` walks the corpus and asserts SSIM ≥ 0.97 between current output and baseline. `LegacyRoundTripTests` asserts JSON byte equality after round-trip.

Baseline capture procedure: documented in `tests/Beutl.UnitTests/Engine/Graphics/Fixtures/LegacyResolutionCorpus/README.md`.

## 7. Pre-PR self-checks

```text
/beutl-pre-pr
```

Runs the same checks `claude-code-review.yml` and `beutl-reviewer` would run, but locally.

## 8. PR conventions

- Title: `feat(engine): per-clip proxy via RenderNodeOperation.CorrectionScale` (Conventional Commit per `AGENTS.md`).
- Mention `@beutl-design-reviewer` in the body. The biggest behavioural surface is `RenderNodeOperation.CorrectionScale` propagation and the compositor's upscale blit.
- This is **6th-iteration design** — earlier drafts assumed scene-wide proxy and were abandoned. The PR body should explain the per-clip-vs-scene-wide distinction so reviewers don't get confused by the design pivot history. Link to `spec.md` § "Design history" and `research.md` § "Historical pivots".

## 9. Future work this enables

Once this lands:

- **Per-clip proxy UX and persistence** (follow-up feature): UI toggle per clip in the Inspector, schema in the project file for proxy ratio, automatic proxy media generation (offline pre-render of 1/2 / 1/4 / 1/8 versions of heavy media files).
- **Render-time policy** (follow-up feature): preview uses proxy, export disables proxy, scrubbing uses proxy, etc.
- **Multi-resolution master template feature** — scale all clips by the same factor when changing the project's `FrameSize`. Different from per-clip proxy but enabled by the same `CorrectionScale` foundation.

## 10. Out of scope (explicit non-goals for this PR)

- The user-facing proxy toggle UI (per-clip Inspector control).
- Per-clip proxy settings persistence in `.scene` / `.beutlproj` schema.
- Offline proxy media generation (decoder output at proxy resolution).
- Automatic proxy threshold heuristics ("if source is 4K or larger, default proxy = 1/4").
- A "scene-wide proxy slider" for preview (this can be added later by setting all sources' proxy to the same ratio).

This PR is the engine-side mechanism only. Everything user-facing is a follow-up feature.
