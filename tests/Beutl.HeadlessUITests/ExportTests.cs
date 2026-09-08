using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Platform.Storage;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media.Encoding;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ExportTests
{
    private sealed class TestCoreObject : CoreObject;

    private sealed class TestOutputContext(string fileName) : IOutputContext
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finish = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _disposeCount;
        private int _runCount;

        public OutputExtension Extension { get; init; } = SceneOutputExtension.Instance;

        public CoreObject Object { get; } = new TestCoreObject
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, fileName))
        };

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>(fileName);

        public IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<double> Progress { get; }
            = new ReactivePropertySlim<double>();

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int RunCount => Volatile.Read(ref _runCount);

        public Task Started => _started.Task;

        public Action? RunEntered { get; init; }

        public bool IgnoreCancellation { get; init; }

        public Exception? SynchronousFailure { get; init; }

        public Exception? AsynchronousFailure { get; init; }

        public Exception? DisposeFailure { get; init; }

        public TaskCompletionSource? DisposeStarted { get; init; }

        public Task? DisposeRelease { get; init; }

        public void Finish()
        {
            _finish.TrySetResult();
        }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runCount);
            RunEntered?.Invoke();
            _started.TrySetResult();
            if (SynchronousFailure is not null)
            {
                throw SynchronousFailure;
            }

            if (AsynchronousFailure is not null)
            {
                return Task.FromException(AsynchronousFailure);
            }

            if (IgnoreCancellation)
            {
                return _finish.Task;
            }

            return _finish.Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            DisposeStarted?.TrySetResult();
            DisposeRelease?.GetAwaiter().GetResult();
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }
        }

        public void WriteToJson(JsonObject json)
        {
        }

        public void ReadFromJson(JsonObject json)
        {
        }
    }

    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorWithRectangle(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            320, 240, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        scene.Duration = TimeSpan.FromMilliseconds(200);

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromMilliseconds(200),
            Layer: 0,
            EngineObjectFactory: () => new RectShape { Width = { CurrentValue = 200 }, Height = { CurrentValue = 150 } }));
        HeadlessTestHelpers.Settle();
        return editor;
    }

    // ---- B2 (a): non-gated — construct/validate the export ViewModel without spawning a worker ----

    [AvaloniaTest]
    public async Task OutputViewModel_constructs_with_sane_defaults()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportvm");

        using var output = new OutputViewModel(editor);
        HeadlessTestHelpers.Settle();

        Assert.That(output.Model, Is.SameAs(editor.Scene));
        Assert.That(output.DestinationFile.Value, Is.Null);
        Assert.That(output.SelectedEncoder.Value, Is.Null);
        Assert.That(output.SupersampleFactor.Value, Is.EqualTo(1));
        Assert.That(output.SupersampleFactors, Is.EqualTo(new[] { 1, 2, 4 }));
        // No destination/encoder yet, so encoding is not permitted.
        Assert.That(output.CanEncode.Value, Is.False);
        Assert.That(output.IsEncoding.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task OutputViewModel_flags_a_supersample_factor_that_exceeds_the_buffer_limit()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportwarn");
        // 5000 * 4 = 20000 > MaxBufferDimension (16384), so a 4x factor overflows the buffer.
        editor.Scene.FrameSize = new Media.PixelSize(5000, 240);

        using var output = new OutputViewModel(editor);
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Null);

        output.SupersampleFactor.Value = 4;
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Not.Null);
        Assert.That(output.CanEncode.Value, Is.False);

        output.SupersampleFactor.Value = 1;
        HeadlessTestHelpers.Settle();
        Assert.That(output.SupersampleWarning.Value, Is.Null);
    }

    [AvaloniaTest]
    public async Task OutputViewModel_without_a_registered_encoder_cannot_encode()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("exportnoenc");

        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(NewWorkspace("exportnoenc"), "out.mp4");
        HeadlessTestHelpers.Settle();

        // A destination alone is not enough; with no encoder extension loaded the list is empty
        // and CanEncode stays false (SelectedEncoder is still null).
        Assert.That(output.Encoders, Is.Empty);
        Assert.That(output.CanEncode.Value, Is.False);
    }

    // Exercise the export failure path with a fake FFmpeg error.
    [AvaloniaTest]
    public async Task OutputViewModel_translates_a_known_ffmpeg_error_when_encoding_fails()
    {
        await ResetProjectAsync();
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditorWithRectangle("exportfailure");

        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(NewWorkspace("exportfailure"), "out.mp4");
        output.SelectedEncoder.Value = ThrowingMp4EncoderExtension.Instance;
        HeadlessTestHelpers.Settle();

        var recorder = new RecordingNotificationHandler();
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        NotificationService.Handler = recorder;
        try
        {
            Assert.ThrowsAsync<FFmpegWorkerException>(async () =>
                await output.RunAsync(CancellationToken.None));
        }
        finally
        {
            NotificationService.Handler = previousHandler;
        }

        // Progress and notifications must use the translated message.
        string expected = MessageStrings.FFmpegErrorInvalidData;
        Assert.That(output.ProgressText.Value, Is.EqualTo(expected));
        Assert.That(
            recorder.ErrorMessages,
            Does.Contain(expected),
            "The error notification must carry the translated message.");
    }

    private sealed class ThrowingMp4EncoderExtension : ControllableEncodingExtension
    {
        public static ThrowingMp4EncoderExtension Instance { get; } = new();

        public override IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
        }

        public override EncodingController CreateController(string file) => new ThrowingController(file);
    }

    private sealed class ThrowingController(string outputFile) : EncodingController(outputFile)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();

        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override ValueTask Encode(
            IFrameProvider frameProvider,
            ISampleProvider sampleProvider,
            CancellationToken cancellationToken)
        {
            // Simulate AVERROR_INVALIDDATA from a corrupt input.
            return ValueTask.FromException(new FFmpegWorkerException(
                "FFmpeg error [-1094995529] Invalid data found when processing input",
                ffmpegErrorCode: FFmpegErrorMessageMapper.InvalidDataCode));
        }
    }

    private sealed class CancellableMp4EncoderExtension : ControllableEncodingExtension
    {
        public CancellableController? Controller { get; private set; }

        public override IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
        }

        public override EncodingController CreateController(string file)
        {
            return Controller = new CancellableController(file);
        }
    }

    private sealed class CancellableController(string outputFile) : EncodingController(outputFile)
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override VideoEncoderSettings VideoSettings { get; } = new();

        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override async ValueTask Encode(
            IFrameProvider frameProvider,
            ISampleProvider sampleProvider,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RecordingNotificationHandler : INotificationServiceHandler
    {
        private readonly List<string> _errorMessages = [];
        private readonly List<string> _warningMessages = [];

        public IReadOnlyList<string> ErrorMessages => _errorMessages;

        public IReadOnlyList<string> WarningMessages => _warningMessages;

        public void Show(Notification notification)
        {
            if (notification.Type == NotificationType.Error)
            {
                _errorMessages.Add(notification.Message);
            }
            else if (notification.Type == NotificationType.Warning)
            {
                _warningMessages.Add(notification.Message);
            }
        }
    }

    [AvaloniaTest]
    public async Task Built_in_output_cancellation_cancels_the_shared_execution_task()
    {
        await ResetProjectAsync();
        GpuTestGate.EnsureAvailable();
        EditViewModel editor = await OpenEditorWithRectangle("export-built-in-cancel");
        var extension = new CancellableMp4EncoderExtension();
        var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(NewWorkspace("export-built-in-cancel"), "out.mp4");
        output.SelectedEncoder.Value = extension;
        HeadlessTestHelpers.Settle();
        var item = new OutputProfileItem(output, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await extension.Controller!.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            item.Cancel();

            try
            {
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            Assert.Multiple(() =>
            {
                Assert.That(execution.IsCanceled, Is.True);
                Assert.That(output.WasCancelled.Value, Is.True);
                Assert.That(output.IsEncoding.Value, Is.False);
                Assert.That(output.IsCompleted.Value, Is.False);
            });
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            item.Cancel();
            try
            {
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputViewModel_preflight_cancellation_resets_completion_state()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-preflight-cancel");
        using var output = new OutputViewModel(editor);
        using var cancellation = new CancellationTokenSource();
        output.IsCompleted.Value = true;
        cancellation.Cancel();

        try
        {
            await output.RunAsync(cancellation.Token);
            Assert.Fail("The cancelled output preflight unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(output.WasCancelled.Value, Is.True);
            Assert.That(output.IsCompleted.Value, Is.False);
            Assert.That(output.IsEncoding.Value, Is.False);
            Assert.That(output.ProgressText.Value, Is.EqualTo(Strings.Cancel));
        });
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_acquires_the_lease_before_context_work_starts()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-lease-before-run");
        bool leaseHeldAtEntry = false;
        var context = new TestOutputContext("export-lease-before-run.scene")
        {
            RunEntered = () => leaseHeldAtEntry = !CanBeginWorkspaceMutation()
        };
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(leaseHeldAtEntry, Is.True);
                Assert.That(context.RunCount, Is.EqualTo(1));
                Assert.That(item.IsRunning.Value, Is.True);
                Assert.That(editor.IsEnabled.Value, Is.False);
            });
            AssertWorkspaceMutationBlocked();

            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(item.IsRunning.Value, Is.False);
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
            AssertWorkspaceMutationAvailable();

            await StartOutput(item).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(context.RunCount, Is.EqualTo(2));
        }
        finally
        {
            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_does_not_enter_context_when_a_mutation_is_active()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-refused");
        var context = new TestOutputContext("export-refused.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        using IDisposable mutation = TestShell.Editor.TryBeginWorktreeMutation()!;

        try
        {
            bool started = item.TryStart(out Task? execution);

            Assert.Multiple(() =>
            {
                Assert.That(started, Is.False);
                Assert.That(execution, Is.Null);
                Assert.That(context.RunCount, Is.Zero);
                Assert.That(item.IsRunning.Value, Is.False);
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
        }
        finally
        {
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_releases_admission_when_editor_suspension_fails()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-suspension-failure");
        var context = new TestOutputContext("export-suspension-failure.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        var expected = new InvalidOperationException("suspension observer failed");
        using IDisposable subscription = editor.IsEnabled.Subscribe(value =>
        {
            if (!value)
            {
                throw expected;
            }
        });

        Exception? actual = null;
        try
        {
            Assert.That(item.TryStart(out Task? execution), Is.True);
            await execution!;
        }
        catch (Exception ex)
        {
            actual = ex;
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(context.RunCount, Is.Zero);
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_releases_the_lease_after_a_synchronous_context_failure()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-sync-failure");
        var expected = new InvalidOperationException("synchronous failure");
        var context = new TestOutputContext("export-sync-failure.scene")
        {
            SynchronousFailure = expected
        };

        await AssertContextFailureReleasesLease(editor, context, expected);
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_releases_the_lease_after_a_faulted_context_task()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-async-failure");
        var expected = new InvalidOperationException("asynchronous failure");
        var context = new TestOutputContext("export-async-failure.scene")
        {
            AsynchronousFailure = expected
        };

        await AssertContextFailureReleasesLease(editor, context, expected);
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_cancel_keeps_the_lease_until_context_returns()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-cancel-active");
        var context = new TestOutputContext("export-cancel-active.scene")
        {
            IgnoreCancellation = true
        };
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            item.Cancel();

            Assert.That(execution.IsCompleted, Is.False);
            AssertWorkspaceMutationBlocked();

            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_cooperative_cancellation_cancels_the_execution_task()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-cooperative-cancel");
        var context = new TestOutputContext("export-cooperative-cancel.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            item.Cancel();

            try
            {
                await execution.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            Assert.That(execution.IsCanceled, Is.True);
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            context.Finish();
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_concurrent_starts_share_one_execution()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-double-start");
        var context = new TestOutputContext("export-double-start.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);

        bool firstStarted = item.TryStart(out Task? first);
        bool secondStarted = item.TryStart(out Task? second);
        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(firstStarted, Is.True);
                Assert.That(secondStarted, Is.True);
                Assert.That(second, Is.SameAs(first));
                Assert.That(context.RunCount, Is.EqualTo(1));
            });

            context.Finish();
            await Task.WhenAll(first!, second!).WaitAsync(TimeSpan.FromSeconds(5));
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            context.Finish();
            await first!.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItems_keep_the_editor_suspended_until_all_executions_finish()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-overlapping-profiles");
        var firstContext = new TestOutputContext("export-overlapping-profiles-first.scene");
        var secondContext = new TestOutputContext("export-overlapping-profiles-second.scene");
        var firstItem = new OutputProfileItem(firstContext, editor, TestShell.Editor);
        var secondItem = new OutputProfileItem(secondContext, editor, TestShell.Editor);
        Task firstExecution = StartOutput(firstItem);
        Task secondExecution = StartOutput(secondItem);

        try
        {
            await Task.WhenAll(firstContext.Started, secondContext.Started)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(editor.IsEnabled.Value, Is.False);
            AssertWorkspaceMutationBlocked();

            firstContext.Finish();
            await firstExecution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(editor.IsEnabled.Value, Is.False);
            AssertWorkspaceMutationBlocked();

            secondContext.Finish();
            await secondExecution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(editor.IsEnabled.Value, Is.True);
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            firstContext.Finish();
            secondContext.Finish();
            await Task.WhenAll(firstExecution, secondExecution).WaitAsync(TimeSpan.FromSeconds(5));
            firstItem.Dispose();
            secondItem.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_during_execution_waits_for_context_completion()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-active");
        var context = new TestOutputContext("export-dispose-active.scene")
        {
            IgnoreCancellation = true
        };
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(context.DisposeCount, Is.Zero);
                Assert.That(execution.IsCompleted, Is.False);
            });
            AssertWorkspaceMutationBlocked();

            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(context.DisposeCount, Is.EqualTo(1));
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_holds_admission_through_deferred_context_disposal()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-deferred-dispose-lease");
        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new TestOutputContext("export-deferred-dispose-lease.scene")
        {
            DisposeStarted = disposeStarted,
            DisposeRelease = releaseDispose.Task
        };
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
            Task observeBlockedDisposal = Task.Run(async () =>
            {
                await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(execution.IsCompleted, Is.False);
                        Assert.That(CanBeginWorkspaceMutation(), Is.False);
                        Assert.That(context.DisposeCount, Is.EqualTo(1));
                    });
                }
                finally
                {
                    releaseDispose.TrySetResult();
                }
            });
            context.Finish();
            await observeBlockedDisposal.WaitAsync(TimeSpan.FromSeconds(5));
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            releaseDispose.TrySetResult();
            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            item.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_completes_and_releases_the_lease_when_context_disposal_fails()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-failure");
        var expected = new InvalidOperationException("dispose failure");
        var context = new TestOutputContext("export-dispose-failure.scene")
        {
            DisposeFailure = expected
        };
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Task execution = StartOutput(item);
        await context.Started.WaitAsync(TimeSpan.FromSeconds(5));

        item.Dispose();
        context.Finish();
        Exception? actual = null;
        try
        {
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            actual = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(context.DisposeCount, Is.EqualTo(1));
            Assert.That(editor.IsEnabled.Value, Is.True);
        });
        AssertWorkspaceMutationAvailable();
    }

    [AvaloniaTest]
    public async Task OutputTabViewModel_remove_item_refuses_active_execution()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-remove-active");
        var viewModel = new OutputTabViewModel(editor);
        var context = new TestOutputContext("export-remove-active.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        viewModel.Items.Add(item);
        viewModel.SelectedItem.Value = item;
        Task execution = StartOutput(item);
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new RecordingNotificationHandler();
        NotificationService.Handler = notifications;

        try
        {
            await context.Started.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.RemoveItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Contain(item));
                Assert.That(viewModel.SelectedItem.Value, Is.SameAs(item));
                Assert.That(context.DisposeCount, Is.Zero);
                Assert.That(notifications.WarningMessages, Does.Contain(Strings.Output_ProfileRunning));
            });
            AssertWorkspaceMutationBlocked();

            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.RemoveItem(item);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Not.Contain(item));
                Assert.That(context.DisposeCount, Is.EqualTo(1));
            });
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            context.Finish();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputTabViewModel_removes_and_reselects_before_plugin_disposal_failure()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-remove-dispose-failure");
        var viewModel = new OutputTabViewModel(editor);
        var expected = new InvalidOperationException("profile dispose failed");
        var failingContext = new TestOutputContext("export-remove-dispose-failure.scene")
        {
            DisposeFailure = expected
        };
        var nextContext = new TestOutputContext("export-remove-dispose-next.scene");
        var failingItem = new OutputProfileItem(failingContext, editor, TestShell.Editor);
        var nextItem = new OutputProfileItem(nextContext, editor, TestShell.Editor);
        viewModel.Items.Add(failingItem);
        viewModel.Items.Add(nextItem);
        viewModel.SelectedItem.Value = failingItem;
        INotificationServiceHandler previousHandler = NotificationService.Handler;
        var notifications = new RecordingNotificationHandler();
        NotificationService.Handler = notifications;

        try
        {
            Assert.DoesNotThrow(() => viewModel.RemoveItem(failingItem));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Not.Contain(failingItem));
                Assert.That(viewModel.SelectedItem.Value, Is.SameAs(nextItem));
                Assert.That(failingContext.DisposeCount, Is.EqualTo(1));
                Assert.That(notifications.ErrorMessages, Does.Contain(expected.Message));
                Assert.Throws<ObjectDisposedException>(() => failingItem.TryStart(out _));
            });
        }
        finally
        {
            NotificationService.Handler = previousHandler;
            nextItem.Dispose();
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputTabViewModel_finishes_removal_when_a_collection_observer_throws()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-remove-observer-failure");
        var viewModel = new OutputTabViewModel(editor);
        var removedContext = new TestOutputContext("export-remove-observer-failure.scene");
        var nextContext = new TestOutputContext("export-remove-observer-next.scene");
        var removedItem = new OutputProfileItem(removedContext, editor, TestShell.Editor);
        var nextItem = new OutputProfileItem(nextContext, editor, TestShell.Editor);
        viewModel.Items.Add(removedItem);
        viewModel.Items.Add(nextItem);
        viewModel.SelectedItem.Value = removedItem;
        var expected = new InvalidOperationException("collection observer failed");
        NotifyCollectionChangedEventHandler observer = (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Remove)
            {
                throw expected;
            }
        };
        viewModel.Items.CollectionChanged += observer;

        try
        {
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => viewModel.RemoveItem(removedItem)),
                Is.SameAs(expected));

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Items, Does.Not.Contain(removedItem));
                Assert.That(viewModel.SelectedItem.Value, Is.SameAs(nextItem));
                Assert.That(removedContext.DisposeCount, Is.EqualTo(1));
                Assert.Throws<ObjectDisposedException>(() => removedItem.TryStart(out _));
            });
        }
        finally
        {
            viewModel.Items.CollectionChanged -= observer;
            nextItem.Dispose();
            viewModel.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_and_completion_race_disposes_once()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-race");
        var context = new TestOutputContext("export-dispose-race.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        using var gate = new ManualResetEventSlim();
        Task execution = StartOutput(item);
        await context.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Task disposeTask = Task.Run(() =>
        {
            gate.Wait();
            item.Dispose();
        });
        Task finishTask = Task.Run(() =>
        {
            gate.Wait();
            context.Finish();
        });

        gate.Set();
        await Task.WhenAll(disposeTask, finishTask, execution).WaitAsync(TimeSpan.FromSeconds(5));
        item.Dispose();
        context.Finish();

        Assert.That(context.DisposeCount, Is.EqualTo(1));
        AssertWorkspaceMutationAvailable();
    }

    [AvaloniaTest]
    public async Task OutputProfileItem_dispose_when_idle_is_immediate_and_idempotent()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-dispose-idle");
        var context = new TestOutputContext("export-dispose-idle.scene");
        var item = new OutputProfileItem(context, editor, TestShell.Editor);

        item.Dispose();
        item.Dispose();

        Assert.That(context.DisposeCount, Is.EqualTo(1));
        AssertWorkspaceMutationAvailable();
    }

    [AvaloniaTest]
    public async Task OutputTab_creates_a_profile_scoped_control_and_controller()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorWithRectangle("export-profile-controls");
        var extension = new RecordingOutputExtension();
        var firstContext = new TestOutputContext("export-profile-controls-first.scene")
        {
            Extension = extension
        };
        var secondContext = new TestOutputContext("export-profile-controls-second.scene")
        {
            Extension = extension
        };
        var firstItem = new OutputProfileItem(firstContext, editor, TestShell.Editor);
        var secondItem = new OutputProfileItem(secondContext, editor, TestShell.Editor);
        var outputTab = new OutputTab();
        ContentControl contentControl = outputTab.FindControl<ContentControl>("contentControl")!;

        try
        {
            Control firstControl = contentControl.ContentTemplate!.Build(firstItem)!;
            Control secondControl = contentControl.ContentTemplate.Build(secondItem)!;

            Assert.Multiple(() =>
            {
                Assert.That(firstControl, Is.Not.SameAs(secondControl));
                Assert.That(firstControl.DataContext, Is.SameAs(firstContext));
                Assert.That(secondControl.DataContext, Is.SameAs(secondContext));
                Assert.That(extension.Contexts, Is.EqualTo(new[] { firstContext, secondContext }));
                Assert.That(extension.Controllers, Is.EqualTo(new[] { firstItem, secondItem }));
            });
        }
        finally
        {
            firstItem.Dispose();
            secondItem.Dispose();
        }
    }

    private static async Task AssertContextFailureReleasesLease(
        EditViewModel editor,
        TestOutputContext context,
        Exception expected)
    {
        var item = new OutputProfileItem(context, editor, TestShell.Editor);
        Exception? actual = null;
        try
        {
            await StartOutput(item);
        }
        catch (Exception ex)
        {
            actual = ex;
        }

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(context.RunCount, Is.EqualTo(1));
                Assert.That(item.IsRunning.Value, Is.False);
                Assert.That(editor.IsEnabled.Value, Is.True);
            });
            AssertWorkspaceMutationAvailable();
        }
        finally
        {
            item.Dispose();
        }
    }

    private static Task StartOutput(OutputProfileItem item)
    {
        Assert.That(item.TryStart(out Task? execution), Is.True);
        Assert.That(execution, Is.Not.Null);
        return execution!;
    }

    private static bool CanBeginWorkspaceMutation()
    {
        using IDisposable? operation = TestShell.Editor.TryBeginWorktreeMutation();
        return operation is not null;
    }

    private static void AssertWorkspaceMutationBlocked()
    {
        Assert.That(CanBeginWorkspaceMutation(), Is.False);
    }

    private static void AssertWorkspaceMutationAvailable()
    {
        Assert.That(CanBeginWorkspaceMutation(), Is.True);
    }

    private sealed class RecordingOutputExtension : OutputExtension
    {
        public List<IOutputContext> Contexts { get; } = [];

        public List<IOutputExecutionController> Controllers { get; } = [];

        public override FilePickerFileType GetFilePickerFileType() => new("Test output");

        public override bool TryCreateControl(
            IEditorContext editorContext,
            IOutputContext context,
            IOutputExecutionController execution,
            [NotNullWhen(true)] out Control? control)
        {
            Contexts.Add(context);
            Controllers.Add(execution);
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IOutputContext? context)
        {
            context = null;
            return false;
        }

        public override bool IsSupported(Type type) => true;
    }

    // B2 (b) — selecting a real FFmpeg encoder and running the full export is BLOCKED headless, so
    // no such test ships. Three independent blockers:
    //   1. Selecting the encoder builds its settings editor, whose Codec ChoicesProvider enumerates
    //      native FFmpeg codecs; that native load hangs under the headless host.
    //   2. The worker's managed assembly (Beutl.FFmpegWorker.dll) is not in this test's output — only
    //      the apphost is — so the process aborts at launch ("application to execute does not exist").
    //      Deploying it would need a worker ProjectReference, which the GPL/MIT boundary forbids here.
    //   3. The worker IPC drives async NamedPipe/shared-memory I/O that deadlocks against the
    //      single-threaded Avalonia headless dispatcher (the encode hangs indefinitely).
    // The (a) tests above cover the export ViewModel surface reachable without touching native FFmpeg.
}
