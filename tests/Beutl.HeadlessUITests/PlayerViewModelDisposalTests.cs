using System.Reactive.Linq;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Regression tests for the abandoned-playback-task disposal race: a Pause() timeout can leave an
// audio backend task running while the player is disposed, and that task can still publish audio
// snapshots after teardown began.
[TestFixture]
public class PlayerViewModelDisposalTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task Publishing_audio_after_dispose_is_safe_for_abandoned_playback_tasks()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("player-dispose");
        PlayerViewModel player = editor.Player;

        // The normal editor-close path disposes the player (the same path ResetShellAsync uses).
        await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!);
        HeadlessTestHelpers.Settle();

        // An audio backend task abandoned by a Pause() timeout can still reach
        // PublishAudioSnapshot after disposal; it must not throw on the torn-down player.
        using var pcm = new Pcm<Stereo32BitFloat>(44100, 64);
        Assert.DoesNotThrow(() => player.PublishAudioSnapshot(pcm, TimeSpan.Zero));

        // Teardown must complete (not dispose) the audio-frame subject: a straggler OnNext that
        // slipped past the _isDisposing snapshot is dropped silently by Rx, whereas OnNext on a
        // disposed ReplaySubject throws ObjectDisposedException. A fresh subscriber to a
        // completed subject observes OnCompleted immediately; a disposed one would throw here.
        bool completed = false;
        ((IPreviewPlayer)player).AudioFramePushed.Subscribe(_ => { }, () => completed = true);
        Assert.That(completed, Is.True,
            "AudioFramePushed must be completed, not disposed, after DisposeAsync");
    }
}
