using Beutl.Editor.Services.Captions;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EditableCaptionCueViewModelTests
{
    [Test]
    public void Cue_RoundTripsEditableFieldsAndLongTimelineTime()
    {
        var source = new CaptionCue(
            TimeSpan.FromHours(27) + TimeSpan.FromMilliseconds(125),
            TimeSpan.FromHours(27) + TimeSpan.FromSeconds(2.5),
            "Hello",
            "Narrator",
            "en",
            CaptionMetadata.Empty
                .Set(CaptionMetadataKeys.AssStyle, "Outlined")
                .Set("plugin.custom", "opaque"));
        var viewModel = new EditableCaptionCueViewModel(1, source);

        bool result = viewModel.TryCreateCue(out CaptionCue? restored);

        Assert.That(result, Is.True);
        Assert.That(restored, Is.EqualTo(source));
        Assert.That(viewModel.StartText, Is.EqualTo("27:00:00.125"));
    }

    [TestCase("bad", "00:00:01.000")]
    [TestCase("00:00:02.000", "00:00:01.000")]
    [TestCase("-00:00:01.000", "00:00:01.000")]
    public void InvalidTiming_IsRejected(string start, string end)
    {
        var viewModel = new EditableCaptionCueViewModel(
            1,
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Text"))
        {
            StartText = start,
            EndText = end,
        };

        Assert.That(viewModel.TryCreateCue(out _), Is.False);
    }
}
