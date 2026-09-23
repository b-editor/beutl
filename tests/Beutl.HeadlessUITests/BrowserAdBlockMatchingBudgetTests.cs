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
        var rules = BrowserAdBlockRules.Parse(text);
        Assert.That(rules.ShouldBlock(new Uri("https://ads.example/shared/0000/a/00/00"), null, "image"), Is.False);
    }

    [Test]
    public void ExcessivelyLongUrlsDoNotTriggerUnboundedIndexScanning()
    {
        var rules = BrowserAdBlockRules.Parse("||ads.example^");
        Assert.That(rules.ShouldBlock(new Uri("https://ads.example/" + new string('a', BrowserAdBlockRules.MaximumRequestUrlLength)), null, "image"), Is.False);
    }

    [Test]
    public void RepeatedRegexTimeoutsAbortTheWholeDecisionInsteadOfKeepingAPartialBlock()
    {
        string text = "||example.test^\n" + string.Join('\n', Enumerable.Range(8, 64)
            .Select(count => "@@|https://example.test/" + string.Concat(Enumerable.Repeat("*a", count)) + "*b^"));
        var rules = BrowserAdBlockRules.Parse(text);
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
}
