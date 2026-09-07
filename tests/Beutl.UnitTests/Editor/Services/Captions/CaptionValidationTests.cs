using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionValidationTests
{
    [Test]
    public void Validate_OrderedAdjacentCues_HasNoIssues()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "first"),
            new CaptionCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "second"),
        ]);

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(document);

        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void Validate_InvalidTimingAndOrder_ReportsEachProblem()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4), "bad duration"),
            new CaptionCue(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), "negative and unordered"),
        ]);

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(document);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Some.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.EndNotAfterStart && issue.CueIndex == 0));
            Assert.That(issues, Has.Some.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.NegativeStart && issue.CueIndex == 1));
            Assert.That(issues, Has.Some.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.OutOfOrder
                && issue.CueIndex == 1
                && issue.RelatedCueIndex == 0));
        });
    }

    [Test]
    public void Validate_NestedOverlapInUnorderedDocument_ReportsOverlapAndOrder()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(10), "long"),
            new CaptionCue(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30), "later"),
            new CaptionCue(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "nested"),
        ]);

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(document);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.Some.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.Overlap
                && issue.CueIndex == 2
                && issue.RelatedCueIndex == 0));
            Assert.That(issues, Has.Some.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.OutOfOrder && issue.CueIndex == 2));
        });
    }

    [Test]
    public void Validate_TextConstraints_CountsGraphemeClustersAndNormalizedLines()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "a\u0301bcde\r\nok\rthird"),
        ]);
        var constraints = new CaptionTextConstraints(maximumLineLength: 4, maximumLineCount: 2);

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(document, constraints);

        Assert.Multiple(() =>
        {
            Assert.That(issues, Has.One.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.TooManyLines
                && issue.ActualValue == 3
                && issue.Limit == 2));
            Assert.That(issues, Has.One.Matches<CaptionValidationIssue>(issue =>
                issue.Kind == CaptionValidationIssueKind.LineTooLong
                && issue.LineIndex == 0
                && issue.ActualValue == 5
                && issue.Limit == 4));
        });
    }

    [TestCase(0, 2)]
    [TestCase(10, 0)]
    public void TextConstraints_NonPositiveLimit_Throws(int maximumLineLength, int maximumLineCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new CaptionTextConstraints(maximumLineLength, maximumLineCount));
    }
}
