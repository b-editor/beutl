using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class CompositionContextTests
{
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

    [Test]
    public void ConditionPropertiesReplaceTheConditionsWithoutLosingOthers()
    {
        var context = new CompositionContext(MediaTime.Zero)
        {
            FrameRate = new(24, 1),
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
            TargetDomain = new Rect(0, 0, 96, 64),
        };

        Assert.That(context.Conditions, Is.EqualTo(new CompositionConditions
        {
            FrameRate = new(24, 1),
            PreferProxy = true,
            PreferredProxyPreset = ProxyPreset.Half,
            TargetDomain = new Rect(0, 0, 96, 64),
        }));
        Assert.That(CompositionConditions.Default.FrameRate, Is.EqualTo(CompositionConditions.DefaultFrameRate));
    }

    // Passing the conditions as one value means a condition added later reaches reused sessions without
    // another hand-written copy.
    [Test]
    public void ReusedSessionsTakeTheCurrentConditionsAsOneValue()
    {
        var first = new CompositionContext(MediaTime.Zero) { FrameRate = new(24, 1), PreferProxy = true };
        using CompositionEvaluationSession session = first.CreateEvaluationSession();
        session.Update(first, MediaTime.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var next = new CompositionContext(MediaTime.FromSeconds(1))
        {
            FrameRate = new(60, 1),
            PreferredProxyPreset = ProxyPreset.Eighth,
            CancellationToken = cancellation.Token,
        };

        session.Update(next, MediaTime.FromSeconds(2));

        Assert.That(session.Context.Conditions, Is.SameAs(next.Conditions));
        Assert.That(session.Context.Time, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(session.Context.DisableResourceShare, Is.True);
    }
}
