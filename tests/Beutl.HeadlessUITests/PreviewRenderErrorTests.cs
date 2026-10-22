using System.Reactive.Linq;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Models;
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
    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(name, 320, 240);
        // The scene-open preview render is dispatched asynchronously to the render thread;
        // when it succeeds it queues a clear of PreviewRenderError. Drain it (and the queued
        // clear) so a manual error set below is guaranteed to be the latest update.
        RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
        return editor;
    }

    private static async Task AddDrawableAsync(EditViewModel editor, Drawable drawable)
    {
        var adder = (IElementAdder)editor.GetRequiredService<IElementAdder>();
        await adder.AddAsync([new ElementDescription(
            Start: MediaTime.Zero,
            Length: MediaTime.FromSeconds(2),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => drawable))],
            CancellationToken.None);
    }

    [AvaloniaTest]
    public async Task QueuePreviewRender_WhenRenderingFails_ShowsPreviewErrorUntilRecoveryWithoutNotification()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("preview-error");
        using var notifications = new NotificationCapture();

        var errorReported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = editor.Player.PreviewRenderError
            .Where(static message => !string.IsNullOrEmpty(message))
            .Take(1)
            .Subscribe(message => errorReported.TrySetResult(message!));

        await AddDrawableAsync(editor, new PreviewFaultDrawable());
        editor.FrameCacheManager.Value.Clear();
        editor.Player.QueuePreviewRender();

        string message = await errorReported.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(message, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
        Assert.That(notifications.All, Is.Empty);

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
        Assert.That(notifications.All, Is.Empty);
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_WhenRenderingFails_StoresFailureWithoutNotification()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("buffered-preview-error");
        using var notifications = new NotificationCapture();

        await AddDrawableAsync(editor, new PreviewFaultDrawable());
        editor.FrameCacheManager.Value.Clear();
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None);

        player.Start(PlaybackRangeOf(editor));
        await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5), pump: false);

        Assert.That(player.Failure, Is.Not.Null);
        Assert.That(player.Failure!.Exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(player.Failure.Exception.Message, Is.EqualTo(PreviewFaultDrawable.ErrorMessage));
        Assert.That(player.Failure.Frame, Is.EqualTo(0));
        Assert.That(notifications.All, Is.Empty);
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_WhenDrawableThrowsObjectDisposed_StoresFailure()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("buffered-object-disposed-error");
        await AddDrawableAsync(editor, new PreviewObjectDisposedFaultDrawable());
        editor.FrameCacheManager.Value.Clear();
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var player = new BufferedPlayer(
            editor, isPlaying, editor.Player.GetFrameRate(), CancellationToken.None);

        player.Start(PlaybackRangeOf(editor));
        await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5), pump: false);

        Assert.That(player.Failure, Is.Not.Null);
        Assert.That(player.Failure!.Exception, Is.TypeOf<ObjectDisposedException>());
        Assert.That(player.Failure.Frame, Is.EqualTo(0));
    }

    [AvaloniaTest]
    public async Task BufferedPlayer_DoesNotStartForACanceledPlaybackSession()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("canceled-buffered-player");
        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var playbackCts = new CancellationTokenSource();
        playbackCts.Cancel();
        using var player = new BufferedPlayer(
            editor,
            isPlaying,
            editor.Player.GetFrameRate(),
            playbackCts.Token);

        player.Start(PlaybackRangeOf(editor));
        await HeadlessTestHelpers.WaitUntilAsync(() => player.ProducerStopped, TimeSpan.FromSeconds(5), pump: false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Failure, Is.Null);
            Assert.That(player.TryDequeue(out _), Is.False);
        });
    }

    [AvaloniaTest]
    [TestCase("canceled")]
    [TestCase("disposed")]
    public async Task BufferedPlayer_WhenStoppedDuringRendering_DoesNotStoreFailure(string stop)
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene($"{stop}-rendering-buffered-player");
        var drawable = new BlockingPreviewFaultDrawable();
        await AddDrawableAsync(editor, drawable);
        HeadlessTestHelpers.Settle();
        RenderThread.Dispatcher.Invoke(static () => { });
        editor.FrameCacheManager.Value.Clear();
        drawable.Arm();

        using var isPlaying = new ReactivePropertySlim<bool>(true);
        using var playbackCts = new CancellationTokenSource();
        using var player = new BufferedPlayer(
            editor,
            isPlaying,
            editor.Player.GetFrameRate(),
            stop == "canceled" ? playbackCts.Token : CancellationToken.None);

        try
        {
            player.Start(PlaybackRangeOf(editor));
            await drawable.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            if (stop == "canceled")
                playbackCts.Cancel();
            else
                player.Dispose();

            drawable.ReleaseRender();

            await HeadlessTestHelpers.WaitUntilAsync(
                () => player.ProducerStopped, TimeSpan.FromSeconds(5), pump: false);
            Assert.That(player.Failure, Is.Null);
        }
        finally
        {
            drawable.ReleaseRender();
        }
    }

    [AvaloniaTest]
    public async Task Play_WhenRenderingFails_ShowsPreviewErrorAndStopsWithoutRenderNotification()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("playing-preview-error");
        using var notifications = new NotificationCapture();

        try
        {
            await AddDrawableAsync(editor, new PreviewFaultDrawable());
            editor.FrameCacheManager.Value.Clear();
            var errorReported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.PreviewRenderError
                .Where(static message => !string.IsNullOrEmpty(message))
                .Take(1)
                .Subscribe(message => errorReported.TrySetResult(message!));

            editor.Player.Play();

            string message = await errorReported.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await HeadlessTestHelpers.WaitUntilAsync(
                () => !editor.Player.IsPlaying.Value, TimeSpan.FromSeconds(5), pump: false);

            Assert.Multiple(() =>
            {
                Assert.That(message, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
                Assert.That(editor.Player.IsPlaying.Value, Is.False);
                Assert.That(editor.FrameCacheManager.Value.CurrentFrame, Is.EqualTo(0));
                Assert.That(
                    notifications.All.Select(static notification => notification.Message),
                    Does.Not.Contain(Beutl.Language.MessageStrings.FrameDrawingException));
            });
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    public async Task PlaybackRenderFailure_UpdatesPreviewStateAndStopsPlayback()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("playback-preview-error");
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        const int frame = 7;
        const int rate = 30;
        editor.Player.IsPlaying.Value = true;

        bool applied = editor.Player.ApplyPlaybackRenderFailure(
            new BufferedPlayer.RenderFailure(new InvalidOperationException(PreviewFaultDrawable.ErrorMessage), frame),
            new Rational(rate),
            static () => true);
        // Check the synchronous failure state before pumping queued work: updating the clock
        // schedules a preview, and a successful recovery render may clear the error again.

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(editor.Player.IsPlaying.Value, Is.False);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds((double)frame / rate)));
            Assert.That(editor.FrameCacheManager.Value.CurrentFrame, Is.EqualTo(frame));
            Assert.That(
                editor.Player.PreviewRenderError.Value,
                Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
        });
    }

    [AvaloniaTest]
    public async Task SupersededRenderFailure_DoesNotReplaceTheNewerSuccessfulPreview()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("superseded-preview-error");
        var drawable = new BlockingPreviewFaultDrawable();
        drawable.Arm();
        await AddDrawableAsync(editor, drawable);
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
            drawable.ReleaseRender();

            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            HeadlessTestHelpers.Settle();

            Assert.That(editor.Player.PreviewRenderError.Value, Is.Null);
        }
        finally
        {
            drawable.ReleaseRender();
        }
    }

    [AvaloniaTest]
    public async Task PlayerView_TracksPreviewRenderErrorVisibility()
    {
        TestGpu.RequireAnyDevice();
        await TestReset.ResetShellAsync();
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

            editor.Player.ApplyPlaybackRenderFailure(
                new BufferedPlayer.RenderFailure(new InvalidOperationException(PreviewFaultDrawable.ErrorMessage), 0),
                editor.Player.GetFrameRate(),
                static () => true);
            HeadlessTestHelpers.Render();

            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(message.Text, Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
            Assert.That(
                AutomationProperties.GetName(message),
                Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));

            // A preview render that succeeds clears the error.
            editor.Player.QueuePreviewRender();
            RenderThreadDrain.Drain(TimeSpan.FromSeconds(10));
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
    public async Task PlaybackRenderFailure_FromAnotherThread_ShowsPreviewError()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditorForNewScene("preview-error-order");
        Rational rate = editor.Player.GetFrameRate();

        bool applied = await Task.Run(() => editor.Player.ApplyPlaybackRenderFailure(
            new BufferedPlayer.RenderFailure(new InvalidOperationException(PreviewFaultDrawable.ErrorMessage), 0),
            rate,
            static () => true));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(
                editor.Player.PreviewRenderError.Value,
                Is.EqualTo(Beutl.Language.MessageStrings.FrameDrawingException));
        });
    }

    // The range PlayerViewModel plays: from the playhead to the end of the scene.
    private static PlaybackRange PlaybackRangeOf(EditViewModel editor)
        => PlaybackRange.From(
            editor.GetRequiredService<IEditorClock>().CurrentTime.Value,
            editor.Scene.Start,
            editor.Scene.Duration,
            editor.Player.GetFrameRate());
}

// Measures 16x16 and draws nothing; each fault drawable overrides Render.
internal abstract partial class PreviewFaultDrawableBase : Drawable
{
    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 16);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class PreviewFaultDrawable : PreviewFaultDrawableBase
{
    public const string ErrorMessage = "preview render failed";

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => throw new InvalidOperationException(ErrorMessage);
}

// The first render after Arm() blocks until ReleaseRender() and then throws. Other renders draw nothing.
internal sealed partial class BlockingPreviewFaultDrawable : PreviewFaultDrawableBase
{
    private readonly TaskCompletionSource<bool> _renderEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseRender =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _armed;
    private int _armedRenderCount;

    public TaskCompletionSource<bool> RenderEntered => _renderEntered;

    public void Arm()
    {
        _armed = true;
        Opacity.Value = 99f;
    }

    public void ReleaseRender() => _releaseRender.TrySetResult(true);

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (!_armed || Interlocked.Increment(ref _armedRenderCount) != 1)
            return;

        _renderEntered.TrySetResult(true);
        _releaseRender.Task.GetAwaiter().GetResult();
        throw new InvalidOperationException(PreviewFaultDrawable.ErrorMessage);
    }
}

internal sealed partial class PreviewObjectDisposedFaultDrawable : PreviewFaultDrawableBase
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => throw new ObjectDisposedException(nameof(PreviewObjectDisposedFaultDrawable));
}
