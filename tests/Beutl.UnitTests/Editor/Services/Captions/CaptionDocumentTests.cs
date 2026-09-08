using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionDocumentTests
{
    [Test]
    public void CollectionEdits_UpdateDocumentWithoutExposingMutableList()
    {
        var first = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "first");
        var second = new CaptionCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "second");
        var replacement = new CaptionCue(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "replacement");
        var document = new CaptionDocument([first]);

        document.Add(second);
        document.Insert(1, replacement);
        document.Replace(0, second);
        CaptionCue removed = document.RemoveAt(1);

        Assert.Multiple(() =>
        {
            Assert.That(document.Cues, Is.Not.InstanceOf<List<CaptionCue>>());
            Assert.That(document.Cues, Is.EqualTo(new[] { second, second }));
            Assert.That(removed, Is.SameAs(replacement));
        });
    }

    [Test]
    public void SplitCue_RetainsMetadataAndSplitsTimeAndText()
    {
        var cue = new CaptionCue(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            "Hello world",
            "Alice",
            "en-US",
            Metadata((CaptionMetadataKeys.AssStyle, "Narration"), ("example.source-id", "cue-1")));
        var document = new CaptionDocument([cue]);

        (CaptionCue first, CaptionCue second) = document.SplitCue(
            0,
            TimeSpan.FromSeconds(3),
            6);

        Assert.Multiple(() =>
        {
            Assert.That(document.Count, Is.EqualTo(2));
            Assert.That(first, Is.EqualTo(new CaptionCue(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                "Hello ",
                "Alice",
                "en-US",
                Metadata((CaptionMetadataKeys.AssStyle, "Narration"), ("example.source-id", "cue-1")))));
            Assert.That(second, Is.EqualTo(new CaptionCue(
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(5),
                "world",
                "Alice",
                "en-US",
                Metadata((CaptionMetadataKeys.AssStyle, "Narration"), ("example.source-id", "cue-1")))));
        });
    }

    [Test]
    public void SplitCue_InsideUnicodeTextElement_ThrowsWithoutMutation()
    {
        var cue = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), "A😀B");
        var document = new CaptionDocument([cue]);

        Assert.Throws<ArgumentException>(() =>
            document.SplitCue(0, TimeSpan.FromSeconds(1), 2));

        Assert.That(document.Cues, Is.EqualTo(new[] { cue }));
    }

    [TestCase(0)]
    [TestCase(2)]
    public void SplitCue_AtCueBoundary_ThrowsWithoutMutation(int seconds)
    {
        var cue = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(2), "text");
        var document = new CaptionDocument([cue]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            document.SplitCue(0, TimeSpan.FromSeconds(seconds), 2));

        Assert.That(document.Cues, Is.EqualTo(new[] { cue }));
    }

    [Test]
    public void MergeWithNext_SharedMetadataIsRetained()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                "one",
                "Alice",
                "en",
                Metadata((CaptionMetadataKeys.AssStyle, "Default"), ("first-only", "value"))),
            new CaptionCue(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                "two",
                "Alice",
                "en",
                Metadata((CaptionMetadataKeys.AssStyle, "Default"), ("second-only", "value"))),
        ]);

        CaptionCue merged = document.MergeWithNext(0, " / ");

        Assert.Multiple(() =>
        {
            Assert.That(document.Count, Is.EqualTo(1));
            Assert.That(merged, Is.EqualTo(new CaptionCue(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(4),
                "one / two",
                "Alice",
                "en",
                Metadata((CaptionMetadataKeys.AssStyle, "Default")))));
        });
    }

    [Test]
    public void MergeWithNext_DifferingMetadataIsClearedAndRangeContainsBothCues()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(8),
                "one",
                "Alice",
                "en",
                Metadata((CaptionMetadataKeys.AssStyle, "A"), ("shared-key", "first"))),
            new CaptionCue(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(6),
                "two",
                "Bob",
                "ja",
                Metadata((CaptionMetadataKeys.AssStyle, "B"), ("shared-key", "second"))),
        ]);

        CaptionCue merged = document.MergeWithNext(0);

        Assert.Multiple(() =>
        {
            Assert.That(merged.Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(merged.End, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(merged.Text, Is.EqualTo("one\ntwo"));
            Assert.That(merged.Speaker, Is.Null);
            Assert.That(merged.Language, Is.Null);
            Assert.That(merged.Metadata, Is.Empty);
        });
    }

    [Test]
    public void Metadata_CopiesInputAndSetReturnsASeparateStructurallyEqualValue()
    {
        var source = new Dictionary<string, string>
        {
            ["example.id"] = "42",
        };
        var metadata = new CaptionMetadata(source);

        source["example.id"] = "changed";
        CaptionMetadata updated = metadata.Set(CaptionMetadataKeys.WebVttClasses, "warning");
        CaptionMetadata equivalent = Metadata(
            ("example.id", "42"),
            (CaptionMetadataKeys.WebVttClasses, "warning"));

        Assert.Multiple(() =>
        {
            Assert.That(metadata["example.id"], Is.EqualTo("42"));
            Assert.That(metadata.ContainsKey(CaptionMetadataKeys.WebVttClasses), Is.False);
            Assert.That(updated, Is.EqualTo(equivalent));
            Assert.That(updated.GetHashCode(), Is.EqualTo(equivalent.GetHashCode()));
        });
    }

    [Test]
    public void MergeWithNext_OnLastCue_ThrowsWithoutMutation()
    {
        var cue = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "only");
        var document = new CaptionDocument([cue]);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.MergeWithNext(0));

        Assert.That(document.Cues, Is.EqualTo(new[] { cue }));
    }

    private static CaptionMetadata Metadata(params (string Key, string Value)[] entries)
        => new(entries.Select(entry => KeyValuePair.Create(entry.Key, entry.Value)));
}
