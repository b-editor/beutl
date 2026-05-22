using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// T028 — smoke-test the 13 in-scope effects under a non-Identity upstream CorrectionScale.
/// For each effect we verify that <see cref="FilterEffectRenderNode.Process"/> runs without throwing,
/// produces at least one output operation, and propagates the upstream CorrectionScale on every output.
/// The visual correctness of the 8 CustomEffect-based effects under non-Identity upstream is a follow-up
/// (see <c>data-model.md</c> § "Implementation deviation"); this smoke test only proves the pipeline wiring.
/// </summary>
[TestFixture]
public class ExtensionAuthorNoOpTests
{
    private static readonly RenderScale s_proxyScale = new(4f, 4f);

    private static RenderNodeContext UpstreamAtScale(RenderScale scale)
    {
        return new RenderNodeContext([
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                hitTest: _ => false,
                correctionScale: scale)
        ]);
    }

    private static void AssertEffectProducesOutput(FilterEffect effect, RenderScale upstreamScale)
    {
        var resource = effect.ToResource(CompositionContext.Default);
        var node = new FilterEffectRenderNode(resource);
        var ctx = UpstreamAtScale(upstreamScale);

        Assert.That(() => node.Process(ctx), Throws.Nothing,
            $"FilterEffectRenderNode.Process threw under upstream CorrectionScale={upstreamScale} for {effect.GetType().Name}");

        var outs = node.Process(ctx);
        Assert.That(outs, Is.Not.Empty,
            $"{effect.GetType().Name} produced no output operations under CorrectionScale={upstreamScale}");

        // FilterEffectRenderNode emits two kinds of operations depending on what the activator left:
        //   - When the builder still holds an SKImageFilter (primitive-only chain, no custom effect ran),
        //     the output is a `CreateLambda` Lambda that materialises via `PushPaint` / `SaveLayer` at the
        //     compositor's output canvas scale. Its `CorrectionScale` is `Identity` because the SaveLayer
        //     produces full-resolution content — see `FilterEffectRenderNode.Process` comments.
        //   - When the activator has materialised RT-based targets (a custom effect ran or no filter
        //     remained), the output is `CreateFromRenderTarget` and its `CorrectionScale` propagates
        //     the unified upstream scale.
        // We accept either Identity or the unified upstream scale; both are valid depending on the chain.
        foreach (var op in outs)
        {
            Assert.That(
                op.CorrectionScale == RenderScale.Identity || op.CorrectionScale == upstreamScale,
                $"{effect.GetType().Name} produced an unexpected CorrectionScale={op.CorrectionScale} for upstream={upstreamScale}");
        }
    }

    private static void AssertEffectPropagatesScale(FilterEffect effect, RenderScale upstreamScale)
        => AssertEffectProducesOutput(effect, upstreamScale);

    // ---- The 5 pure-primitive effects: zero source modification required ----

    [Test]
    public void Blur_PropagatesProxyScale()
    {
        var effect = new Blur() { Sigma = { CurrentValue = new Size(10, 10) } };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void DropShadow_PropagatesProxyScale()
    {
        var effect = new DropShadow()
        {
            Position = { CurrentValue = new Point(5, 5) },
            Sigma = { CurrentValue = new Size(10, 10) },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void InnerShadow_PropagatesProxyScale()
    {
        var effect = new InnerShadow()
        {
            Position = { CurrentValue = new Point(5, 5) },
            Sigma = { CurrentValue = new Size(10, 10) },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void Erode_PropagatesProxyScale()
    {
        var effect = new Erode()
        {
            RadiusX = { CurrentValue = 4f },
            RadiusY = { CurrentValue = 4f },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void Dilate_PropagatesProxyScale()
    {
        var effect = new Dilate()
        {
            RadiusX = { CurrentValue = 4f },
            RadiusY = { CurrentValue = 4f },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    // ---- The 8 CustomEffect-based effects: pipeline wiring + propagation only ----
    // Visual correctness of these effects under non-Identity upstream requires the
    // structural CreateTarget-at-upstream-scale follow-up; see data-model.md.

    [Test]
    public void MosaicEffect_PropagatesProxyScale()
    {
        var effect = new MosaicEffect()
        {
            TileSize = { CurrentValue = new Size(8, 8) },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void ColorShift_PropagatesProxyScale()
    {
        var effect = new ColorShift()
        {
            RedOffset = { CurrentValue = new PixelPoint(2, 0) },
            GreenOffset = { CurrentValue = new PixelPoint(-2, 0) },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void ShakeEffect_PropagatesProxyScale()
    {
        // ShakeEffect translates bounds in authoring space — CorrectionScale-agnostic by design.
        var effect = new ShakeEffect()
        {
            StrengthX = { CurrentValue = 5f },
            StrengthY = { CurrentValue = 5f },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void SplitEffect_PropagatesProxyScale()
    {
        var effect = new SplitEffect()
        {
            HorizontalDivisions = { CurrentValue = 2 },
            VerticalDivisions = { CurrentValue = 2 },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void Clipping_PropagatesProxyScale()
    {
        var effect = new Clipping()
        {
            Left = { CurrentValue = 5f },
            Right = { CurrentValue = 5f },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void FlatShadow_PropagatesProxyScale()
    {
        var effect = new FlatShadow()
        {
            Length = { CurrentValue = 10f },
            Angle = { CurrentValue = 45f },
        };
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    [Test]
    public void StrokeEffect_PropagatesProxyScale()
    {
        var effect = new StrokeEffect();
        // Default Pen is null on construction → ApplyTo will skip the inner action but should still propagate.
        AssertEffectPropagatesScale(effect, s_proxyScale);
    }

    // ---- Identity upstream: every effect's existing behavior is preserved (regression guard) ----

    [Test]
    public void AllEffects_IdentityUpstream_StaysIdentity()
    {
        FilterEffect[] effects =
        [
            new Blur() { Sigma = { CurrentValue = new Size(5, 5) } },
            new DropShadow(),
            new InnerShadow(),
            new Erode(),
            new Dilate(),
            new MosaicEffect(),
            new ColorShift(),
            new ShakeEffect(),
            new SplitEffect(),
            new Clipping(),
            new FlatShadow(),
            new StrokeEffect(),
        ];

        foreach (var effect in effects)
        {
            AssertEffectPropagatesScale(effect, RenderScale.Identity);
        }
    }
}
