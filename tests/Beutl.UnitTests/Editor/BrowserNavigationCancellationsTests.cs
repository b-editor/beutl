using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class BrowserNavigationCancellationsTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OverlappingNavigationsKeepIndependentCancellationIdentities(bool newestCompletesFirst)
    {
        var references = new Dictionary<nint, int>();
        using var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        cancellations.MarkCanceled(1);
        cancellations.BeginNavigation(2);
        cancellations.MarkCanceled(2);

        // Distinct WKNavigation objects can request the same URL; completion order is irrelevant.
        Assert.That(cancellations.CompleteNavigation(newestCompletesFirst ? 2 : 1), Is.True);
        Assert.That(cancellations.CompleteNavigation(newestCompletesFirst ? 1 : 2), Is.True);
        Assert.That(cancellations.Current, Is.EqualTo((nint)0));
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    [Test]
    public void RetryingTheSameUrlDoesNotInheritThePreviousCancellation()
    {
        var references = new Dictionary<nint, int>();
        using var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        cancellations.MarkCanceled(1);
        cancellations.BeginNavigation(2);
        Assert.That(cancellations.CompleteNavigation(1), Is.True);
        Assert.That(cancellations.Current, Is.EqualTo((nint)2));
        Assert.That(cancellations.CompleteNavigation(2), Is.False);
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    [Test]
    public void CommittingANewerPagePreservesOlderPendingCancellations()
    {
        var references = new Dictionary<nint, int>();
        using var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        cancellations.MarkCanceled(1);
        cancellations.BeginNavigation(2);
        cancellations.CommitNavigation(2);
        Assert.That(cancellations.CompleteNavigation(1), Is.True);
        Assert.That(cancellations.CompleteNavigation(2), Is.False);
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    [Test]
    public void UnrelatedCompletionsDoNotChangeTheCurrentNavigation()
    {
        var references = new Dictionary<nint, int>();
        using var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        cancellations.MarkCanceled(1);
        Assert.That(cancellations.CompleteNavigation(3), Is.False);
        Assert.That(cancellations.Current, Is.EqualTo((nint)1));
        Assert.That(cancellations.CompleteNavigation(1), Is.True);
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    [Test]
    public void FailedInterceptionReleasesItsMarkerWithoutConsumingANormalFailure()
    {
        var references = new Dictionary<nint, int>();
        using var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        Assert.That(cancellations.MarkCanceled(1), Is.True);
        Assert.That(cancellations.MarkCanceled(1), Is.False);
        Assert.That(references[1], Is.EqualTo(2));
        Assert.That(cancellations.ForgetCancellation(1), Is.True);
        Assert.That(cancellations.CompleteNavigation(1), Is.False);
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    [Test]
    public void DisposingReleasesEveryOutstandingNavigationOnce()
    {
        var references = new Dictionary<nint, int>();
        var cancellations = Create(references);
        cancellations.BeginNavigation(1);
        cancellations.MarkCanceled(1);
        cancellations.BeginNavigation(2);
        cancellations.MarkCanceled(2);
        cancellations.Dispose();
        cancellations.Dispose();
        Assert.That(cancellations.Current, Is.EqualTo((nint)0));
        Assert.That(cancellations.CompleteNavigation(1), Is.False);
        Assert.That(references.Values, Is.All.EqualTo(0));
    }

    private static BrowserNavigationCancellations Create(Dictionary<nint, int> references) => new(
        navigation => references[navigation] = references.GetValueOrDefault(navigation) + 1,
        navigation =>
        {
            Assert.That(references.GetValueOrDefault(navigation), Is.GreaterThan(0), "Each native release needs an owned reference.");
            references[navigation]--;
        });
}
