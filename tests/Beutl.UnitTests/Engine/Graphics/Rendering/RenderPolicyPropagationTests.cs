using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderPolicyPropagationTests
{
    [Test]
    public void PlanNode_ForwardsPolicyToDescribe()
    {
        var effect = new PolicyProbeEffect();
        using var resource = effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        var input = RenderNodeOperation.CreateLambda(new Rect(0, 0, 1, 1), static _ => { });
        var context = new RenderNodeContext(
            [input], RenderIntent.Preview, pullPurpose: RenderPullPurpose.Auxiliary);

        RenderNodeOperation[] outputs = node.Process(context);
        RenderNodeOperation.DisposeAll(outputs);

        Assert.Multiple(() =>
        {
            Assert.That(effect.ObservedIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(effect.ObservedPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
        });
    }

    [Test]
    public void PublicPolicyContexts_RejectUnknownEnumValues()
    {
        const RenderIntent badIntent = (RenderIntent)42;
        const RenderPullPurpose badPurpose = (RenderPullPurpose)42;
        using var node = new EmptyRenderNode();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderNodeContext([], badIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderNodeContext(
                [], RenderIntent.Preview, pullPurpose: badPurpose));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderNodeProcessor(
                node, false, badIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PassUniformContext(
                1f, 1, 1, new Rect(0, 0, 1, 1), badIntent, RenderPullPurpose.Frame));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PassUniformContext(
                1f, 1, 1, new Rect(0, 0, 1, 1), RenderIntent.Preview, badPurpose));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BrushConstructor(
                Rect.Empty, null, BlendMode.SrcOver, badIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BrushConstructor(
                Rect.Empty, null, BlendMode.SrcOver, RenderIntent.Preview, pullPurpose: badPurpose));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImmediateCanvas(null!, badIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImmediateCanvas(
                null!, RenderIntent.Preview, pullPurpose: badPurpose));
        });
    }

    [SuppressResourceClassGeneration]
    private sealed partial class PolicyProbeEffect : FilterEffect
    {
        public RenderIntent? ObservedIntent { get; private set; }

        public RenderPullPurpose? ObservedPurpose { get; private set; }

        public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
        {
            ObservedIntent = builder.RenderIntent;
            ObservedPurpose = builder.PullPurpose;
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
        }
    }

    private sealed class EmptyRenderNode : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];
    }
}
