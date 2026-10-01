using System.Diagnostics;
using Beutl.Editor.Components.WebBrowserTab;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class BrowserAdBlockMatchingBudgetTests
{
    [TestCase(BrowserAdBlockRules.MaximumCandidateRules + 1, 0)]
    [TestCase(0, BrowserAdBlockRules.MaximumCandidateRules + 1)]
    [TestCase(BrowserAdBlockRules.MaximumCandidateRules / 2 + 1, BrowserAdBlockRules.MaximumCandidateRules / 2 + 1)]
    public void OversizedCandidateSetsAllowRequestsWithoutPartialBlocking(int unindexed, int indexed)
    {
        string text = "||ads.example^\n"
            + string.Join('\n', Enumerable.Range(0, unindexed).Select(i => $"/a/{i / 100:D2}/{i % 100:D2}")) + "\n"
            + string.Join('\n', Enumerable.Range(0, indexed).Select(i => $"/shared/{i:D4}"));
        var rules = BrowserAdBlockTestRules.Parse(text);
        Assert.That(rules.ShouldBlock(new Uri("https://ads.example/shared/0000/a/00/00"), null, "image"), Is.False);
    }

    [Test]
    public void ExcessivelyLongUrlsDoNotTriggerUnboundedIndexScanning()
    {
        var rules = BrowserAdBlockTestRules.Parse("||ads.example^");
        Assert.That(rules.ShouldBlock(new Uri("https://ads.example/" + new string('a', BrowserAdBlockRules.MaximumRequestUrlLength)), null, "image"), Is.False);
    }

    [TestCase(9.999, true)]
    [TestCase(10, false)]
    [TestCase(10.001, false)]
    public void RequestMatchDeadlineAllowsRequestsAtOrAfterTenMilliseconds(double elapsedMilliseconds, bool blocked)
    {
        var clock = new ElapsedTimeProvider(TimeSpan.FromMilliseconds(elapsedMilliseconds));
        var rules = BrowserAdBlockTestRules.Parse("||ads.example.com^\n@@||ads.example.com/allowed.js|", clock);

        Assert.That(rules.ShouldBlock(new Uri("https://cdn.ads.example.com/banner.js"), new Uri("https://publisher.test/"), "script"),
            Is.EqualTo(blocked));
    }

    [TestCase(9.999, true)]
    [TestCase(10, false)]
    [TestCase(10.001, false)]
    public void RequestMatchDeadlineDiscardsMatchesCompletedAtOrAfterTenMilliseconds(double elapsedMilliseconds, bool blocked)
    {
        var request = new Uri("http://a/");
        // Keep time frozen through five URL-index checks, the exception pass, and the
        // pre-match check for this single unindexed rule. Advance after the blocking regex.
        var clock = new ElapsedTimeProvider(TimeSpan.FromMilliseconds(elapsedMilliseconds), checksBeforeElapsed: 7);
        var rules = BrowserAdBlockTestRules.Parse("||a^", clock);

        Assert.That(rules.ShouldBlock(request, null, "script"), Is.EqualTo(blocked));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedRegexTimeoutsAbortTheWholeDecisionInsteadOfKeepingAPartialBlock(bool freezeRequestClock)
    {
        string text = "||example.test^\n" + string.Join('\n', Enumerable.Range(8, 64)
            .Select(count => "@@|https://example.test/" + string.Concat(Enumerable.Repeat("*a", count)) + "*b^"));
        // Freezing only the request clock proves the real regex timeout also fails open.
        var rules = BrowserAdBlockRules.Parse(text, freezeRequestClock ? new BrowserAdBlockTestRules.FrozenTimeProvider() : null);
        var url = new Uri("https://example.test/" + new string('a', 80) + "bx");
        var timer = Stopwatch.StartNew();
        bool blocked = rules.ShouldBlock(url, null, "script");
        timer.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.False, "An incomplete decision must not block a request.");
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "Regex timeouts must not accumulate across the candidate list.");
        });
    }

    private sealed class ElapsedTimeProvider(TimeSpan elapsed, int checksBeforeElapsed = 0) : TimeProvider
    {
        private bool _started;
        private int _checksBeforeElapsed = checksBeforeElapsed;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            if (_started && _checksBeforeElapsed-- <= 0) return TimeSpan.TicksPerSecond + elapsed.Ticks;
            _started = true;
            return TimeSpan.TicksPerSecond;
        }
    }
}
