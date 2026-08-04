using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class LegacyFilterSegmentBrushScopeTests
{
    [Test]
    public void LegacySegment_TakesOnlyTheBrushesItsOwnOperationsUse()
    {
        using Brush.Resource first = MakeDrawableBrush(10);
        using Brush.Resource second = MakeDrawableBrush(20);
        var effect = new TwoSegmentBrushEffect(first, second);
        using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 40, 30), Brushes.Resource.White, null));
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        LegacyFilterEffectRenderFragmentPayload[] segments = graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Where(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect)
            .Select(static reference => (LegacyFilterEffectRenderFragmentPayload)reference.Payload!)
            .ToArray();

        Assert.That(segments, Has.Length.EqualTo(2), "the shader operation must split the legacy run in two");
        Assert.Multiple(() =>
        {
            Assert.That(
                segments[0].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.FirstHandle }));
            Assert.That(
                segments[1].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.SecondHandle }));
        });
    }

    private static Brush.Resource MakeDrawableBrush(float size)
    {
        var content = new RectShape();
        content.Width.CurrentValue = size;
        content.Height.CurrentValue = size;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        return brush.ToResource(CompositionContext.Default);
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class TwoSegmentBrushEffect(Brush.Resource first, Brush.Resource second) : FilterEffect
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    public FilterEffectBrush FirstHandle { get; private set; } = FilterEffectBrush.Empty;

    public FilterEffectBrush SecondHandle { get; private set; } = FilterEffectBrush.Empty;

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        FirstHandle = context.RegisterBrush(first);
        context.CustomEffect(FirstHandle, static (_, _) => { }, static (_, bounds) => bounds);
        context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        SecondHandle = context.RegisterBrush(second);
        context.CustomEffect(SecondHandle, static (_, _) => { }, static (_, bounds) => bounds);
    }

    public override Resource ToResource(CompositionContext context)
    {
        var created = new Resource();
        bool updateOnly = true;
        created.Update(this, context, ref updateOnly);
        return created;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
