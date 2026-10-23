using System.Net;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

public sealed partial class AiDialogWorkflowTests
{
    // A clip's transcription sends the clip's span of its file, from its offset and at its speed, in parts of
    // at most ten minutes, so the estimate prices that part of the file rather than the whole file. The cases: a
    // 30-second clip of an hour-long file, a clip that starts 40 seconds into a minute-long file, a 30-second clip at
    // double speed, and a 30-minute clip, priced one 10-minute part at a time.
    [AvaloniaTest]
    [TestCase(3600, 30, 0, 100, 30)]
    [TestCase(60, 50, 40, 100, 20)]
    [TestCase(3600, 30, 0, 200, 60)]
    [TestCase(3600, 1800, 0, 100, 600)]
    public async Task Transcription_estimate_for_a_file_prices_the_clip(
        double fileSeconds, double clipSeconds, double offsetSeconds, float speed, double expectedSeconds)
    {
        await TestReset.ResetShellAsync();
        var availability = new RecordingAvailabilityService();
        await using var api = new ApiScope(new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/api/v3/user/entitlements" => JsonResponse(HttpStatusCode.OK, EntitlementsJson()),
            _ => JsonResponse(HttpStatusCode.NotFound, "{}"),
        }));
        BeutlApiApplication clients = api.Clients;
        await using var viewModel = CreateSubtitleDialog(clients, availabilityService: availability);

        viewModel.SelectedAudioSource.Value = new AudioSourceItem(
            "clip",
            Path.Combine(BeutlHomeIsolation.CurrentHome!, "recording.wav"),
            MediaTime.FromSeconds(fileSeconds),
            elementLength: MediaTime.FromSeconds(clipSeconds),
            sourceOffset: MediaTime.FromSeconds(offsetSeconds),
            speed: speed);
        AiOperationAvailabilityRequest.Transcription[] requests = [];
        await WaitUntilAsync(() => (requests = availability.GetRequestsSnapshot()
            .OfType<AiOperationAvailabilityRequest.Transcription>().ToArray()).Length > 0);

        Assert.That(requests.Last().DurationSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    // The tab stays open while the clip is edited. An edit of its speed keyframes keeps the selected item, and the
    // estimate must still price the span the run sends. An edit of another element neither integrates the speed again
    // nor checks availability again (#3228).
    [AvaloniaTest]
    public async Task Transcription_estimate_follows_an_edit_of_the_clip_speed_keyframes()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-subtitle-speed-estimate");
        string sourcePath = Path.Combine(BeutlHomeIsolation.CurrentHome!, "speed-estimate-source.wav");
        WritePcmWave(sourcePath, sampleRate: 16_000, sampleCount: 16_000 * 60);
        var availability = new RecordingAvailabilityService();
        await using var api = new ApiScope(
            new StubHandler(request =>
                request.RequestUri?.AbsolutePath == "/api/v3/user/entitlements"
                    ? JsonResponse(HttpStatusCode.OK, EntitlementsJson())
                    : JsonResponse(HttpStatusCode.NotFound, "{}")),
            signIn: false);
        using var viewModel = CreateSubtitleDialog(api.Clients, editor, availabilityService: availability);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(sourcePath));
        var sound = new SourceSound { Source = { Value = soundSource } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = 100 } });
        sound.Speed.SetKeyFrameAnimation(animation);
        var element = new Element
        {
            Name = { Value = "Voice" },
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(10) },
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
                $"speed-estimate-{Guid.NewGuid():N}.belm")),
        };
        element.Objects.Add(sound);
        var other = new Element
        {
            Name = { Value = "Title" },
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(10) },
            ZIndex = { Value = 1 },
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(editor.Scene.Uri!.LocalPath)!,
                $"speed-estimate-other-{Guid.NewGuid():N}.belm")),
        };

        try
        {
            editor.Scene.AddChild(element, ElementOverlapHandling.Allow);
            editor.Scene.AddChild(other, ElementOverlapHandling.Allow);
            await WaitUntilAsync(() => viewModel.AudioSources.Value.Count == 2);
            viewModel.SelectedAudioSource.Value = viewModel.AudioSources.Value.Single(item => !item.IsSceneMix);
            await WaitUntilAsync(() => LastTranscriptionSeconds() is > 9.99 and < 10.01);

            // From 100 % to 300 % over the ten seconds, the clip plays twenty seconds of its file.
            ((KeyFrame<float>)animation.KeyFrames[1]).Value.Value = 300;
            await WaitUntilAsync(() => LastTranscriptionSeconds() is > 19.99 and < 20.01);
            int checks = TranscriptionChecks().Length;
            long recomputations = viewModel.SourceSpanRecomputations;
            long sceneEdits = viewModel.SceneAudioRevision;
            other.ZIndex.Value = 2;
            // Past the 250 ms the availability tracker waits before it checks.
            await Task.Delay(TimeSpan.FromMilliseconds(750));

            Assert.Multiple(() =>
            {
                Assert.That(recomputations, Is.GreaterThan(0));
                Assert.That(viewModel.SceneAudioRevision, Is.GreaterThan(sceneEdits));
                Assert.That(viewModel.SourceSpanRecomputations, Is.EqualTo(recomputations));
                Assert.That(TranscriptionChecks(), Has.Length.EqualTo(checks));
            });
        }
        finally
        {
            File.Delete(sourcePath);
        }

        AiOperationAvailabilityRequest.Transcription[] TranscriptionChecks()
            => availability.GetRequestsSnapshot().OfType<AiOperationAvailabilityRequest.Transcription>().ToArray();

        double? LastTranscriptionSeconds() => TranscriptionChecks().LastOrDefault()?.DurationSeconds;
    }
}
