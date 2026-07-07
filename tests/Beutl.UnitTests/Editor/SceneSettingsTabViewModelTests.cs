using System.Collections.Concurrent;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;

using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class SceneSettingsTabViewModelTests
{
    // NotificationService.Handler is a process-global, write-once facade (set by the app at startup,
    // which does not run under test). Install a capturing handler so the Apply path's ShowWarning is
    // observable; Beutl.Core grants InternalsVisibleTo to reach the internal setter.
    private static readonly CaptureNotificationHandler s_notificationHandler = new();

    [OneTimeSetUp]
    public void InstallNotificationHandler()
    {
        NotificationService.Handler = s_notificationHandler;
        // The setter is write-once (??=); if another fixture installed a handler first this no-ops
        // and the queue stays empty. Fail loudly instead of as a confusing false-negative in a test.
        Assert.That(NotificationService.Handler, Is.SameAs(s_notificationHandler),
            "Another fixture already installed a NotificationService handler; "
            + "SceneSettings notifications will not be captured.");
    }

    [SetUp]
    public void ClearCapturedNotifications()
    {
        s_notificationHandler.Clear();
    }

    [Test]
    public async Task Apply_WhenInputsChangeWhilePausing_CommitsLatestInputs()
    {
        var scene = new Scene(640, 480, string.Empty)
        {
            Start = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(5),
        };
        var sceneSettingsService = new CaptureSceneSettingsService();
        var timelineOptionsProvider = new TestTimelineOptionsProvider(scene);
        var previewPlayer = new BlockingPreviewPlayer();
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService(scene);
        editorContext.AddService<ISceneSettingsService>(sceneSettingsService);
        editorContext.AddService<ITimelineOptionsProvider>(timelineOptionsProvider);
        editorContext.AddService<IPreviewPlayer>(previewPlayer);

        using var viewModel = new SceneSettingsTabViewModel(editorContext)
        {
            Width =
            {
                Value = 800
            },
            Height =
            {
                Value = 600
            },
            StartInput =
            {
                Value = TimeSpan.FromSeconds(2).ToString()
            },
            DurationInput =
            {
                Value = TimeSpan.FromSeconds(10).ToString()
            },
        };

        Task applyTask = viewModel.Apply.ExecuteAsync();
        await previewPlayer.PauseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var latestFrameSize = new PixelSize(1280, 720);
        TimeSpan latestStart = TimeSpan.FromSeconds(3);
        TimeSpan latestDuration = TimeSpan.FromSeconds(12);
        viewModel.Width.Value = latestFrameSize.Width;
        viewModel.Height.Value = latestFrameSize.Height;
        viewModel.StartInput.Value = latestStart.ToString();
        viewModel.DurationInput.Value = latestDuration.ToString();

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
    public async Task Apply_WhenInputsBecomeInvalidWhilePausing_DoesNotCommitAndNotifies(
        int width, int height, string start, string duration)
    {
        var scene = new Scene(640, 480, string.Empty)
        {
            Start = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(5),
        };
        var sceneSettingsService = new CaptureSceneSettingsService();
        var timelineOptionsProvider = new TestTimelineOptionsProvider(scene);
        var previewPlayer = new BlockingPreviewPlayer();
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService(scene);
        editorContext.AddService<ISceneSettingsService>(sceneSettingsService);
        editorContext.AddService<ITimelineOptionsProvider>(timelineOptionsProvider);
        editorContext.AddService<IPreviewPlayer>(previewPlayer);

        using var viewModel = new SceneSettingsTabViewModel(editorContext)
        {
            Width =
            {
                Value = 800
            },
            Height =
            {
                Value = 600
            },
            StartInput =
            {
                Value = TimeSpan.FromSeconds(2).ToString()
            },
            DurationInput =
            {
                Value = TimeSpan.FromSeconds(10).ToString()
            },
        };

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
                s_notificationHandler.Contains(Beutl.Language.MessageStrings.SceneSettings_ApplyCanceledInputsInvalid),
                Is.True,
                "Apply must warn the user instead of cancelling silently when inputs become invalid during the pause");
        });
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        // Fully qualified: System.Reactive also defines a Notification type, so the bare name is ambiguous.
        private readonly ConcurrentQueue<Beutl.Services.Notification> _notifications = new();

        public void Show(Beutl.Services.Notification notification)
        {
            _notifications.Enqueue(notification);
        }

        public bool Contains(string message)
        {
            return _notifications.Any(n => n.Message == message);
        }

        public void Clear()
        {
            _notifications.Clear();
        }
    }

    private sealed class TestEditorContext(CoreObject obj) : IEditorContext
    {
        private readonly Dictionary<Type, object> _services = [];

        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public void AddService<T>(T service)
            where T : notnull
        {
            _services[typeof(T)] = service;
        }

        public object? GetService(Type serviceType)
        {
            return _services.GetValueOrDefault(serviceType);
        }

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return default;
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return default;
        }

        public bool OpenToolTab(IToolContext item)
        {
            return false;
        }

        public void CloseToolTab(IToolContext item)
        {
        }
    }

    private sealed class TestTimelineOptionsProvider(Scene scene) : ITimelineOptionsProvider
    {
        public Scene Scene { get; } = scene;

        public IReactiveProperty<TimelineOptions> Options { get; } = new ReactiveProperty<TimelineOptions>(
            new TimelineOptions(1, Vector2.Zero, 4));

        public IObservable<float> Scale => Options.Select(x => x.Scale);

        public IObservable<Vector2> Offset => Options.Select(x => x.Offset);
    }

    private sealed class BlockingPreviewPlayer : IPreviewPlayer
    {
        private readonly TaskCompletionSource _pauseCompletion = new();

        public TaskCompletionSource PauseStarted { get; } = new();

        public IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; } =
            new ReactiveProperty<Ref<Bitmap>?>((Ref<Bitmap>?)null);

        public IObservable<Unit> AfterRendered => Observable.Empty<Unit>();

        public IReadOnlyReactiveProperty<bool> IsPlaying { get; } = new ReactiveProperty<bool>(true);

        public async Task Pause()
        {
            PauseStarted.SetResult();
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

        public TimeSpan Start { get; private set; }

        public TimeSpan Duration { get; private set; }

        public bool Apply(
            Scene scene,
            PixelSize frameSize,
            TimeSpan start,
            TimeSpan duration)
        {
            CallCount++;
            FrameSize = frameSize;
            Start = start;
            Duration = duration;
            return true;
        }
    }
}
