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

// Regression coverage for the editor-clock disposal race.
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
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    // Supplies the services accepted by the editor property context.
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

        // Simulate a playback timer writing from a background thread.
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
                    // This is the race that caused the production crash.
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
            // Drain the writer before the test ends.
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Check after the writer has stopped.
        if (observedStack.Task.IsCompleted)
        {
            string stack = await observedStack.Task;
            Assert.Fail($"a clock write landed on the disposed subject (dispose-order race):\n{stack}");
        }
    }
}
