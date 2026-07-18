using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Linq;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class PreviewRenderErrorTests
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
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void AddFaultingDrawable(EditViewModel editor)
    {
        var adder = (IElementAdder)editor.GetRequiredService<IElementAdder>();
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new PreviewFaultDrawable()));
        editor.FrameCacheManager.Value.Clear();
    }

    [AvaloniaTest]
    public async Task QueuePreviewRender_WhenRenderingFails_ShowsPreviewErrorUntilRecoveryWithoutNotification()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preview-error");
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();

        try
        {
            NotificationService.Handler = notifications;
            var errorReported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.PreviewRenderError
                .Where(static message => !string.IsNullOrEmpty(message))
                .Take(1)
                .Subscribe(message => errorReported.TrySetResult(message!));

            AddFaultingDrawable(editor);
            editor.Player.QueuePreviewRender();

            string message = await errorReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(message, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
            Assert.That(notifications.Notifications, Is.Empty);

            var errorCleared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable recoverySubscription = editor.Player.PreviewRenderError
                .Where(static current => string.IsNullOrEmpty(current))
                .Take(1)
                .Subscribe(_ => errorCleared.TrySetResult(true));
            var structure = editor.GetRequiredService<IElementStructureService>();
            structure.Delete(editor.Scene, editor.Scene.Children.ToArray());
            editor.FrameCacheManager.Value.Clear();
            editor.Player.QueuePreviewRender();

            await errorCleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(editor.Player.PreviewRenderError.Value, Is.Null);
            Assert.That(notifications.Notifications, Is.Empty);
        }
        finally
        {
            NotificationService.Handler = previousHandler;
        }
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_WhenRenderingFails_StoresFailureWithoutNotification()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("buffered-preview-error");
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();

        try
        {
            NotificationService.Handler = notifications;
            AddFaultingDrawable(editor);
            using var isPlaying = new ReactivePropertySlim<bool>(true);
            using var player = new BufferedPlayer(
                editor, editor.Scene, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None);

            player.Start();
            await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5));

            Assert.That(player.Failure, Is.Not.Null);
            Assert.That(player.Failure!.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(player.Failure.Exception.Message, Is.EqualTo(PreviewFaultDrawable.ErrorMessage));
            Assert.That(player.Failure.Frame, Is.EqualTo(0));
            Assert.That(notifications.Notifications, Is.Empty);
        }
        finally
        {
            NotificationService.Handler = previousHandler;
        }
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_WhenDrawableThrowsObjectDisposed_StoresFailure()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("buffered-object-disposed-error");
        var adder = (IElementAdder)editor.GetRequiredService<IElementAdder>();
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new PreviewObjectDisposedFaultDrawable()));
        editor.FrameCacheManager.Value.Clear();
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, editor.Scene, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None);

        player.Start();
        await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5));

        Assert.That(player.Failure, Is.Not.Null);
        Assert.That(player.Failure!.Exception, Is.TypeOf<ObjectDisposedException>());
        Assert.That(player.Failure.Frame, Is.EqualTo(0));
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_DoesNotStartForACanceledPlaybackSession()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("canceled-buffered-player");
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var playbackCts = new CancellationTokenSource();
        playbackCts.Cancel();
        using var player = new BufferedPlayer(
            editor,
            editor.Scene,
            isPlaying,
            editor.Player.GetFrameRate(),
            playbackCts.Token);

        player.Start();
        await WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(player.Failure, Is.Null);
            Assert.That(player.TryDequeue(out _), Is.False);
        });
    }

    [AvaloniaTest]
    public async Task Play_WhenRenderingFails_ShowsPreviewErrorAndStopsWithoutNotification()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("playing-preview-error");
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new CaptureNotificationHandler();

        try
        {
            NotificationService.Handler = notifications;
            AddFaultingDrawable(editor);
            var errorReported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.PreviewRenderError
                .Where(static message => !string.IsNullOrEmpty(message))
                .Take(1)
                .Subscribe(message => errorReported.TrySetResult(message!));

            editor.Player.Play();

            string message = await errorReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => !editor.Player.IsPlaying.Value, TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(message, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
                Assert.That(editor.Player.IsPlaying.Value, Is.False);
                Assert.That(editor.FrameCacheManager.Value.CurrentFrame, Is.EqualTo(0));
                Assert.That(notifications.Notifications, Is.Empty);
            });
        }
        finally
        {
            await editor.Player.Pause();
            NotificationService.Handler = previousHandler;
        }
    }

    [AvaloniaTest]
    public async Task PlaybackRenderFailure_UpdatesPreviewStateAndStopsPlayback()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("playback-preview-error");
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        const int frame = 7;
        const int rate = 30;
        editor.Player.IsPlaying.Value = true;

        bool applied = editor.Player.ApplyPlaybackRenderFailure(
            new BufferedPlayer.RenderFailure(new InvalidOperationException(PreviewFaultDrawable.ErrorMessage), frame),
            rate,
            static () => true);
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(editor.Player.IsPlaying.Value, Is.False);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds((double)frame / rate)));
            Assert.That(editor.FrameCacheManager.Value.CurrentFrame, Is.EqualTo(frame));
            Assert.That(
                editor.Player.PreviewRenderError.Value,
                Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
        });
    }

    [AvaloniaTest]
    public async Task SupersededRenderFailure_DoesNotReplaceTheNewerSuccessfulPreview()
    {
        GpuTestGate.EnsureAvailable();
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("superseded-preview-error");
        var drawable = new SupersededPreviewFaultDrawable();
        var adder = (IElementAdder)editor.GetRequiredService<IElementAdder>();
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => drawable));
        editor.FrameCacheManager.Value.Clear();

        try
        {
            editor.Player.QueuePreviewRender();
            await drawable.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var rendered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.AfterRendered
                .Take(1)
                .Subscribe(_ => rendered.TrySetResult(true));
            editor.Player.QueuePreviewRender();
            drawable.ReleaseFirstRender();

            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.That(editor.Player.PreviewRenderError.Value, Is.Null);
        }
        finally
        {
            drawable.ReleaseFirstRender();
        }
    }

    [AvaloniaTest]
    public async Task PlayerView_TracksPreviewRenderErrorVisibility()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preview-error-overlay");
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 640, Height = 480 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Panel framePanel = view.FindControl<Panel>("framePanel")!;
            Border overlay = view.FindControl<Border>("previewRenderErrorOverlay")!;
            TextBlock message = view.FindControl<TextBlock>("previewRenderErrorMessage")!;
            Assert.That(framePanel, Is.Not.Null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(message, Is.Not.Null);
            Assert.That(overlay.Parent, Is.SameAs(framePanel.Parent),
                "The error overlay must remain a sibling of the transformed preview panel.");
            Assert.That(
                AutomationProperties.GetLiveSetting(message),
                Is.EqualTo(AutomationLiveSetting.Assertive));
            Assert.That(overlay.IsVisible, Is.False);

            editor.Player.SetPreviewRenderError(Beutl.Language.MessageStrings.FrameDrawingException);
            HeadlessTestHelpers.Render();

            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(message.Text, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
            Assert.That(
                AutomationProperties.GetName(message),
                Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));

            editor.Player.SetPreviewRenderError(null);
            HeadlessTestHelpers.Render();

            Assert.That(overlay.IsVisible, Is.False);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task PreviewRenderError_LatestQueuedUpdateWins()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("preview-error-order");

        await Task.Run(() =>
        {
            editor.Player.SetPreviewRenderError("superseded");
            editor.Player.SetPreviewRenderError(Beutl.Language.MessageStrings.FrameDrawingException);
        });
        HeadlessTestHelpers.Settle();

        Assert.That(
            editor.Player.PreviewRenderError.Value,
            Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed >= timeout)
            {
                Assert.Fail($"Condition was not met within {timeout}.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public ConcurrentQueue<Notification> Notifications { get; } = new();

        public void Show(Notification notification) => Notifications.Enqueue(notification);
    }
}

internal sealed partial class PreviewFaultDrawable : Drawable
{
    public const string ErrorMessage = "preview render failed";

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => throw new InvalidOperationException(ErrorMessage);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class SupersededPreviewFaultDrawable : Drawable
{
    private readonly TaskCompletionSource<bool> _renderEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseFirstRender =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _renderCount;

    public TaskCompletionSource<bool> RenderEntered => _renderEntered;

    public void ReleaseFirstRender() => _releaseFirstRender.TrySetResult(true);

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (Interlocked.Increment(ref _renderCount) == 1)
        {
            _renderEntered.TrySetResult(true);
            _releaseFirstRender.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException(PreviewFaultDrawable.ErrorMessage);
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class PreviewObjectDisposedFaultDrawable : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => throw new ObjectDisposedException(nameof(PreviewObjectDisposedFaultDrawable));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}
