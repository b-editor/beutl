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
    // A brush registered after a segment's last operation cannot be painted by that segment, so the segment must
    // not inherit its dependency, required region, or target-effect flag.
    [Test]
    public void LegacySegment_DoesNotTakeABrushRegisteredAfterItsLastOperation()
    {
        using Brush.Resource first = MakeDrawableBrush(10);
        using Brush.Resource second = MakeDrawableBrush(20);
        var effect = new TwoSegmentBrushEffect(first, second);
        LegacyFilterEffectRenderFragmentPayload[] segments = RecordSegments(effect);

        Assert.That(segments, Has.Length.EqualTo(2), "the shader operation must split the legacy run in two");
        Assert.Multiple(() =>
        {
            Assert.That(
                segments[0].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.FirstHandle }));
            Assert.That(
                segments[1].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.FirstHandle, effect.SecondHandle }));
        });
    }

    // RegisterBrush documents no ordering requirement, so a handle registered before a typed operation must still
    // reach the later legacy segment that paints with it.
    [Test]
    public void LegacySegment_TakesABrushRegisteredBeforeATypedOperation()
    {
        using Brush.Resource brush = MakeDrawableBrush(10);
        var effect = new BrushBeforeTypedOperationEffect(brush);
        LegacyFilterEffectRenderFragmentPayload[] segments = RecordSegments(effect);

        Assert.That(segments, Has.Length.EqualTo(1), "only the custom effect belongs to a legacy segment");
        Assert.That(
            segments[0].Brushes.Select(static binding => binding.Handle),
            Is.EqualTo(new[] { effect.Handle }));
    }

    // A handle stays usable from every operation authored after it, so a second use past a typed operation must
    // reach the second segment too.
    [Test]
    public void LegacySegment_TakesABrushReusedAfterATypedOperation()
    {
        using Brush.Resource brush = MakeDrawableBrush(10);
        var effect = new ReusedBrushAcrossTypedOperationEffect(brush);
        LegacyFilterEffectRenderFragmentPayload[] segments = RecordSegments(effect);

        Assert.That(segments, Has.Length.EqualTo(2), "the shader operation must split the legacy run in two");
        Assert.Multiple(() =>
        {
            Assert.That(
                segments[0].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.Handle }));
            Assert.That(
                segments[1].Brushes.Select(static binding => binding.Handle),
                Is.EqualTo(new[] { effect.Handle }));
        });
    }

    private static LegacyFilterEffectRenderFragmentPayload[] RecordSegments(FilterEffect effect)
    {
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
        return graph.Fragments
            .Select(static fragment => (RenderFragmentReference)fragment.Payload!)
            .Where(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect)
            .Select(static reference => (LegacyFilterEffectRenderFragmentPayload)reference.Payload!)
            .ToArray();
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

    public new sealed class Resource : FilterEffect.Resource
    {
        public Resource()
            : base(skipDefaultInitialization: true)
        {
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class BrushBeforeTypedOperationEffect(Brush.Resource brush) : FilterEffect
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    public FilterEffectBrush Handle { get; private set; } = FilterEffectBrush.Empty;

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        Handle = context.RegisterBrush(brush);
        context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        context.CustomEffect(Handle, static (_, _) => { }, static (_, bounds) => bounds);
    }

    public override Resource ToResource(CompositionContext context)
    {
        var created = new Resource();
        bool updateOnly = true;
        created.Update(this, context, ref updateOnly);
        return created;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public Resource()
            : base(skipDefaultInitialization: true)
        {
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class ReusedBrushAcrossTypedOperationEffect(Brush.Resource brush) : FilterEffect
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    public FilterEffectBrush Handle { get; private set; } = FilterEffectBrush.Empty;

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        Handle = context.RegisterBrush(brush);
        context.CustomEffect(Handle, static (_, _) => { }, static (_, bounds) => bounds);
        context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        context.CustomEffect(Handle, static (_, _) => { }, static (_, bounds) => bounds);
    }

    public override Resource ToResource(CompositionContext context)
    {
        var created = new Resource();
        bool updateOnly = true;
        created.Update(this, context, ref updateOnly);
        return created;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public Resource()
            : base(skipDefaultInitialization: true)
        {
        }
    }
}
