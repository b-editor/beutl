using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.JsonConverters;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// A scene end or a marker written by hand or read outside a project can fall up to a tick short of its frame; the
// timecode box, the Start command and stepping between markers still treat it as that frame (#2885).
[TestFixture]
public sealed class TickShortSceneBoundaryTests
{
    private static readonly string[] s_rates = ["30", "30000/1001"];

    [AvaloniaTest]
    public async Task The_timecode_box_takes_the_last_frame_of_a_scene_ending_a_tick_short([ValueSource(nameof(s_rates))] string rateText)
    {
        (EditViewModel editor, Rational rate) = await OpenAsync(rateText, frames: 2);

        bool last = editor.Player.TryParseTimecode("1f", out MediaTime target, out _);
        bool past = editor.Player.TryParseTimecode("2f", out _, out GotoTimecodeError error);

        Assert.Multiple(() =>
        {
            Assert.That(last, Is.True);
            Assert.That(target, Is.EqualTo(MediaTime.FromFrames(1, rate)));
            Assert.That(past, Is.False);
            Assert.That(error, Is.EqualTo(GotoTimecodeError.OutOfRange));
        });
    }

    [AvaloniaTest]
    public async Task Start_from_the_last_frame_of_a_scene_ending_a_tick_short_goes_to_its_start([ValueSource(nameof(s_rates))] string rateText)
    {
        (EditViewModel editor, Rational rate) = await OpenAsync(rateText, frames: 2);
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        clock.Seek(MediaTime.FromFrames(1, rate));

        editor.Player.Start.Execute();

        Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.Zero));
    }

    [AvaloniaTest]
    public async Task Stepping_back_from_a_marker_a_tick_short_reaches_the_one_before_it([ValueSource(nameof(s_rates))] string rateText)
    {
        (EditViewModel editor, Rational rate) = await OpenAsync(rateText, frames: 30);
        editor.Scene.Markers.Add(new SceneMarker(TickShort(MediaTime.FromFrames(1, rate)), "first"));
        editor.Scene.Markers.Add(new SceneMarker(TickShort(MediaTime.FromFrames(2, rate)), "second"));
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        clock.Seek(MediaTime.FromFrames(2, rate));

        editor.SeekToAdjacentMarker(forward: false);

        Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromFrames(1, rate)));
    }

    // A scene of this many frames whose end falls a little short of its frame.
    private static async Task<(EditViewModel Editor, Rational Rate)> OpenAsync(string rateText, int frames)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("tick-short-scene");
        Project project = editor.Scene.FindHierarchicalParent<Project>()!;
        project.FrameRate.Value = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        Rational rate = project.GetFrameRate();
        editor.Scene.Start.Value = MediaTime.Zero;
        editor.Scene.Duration.Value = TickShort(MediaTime.FromFrames(frames, rate));
        HeadlessTestHelpers.Settle();
        return (editor, rate);
    }

    // A time as its TimeSpan string reads outside a project: rounded down to a tick, then up to a flick.
    private static MediaTime TickShort(MediaTime time)
    {
        string text;
        using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            text = JsonSerializer.Serialize(time);
        return JsonSerializer.Deserialize<MediaTime>(text);
    }
}
