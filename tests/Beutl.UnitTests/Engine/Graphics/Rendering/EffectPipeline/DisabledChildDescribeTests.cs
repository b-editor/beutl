using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression coverage for the 004 un-flattening: a group now describes into a single render node, so a disabled
/// child must be skipped at <see cref="FilterEffect.Describe"/> time (the per-child flatten that used to honor
/// <see cref="Beutl.Engine.EngineObject.IsEnabled"/> is gone). Pre-004 a disabled nested effect rendered as a
/// no-op; these tests pin that back. The graph-level cases run without a GPU; the pixel-parity cases are
/// Vulkan-gated.
/// </summary>
[TestFixture]
public class DisabledChildDescribeTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    private static EffectGraphBuilder Describe(FilterEffect effect)
    {
        using FilterEffect.Resource resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, resource);
        return builder;
    }

    private static int DescribedNodeCount(FilterEffect effect)
    {
        using EffectGraph graph = Describe(effect).Build();
        return graph.Nodes.Count;
    }

    private static StructuralKey DescribedKey(FilterEffect effect)
    {
        using EffectGraph graph = Describe(effect).Build();
        return StructuralKey.Compute(graph);
    }

    private static FilterEffectGroup Group(bool blurEnabled)
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;
        group.Children.Add(gamma);

        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(6, 6);
        blur.IsEnabled = blurEnabled;
        group.Children.Add(blur);

        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);
        return group;
    }

    private static FilterEffectGroup GammaInvertOnly()
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;
        group.Children.Add(gamma);
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);
        return group;
    }

    // (b) structural half: skipping a disabled child yields exactly the graph of the group without that child.
    [Test]
    public void DisabledGroupChild_IsExcludedFromDescribedGraph()
    {
        StructuralKey withDisabledBlur = DescribedKey(Group(blurEnabled: false));
        StructuralKey withoutBlur = DescribedKey(GammaInvertOnly());
        StructuralKey allEnabled = DescribedKey(Group(blurEnabled: true));

        Assert.That(withDisabledBlur, Is.EqualTo(withoutBlur));
        Assert.That(withDisabledBlur, Is.Not.EqualTo(allEnabled));
    }

    // (b) toggle half: flipping IsEnabled across frames is a structural change (node count differs), so the plan
    // cache misses and recompiles rather than replaying a stale plan.
    [Test]
    public void TogglingChildIsEnabled_IsStructural()
    {
        var group = new FilterEffectGroup();
        var gamma = new Gamma();
        gamma.Amount.CurrentValue = 1.5f;
        group.Children.Add(gamma);
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(6, 6);
        blur.IsEnabled = false;
        group.Children.Add(blur);
        var invert = new Invert();
        invert.Amount.CurrentValue = 1f;
        group.Children.Add(invert);

        using var resource = (FilterEffectGroup.Resource)group.ToResource(CompositionContext.Default);

        var builder1 = new EffectGraphBuilder(s_bounds, 1f, 1f);
        group.Describe(builder1, resource);
        using EffectGraph graph1 = builder1.Build();
        StructuralKey key1 = StructuralKey.Compute(graph1);
        int count1 = graph1.Nodes.Count;

        blur.IsEnabled = true;
        bool updateOnly = false;
        resource.Update(group, CompositionContext.Default, ref updateOnly);

        var builder2 = new EffectGraphBuilder(s_bounds, 1f, 1f);
        group.Describe(builder2, resource);
        using EffectGraph graph2 = builder2.Build();
        StructuralKey key2 = StructuralKey.Compute(graph2);
        int count2 = graph2.Nodes.Count;

        Assert.That(key1, Is.Not.EqualTo(key2), "toggling IsEnabled must change the structural key");
        Assert.That(count2, Is.GreaterThan(count1), "enabling a child must add its node(s)");
    }

    // (c) structural half: a presenter delegating to a disabled target describes nothing (identity passthrough).
    [Test]
    public void DisabledPresenterTarget_DescribesIdentityGraph()
    {
        var enabledTarget = new Gamma();
        enabledTarget.Amount.CurrentValue = 1.6f;
        var enabledPresenter = new FilterEffectPresenter();
        enabledPresenter.Target.CurrentValue = enabledTarget;

        var disabledTarget = new Gamma();
        disabledTarget.Amount.CurrentValue = 1.6f;
        disabledTarget.IsEnabled = false;
        var disabledPresenter = new FilterEffectPresenter();
        disabledPresenter.Target.CurrentValue = disabledTarget;

        Assert.That(DescribedNodeCount(enabledPresenter), Is.GreaterThan(0));
        Assert.That(DescribedNodeCount(disabledPresenter), Is.EqualTo(0));
    }

    // (a) pixel parity: a disabled child renders exactly as if it were removed, and differently from all-enabled.
    [Test]
    public void DisabledGroupChild_RendersIdenticalToRemovingIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap disabled = Render(Group(blurEnabled: false));
            using Bitmap removed = Render(GammaInvertOnly());
            using Bitmap allEnabled = Render(Group(blurEnabled: true));

            GoldenImageHarness.AssertByteIdentical(removed, disabled);
            Assert.That(disabled.GetPixelSpan().SequenceEqual(allEnabled.GetPixelSpan()), Is.False,
                "the enabled blur must change the render, else the parity would be vacuous");
        });
    }

    // (b) render half: both sides of a toggle render as their respective (removed / all-enabled) graphs.
    [Test]
    public void TogglingChildIsEnabled_RendersCorrectlyBothSides()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap disabled = Render(Group(blurEnabled: false));
            using Bitmap removed = Render(GammaInvertOnly());
            using Bitmap enabled = Render(Group(blurEnabled: true));
            using Bitmap allBlur = Render(Group(blurEnabled: true));

            GoldenImageHarness.AssertByteIdentical(removed, disabled);
            GoldenImageHarness.AssertByteIdentical(allBlur, enabled);
        });
    }

    // (c) render half: a presenter with a disabled target renders identically to one with no target (identity).
    [Test]
    public void DisabledPresenterTarget_RendersIdentity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var disabledTarget = new Gamma();
            disabledTarget.Amount.CurrentValue = 1.6f;
            disabledTarget.IsEnabled = false;
            var disabledPresenter = new FilterEffectPresenter();
            disabledPresenter.Target.CurrentValue = disabledTarget;

            var noTarget = new FilterEffectPresenter();

            using Bitmap disabled = Render(disabledPresenter);
            using Bitmap identity = Render(noTarget);
            GoldenImageHarness.AssertByteIdentical(identity, disabled);
        });
    }

    private static Bitmap Render(FilterEffect effect)
    {
        var fill = new LinearGradientBrush();
        fill.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        fill.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        fill.GradientStops.Add(new GradientStop(Colors.Red, 0));
        fill.GradientStops.Add(new GradientStop(Colors.Lime, 0.5f));
        fill.GradientStops.Add(new GradientStop(Colors.Blue, 1));

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 240;
        shape.Height.CurrentValue = 150;
        shape.Fill.CurrentValue = fill;

        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 12f;
        shape.Transform.CurrentValue = rotation;

        shape.FilterEffect.CurrentValue = effect;
        Drawable.Resource resource = shape.ToResource(CompositionContext.Default);
        return GoldenImageHarness.RenderAtScale(resource, SceneFixtures.ReferenceSize, 1f);
    }
}
