using Beutl.Composition;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class CompositionContextTests
{
    [Test]
    public void CompositionFrame_PreservesThreeValueContractAndAddsValidatedPurposeProvenance()
    {
        var frame = new CompositionFrame([], default, default);
        var auxiliary = new CompositionFrame(
            [],
            default,
            default,
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary);
        var (objects, time, size) = auxiliary;

        Assert.Multiple(() =>
        {
            Assert.That(frame.RenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(frame.PullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
            Assert.That(auxiliary.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(auxiliary.PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(objects, Is.EqualTo(auxiliary.Objects));
            Assert.That(time, Is.EqualTo(auxiliary.Time));
            Assert.That(size, Is.EqualTo(auxiliary.Size));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositionFrame(
                    [], default, default, (RenderIntent)42, RenderPullPurpose.Frame));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositionFrame(
                    [], default, default, RenderIntent.Preview, (RenderPullPurpose)42));
        });
    }

    [Test]
    public void CompositionFrame_EqualityIncludesBothPolicyValues()
    {
        var preview = new CompositionFrame(
            [], default, default, RenderIntent.Preview, RenderPullPurpose.Frame);
        var delivery = new CompositionFrame(
            [], default, default, RenderIntent.Delivery, RenderPullPurpose.Frame);
        var auxiliary = new CompositionFrame(
            [], default, default, RenderIntent.Preview, RenderPullPurpose.Auxiliary);

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.Not.EqualTo(delivery));
            Assert.That(preview, Is.Not.EqualTo(auxiliary));
            Assert.That(preview.GetHashCode(), Is.Not.EqualTo(delivery.GetHashCode()));
            Assert.That(preview.GetHashCode(), Is.Not.EqualTo(auxiliary.GetHashCode()));
        });
    }

    [Test]
    public void Constructor_DefaultsToPreviewFramePolicy()
    {
        var context = new CompositionContext(TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(context.RenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(context.PullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
        });
    }

    [Test]
    public void Constructor_AcceptsExplicitPolicyAndRejectsUnknownValues()
    {
        var context = new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary);

        Assert.Multiple(() =>
        {
            Assert.That(context.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(context.PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositionContext(TimeSpan.Zero, (RenderIntent)42, RenderPullPurpose.Frame));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CompositionContext(TimeSpan.Zero, RenderIntent.Preview, (RenderPullPurpose)42));
        });
    }

    [Test]
    public void Default_ReturnsFreshInstance_SoMutationDoesNotLeakGlobally()
    {
        CompositionContext first = CompositionContext.Default;
        CompositionContext second = CompositionContext.Default;

        first.PreferProxy = true;
        first.DisableResourceShare = true;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.SameAs(second), "Default must not be a shared mutable singleton.");
            Assert.That(second.PreferProxy, Is.False);
            Assert.That(CompositionContext.Default.DisableResourceShare, Is.False);
        });
    }
}
