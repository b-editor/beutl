using Beutl.Extensions.FFmpeg.PropertyEditors;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class AudioFormatDecisionTests
{
    [Test]
    public void ResolveSupportedFormats_NonDegraded_ReturnsItemsVerbatim()
    {
        AudioFormat[] items = [AudioFormat.S16, AudioFormat.Fltp];

        AudioFormat[] resolved = AudioFormatOptions.ResolveSupported(
            new OptionsQueryResult<AudioFormat>(items, Degraded: false));

        Assert.That(resolved, Is.EqualTo(items));
    }

    [Test]
    public void ResolveSupportedFormats_Degraded_ReturnsEveryFormatExceptDefault()
    {
        AudioFormat[] resolved = AudioFormatOptions.ResolveSupported(
            new OptionsQueryResult<AudioFormat>([], Degraded: true));

        AudioFormat[] expected = Enum.GetValues<AudioFormat>()
            .Where(f => f != AudioFormat.Default)
            .ToArray();
        Assert.That(resolved, Is.EqualTo(expected));
        Assert.That(resolved, Is.Not.Empty);
        Assert.That(resolved, Does.Not.Contain(AudioFormat.Default));
    }

    // The point of the degraded->all rule: a degraded result must NOT collapse the list to empty,
    // because an empty list would make the editor reset the user's current format to Default. Showing
    // every format keeps the selection. This composes ResolveSupportedFormats with CodecFormatSelection
    // exactly as AudioFormatEditorViewModel.UpdateAsync/ApplyFormats do.
    [Test]
    public void DegradedResult_PreservesCurrentSelection_InsteadOfResettingToDefault()
    {
        var degraded = new OptionsQueryResult<AudioFormat>([], Degraded: true);

        AudioFormat[] supported = AudioFormatOptions.ResolveSupported(degraded);
        AudioFormat[] withSentinel = [AudioFormat.Default, .. supported];
        FormatSelectionResult selection =
            CodecFormatSelection.Resolve(withSentinel, AudioFormat.Fltp, AudioFormat.Default);

        Assert.That(selection.ResetToSentinel, Is.False, "degraded must keep the current format selected");
        Assert.That(withSentinel[selection.SelectedIndex], Is.EqualTo(AudioFormat.Fltp));
    }

    // Contrast: had the degraded branch instead applied the empty payload, the same current format
    // would be unsupported and the editor would reset it to Default.
    [Test]
    public void EmptyFormatList_WouldResetCurrentToDefault()
    {
        AudioFormat[] withSentinel = [AudioFormat.Default];

        FormatSelectionResult selection =
            CodecFormatSelection.Resolve(withSentinel, AudioFormat.Fltp, AudioFormat.Default);

        Assert.That(selection.ResetToSentinel, Is.True);
    }
}
