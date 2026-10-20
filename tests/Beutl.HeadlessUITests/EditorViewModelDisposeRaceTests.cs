using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Regression coverage for the editor-clock disposal race.
[TestFixture]
public class EditorViewModelDisposeRaceTests
{
    // Supplies the services accepted by the editor property context.
    private sealed class ServiceVisitor(EditViewModel editViewModel, IEditorClock clock)
        : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IEditorClock) ? clock : editViewModel.GetService(serviceType);
    }

    [AvaloniaTest]
    public async Task Dispose_completes_the_current_time_subject_before_the_clock_bridge_can_write_to_it()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("dispose-race");
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        IReactiveProperty<TimeSpan> clockProperty = clock.CurrentTime;
        var observedStack = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var adapter = new CorePropertyAdapter<TimeSpan>(Scene.DurationProperty, editor.Scene);

        // Simulate a playback timer writing from a background thread.
        using var stop = new CancellationTokenSource();
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
                    return;
                }

                await Task.Yield();
            }
        });

        try
        {
            for (int i = 0; i < 2000 && !observedStack.Task.IsCompleted; i++)
            {
                var viewModel = new ValueEditorViewModel<TimeSpan>(adapter);
                viewModel.Accept(new ServiceVisitor(editor, clock));
                viewModel.Dispose();
            }
        }
        finally
        {
            await stop.CancelAsync();
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
        }

        if (observedStack.Task.IsCompleted)
        {
            string stack = await observedStack.Task;
            Assert.Fail($"a clock write landed on the disposed subject (dispose-order race):\n{stack}");
        }
    }
}
