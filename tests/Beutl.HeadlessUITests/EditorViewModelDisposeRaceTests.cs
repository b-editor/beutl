using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Reproduces the telemetry crash: a playback-loop timer callback (ThreadPool) reached a disposed
// System.Reactive.Subjects.Subject<T>.OnNext through the editor-clock subscription that
// BaseEditorViewModel bridges onto its private Subject<TimeSpan> (_currentTime). Even with the
// clock subscription disposed first, ReactivePropertySlim keeps iterating its observer list while a
// writer thread is already inside the setter, so the bridge can still land on the subject after
// teardown ran on another thread. BaseEditorViewModel therefore completes (not disposes) the
// subject: post-completion OnNext is swallowed, whereas a disposed one escapes as an unhandled
// ObjectDisposedException on the ThreadPool -> process crash.
[TestFixture]
public class EditorViewModelDisposeRaceTests
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

    // Minimal visitor injecting the editor-session services Accept() resolves.
    private sealed class ServiceVisitor(EditViewModel editViewModel, IEditorClock clock)
        : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(EditViewModel))
                return editViewModel;
            if (serviceType == typeof(IEditorClock))
                return clock;
            return null;
        }
    }

    [AvaloniaTest]
    public async Task Dispose_completes_the_current_time_subject_before_the_clock_bridge_can_write_to_it()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("dispose-race");
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        IReactiveProperty<TimeSpan> clockProperty = clock.CurrentTime;
        var observedStack = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var adapter = new CorePropertyAdapter<TimeSpan>(Scene.DurationProperty, editor.Scene);

        // A background thread hammering the clock stands in for the playback-loop timer callback.
        using var stop = new CancellationTokenSource();
        var observedDisposedWrite = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Task.Run(async () =>
        {
            TimeSpan t = TimeSpan.Zero;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    clockProperty.Value = (t += TimeSpan.FromMilliseconds(1));
                }
                catch (ObjectDisposedException ex)
                {
                    // The window this test covers: the write landed after the subject was disposed
                    // but before the clock subscription was torn down.
                    observedStack.TrySetResult(ex.StackTrace ?? string.Empty);
                    observedDisposedWrite.TrySetResult(true);
                    return;
                }

                await Task.Yield();
            }
        });

        try
        {
            for (int i = 0; i < 2000 && !observedDisposedWrite.Task.IsCompleted; i++)
            {
                var viewModel = new ValueEditorViewModel<TimeSpan>(adapter);
                viewModel.Accept(new ServiceVisitor(editor, clock));
                viewModel.Dispose();
            }
        }
        finally
        {
            await stop.CancelAsync();
            // Await the writer so an unexpected fault fails this test deterministically, and a
            // teardown that hangs fails with a timeout instead of leaking the writer into later
            // tests.
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Assert only after the writer has fully drained: an ObjectDisposedException recorded by
        // the very last Dispose() iteration — or while the writer was being cancelled — must
        // still fail the test.
        if (observedStack.Task.IsCompleted)
        {
            string stack = await observedStack.Task;
            Assert.Fail($"a clock write landed on the disposed subject (dispose-order race):\n{stack}");
        }
    }
}
