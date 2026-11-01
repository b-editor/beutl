using System.Globalization;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;

using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class SceneSettingsTabViewModelTests
{
    [Test]
    public async Task Apply_WhenInputsChangeWhilePausing_CommitsLatestInputs()
    {
        using SceneSettingsTabViewModel viewModel = CreateViewModel(
            out CaptureSceneSettingsService sceneSettingsService,
            out BlockingPreviewPlayer previewPlayer);

        Task applyTask = viewModel.Apply.ExecuteAsync();
        await previewPlayer.PauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var latestFrameSize = new PixelSize(1280, 720);
        MediaTime latestStart = MediaTime.FromSeconds(3);
        MediaTime latestDuration = MediaTime.FromSeconds(12);
        viewModel.Width.Value = latestFrameSize.Width;
        viewModel.Height.Value = latestFrameSize.Height;
        viewModel.StartInput.Value = latestStart.ToTimeSpan().ToString();
        viewModel.DurationInput.Value = latestDuration.ToTimeSpan().ToString();

        previewPlayer.CompletePause();
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(sceneSettingsService.CallCount, Is.EqualTo(1));
            Assert.That(sceneSettingsService.FrameSize, Is.EqualTo(latestFrameSize));
            Assert.That(sceneSettingsService.Start, Is.EqualTo(latestStart));
            Assert.That(sceneSettingsService.Duration, Is.EqualTo(latestDuration));
        });
    }

    [TestCase(0, 600, "00:00:03", "00:00:12", TestName = "Apply_DoesNotCommitAndNotifies_WhenWidthBecomesZeroWhilePausing")]
    [TestCase(800, 0, "00:00:03", "00:00:12", TestName = "Apply_DoesNotCommitAndNotifies_WhenHeightBecomesZeroWhilePausing")]
    [TestCase(800, 600, "-00:00:01", "00:00:12", TestName = "Apply_DoesNotCommitAndNotifies_WhenStartBecomesNegativeWhilePausing")]
    [TestCase(800, 600, "00:00:03", "00:00:00", TestName = "Apply_DoesNotCommitAndNotifies_WhenDurationBecomesZeroWhilePausing")]
    [NonParallelizable]
    public async Task Apply_WhenInputsBecomeInvalidWhilePausing_DoesNotCommitAndNotifies(
        int width, int height, string start, string duration)
    {
        using SceneSettingsTabViewModel viewModel = CreateViewModel(
            out CaptureSceneSettingsService sceneSettingsService,
            out BlockingPreviewPlayer previewPlayer);
        using var notifications = NotificationCapture.Install();

        Task applyTask = viewModel.Apply.ExecuteAsync();
        await previewPlayer.PauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Width.Value = width;
        viewModel.Height.Value = height;
        viewModel.StartInput.Value = start;
        viewModel.DurationInput.Value = duration;

        previewPlayer.CompletePause();
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(sceneSettingsService.CallCount, Is.EqualTo(0));
            Assert.That(
                notifications.All.Any(n => n.Message == Beutl.Language.MessageStrings.SceneSettings_ApplyCanceledInputsInvalid),
                Is.True,
                "Apply must warn the user instead of cancelling silently when inputs become invalid during the pause");
        });
    }

    // A pause still draining the last frame no longer shows in IsPlaying, so applying waits for Pause anyway.
    [Test]
    public async Task Apply_WaitsForAPauseThatIsPlayingNoLongerShows()
    {
        using SceneSettingsTabViewModel viewModel = CreateViewModel(
            out CaptureSceneSettingsService sceneSettingsService,
            out BlockingPreviewPlayer previewPlayer,
            isPlaying: false);

        Task applyTask = viewModel.Apply.ExecuteAsync();
        await previewPlayer.PauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(sceneSettingsService.CallCount, Is.Zero);

        previewPlayer.CompletePause();
        await applyTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(sceneSettingsService.CallCount, Is.EqualTo(1));
    }

    // A scene on frames keeps its times when another setting changes, and typed frame times land on its frames (#2885).
    [TestCase("30")]
    [TestCase("30000/1001")]
    public async Task Apply_keeps_the_times_left_as_shown_and_takes_typed_times_on_the_frame_grid(string rateText)
    {
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        var scene = new Scene(640, 480, string.Empty)
        {
            Start = { Value = MediaTime.FromFrames(1, rate) },
            Duration = { Value = MediaTime.FromFrames(301, rate) },
        };
        var project = new Project();
        project.FrameRate.Value = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        project.Items.Add(scene);
        using SceneSettingsTabViewModel viewModel = CreateViewModel(
            scene, out CaptureSceneSettingsService sceneSettingsService, out BlockingPreviewPlayer previewPlayer);
        previewPlayer.CompletePause();

        viewModel.Width.Value = 800;
        await viewModel.Apply.ExecuteAsync();
        (MediaTime Start, MediaTime Duration) kept = (sceneSettingsService.Start, sceneSettingsService.Duration);
        viewModel.StartInput.Value = Truncated(MediaTime.FromFrames(2, rate));
        viewModel.DurationInput.Value = Truncated(MediaTime.FromFrames(600, rate));
        await viewModel.Apply.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo((MediaTime.FromFrames(1, rate), MediaTime.FromFrames(301, rate))));
            Assert.That(sceneSettingsService.Start, Is.EqualTo(MediaTime.FromFrames(2, rate)));
            Assert.That(sceneSettingsService.Duration, Is.EqualTo(MediaTime.FromFrames(600, rate)));
        });

        // A frame time as earlier versions showed it, rounded down to a tick.
        static string Truncated(MediaTime time) => TimeSpan.FromTicks(time.ToTicks(MidpointRounding.ToNegativeInfinity)).ToString();
    }

    private static SceneSettingsTabViewModel CreateViewModel(
        out CaptureSceneSettingsService sceneSettingsService,
        out BlockingPreviewPlayer previewPlayer,
        bool isPlaying = true)
    {
        var scene = new Scene(640, 480, string.Empty)
        {
            Start = { Value = MediaTime.FromSeconds(1) },
            Duration = { Value = MediaTime.FromSeconds(5) },
        };
        SceneSettingsTabViewModel viewModel = CreateViewModel(scene, out sceneSettingsService, out previewPlayer, isPlaying);
        viewModel.Width.Value = 800;
        viewModel.Height.Value = 600;
        viewModel.StartInput.Value = TimeSpan.FromSeconds(2).ToString();
        viewModel.DurationInput.Value = TimeSpan.FromSeconds(10).ToString();
        return viewModel;
    }

    private static SceneSettingsTabViewModel CreateViewModel(
        Scene scene,
        out CaptureSceneSettingsService sceneSettingsService,
        out BlockingPreviewPlayer previewPlayer,
        bool isPlaying = true)
    {
        sceneSettingsService = new CaptureSceneSettingsService();
        previewPlayer = new BlockingPreviewPlayer(isPlaying);
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService(scene);
        editorContext.AddService<ISceneSettingsService>(sceneSettingsService);
        editorContext.AddService<ITimelineOptionsProvider>(new TestTimelineOptionsProvider(scene));
        editorContext.AddService<IPreviewPlayer>(previewPlayer);
        return new SceneSettingsTabViewModel(editorContext);
    }

    private sealed class TestTimelineOptionsProvider(Scene scene) : ITimelineOptionsProvider
    {
        public Scene Scene { get; } = scene;

        public IReactiveProperty<TimelineOptions> Options { get; } = new ReactiveProperty<TimelineOptions>(
            new TimelineOptions { MaxLayerCount = 4 });

        public IObservable<float> Scale => Options.Select(x => x.Scale);

        public IObservable<Vector2> Offset => Options.Select(x => x.Offset);
    }

    private sealed class BlockingPreviewPlayer(bool isPlaying) : IPreviewPlayer
    {
        private readonly TaskCompletionSource _pauseCompletion = new();

        public TaskCompletionSource PauseStarted { get; } = new();

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; } =
            new ReactiveProperty<Ref<Bitmap>?>((Ref<Bitmap>?)null);

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying { get; } = new ReactiveProperty<bool>(isPlaying);

        public async Task Pause()
        {
            PauseStarted.TrySetResult();
            await _pauseCompletion.Task;
        }

        public void CompletePause()
        {
            _pauseCompletion.SetResult();
        }
    }

    private sealed class CaptureSceneSettingsService : ISceneSettingsService
    {
        public int CallCount { get; private set; }

        public PixelSize FrameSize { get; private set; }

        public MediaTime Start { get; private set; }

        public MediaTime Duration { get; private set; }

        public bool Apply(Scene scene, PixelSize frameSize, MediaTime start, MediaTime duration, SpeakerLayout channelLayout)
            => Apply(scene, frameSize, start, duration);

        public bool Apply(
            Scene scene,
            PixelSize frameSize,
            MediaTime start,
            MediaTime duration)
        {
            CallCount++;
            FrameSize = frameSize;
            Start = start;
            Duration = duration;
            return true;
        }
    }
}
