using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// An export runs from a snapshot of the scene and leaves the window free, so the tab is where it is followed and stopped.
[TestFixture]
[NonParallelizable]
public sealed class OutputViewProgressTests
{
    [AvaloniaTest]
    public async Task The_tab_starts_an_export_and_can_cancel_it()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-progress");
        using var output = new OutputViewModel(editor);
        var execution = new PendingExecution();
        var view = new OutputView(execution) { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button encode = ButtonWith(view, Strings.Encode);
            Button cancel = ButtonWith(view, Strings.Cancel);
            Assert.That(cancel.IsVisible, Is.False);

            encode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(execution.IsRunning.Value, Is.True);
                Assert.That(encode.IsVisible, Is.False);
                Assert.That(cancel.IsVisible, Is.True);
            });

            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();

            Assert.That(execution.IsRunning.Value, Is.False);
            Assert.That(encode.IsVisible, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_figures_of_a_run_appear_when_it_starts_and_stay_after_it()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-figures");
        using var output = new OutputViewModel(editor);
        var view = new OutputView(new PendingExecution()) { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(FiguresOf(view).IsVisible, Is.False);

            output.HasProgress.Value = true;
            output.ProgressText.Value = Strings.Encoding;
            output.ProgressMax.Value = 100;
            output.ProgressValue.Value = 40;
            output.Elapsed.Value = "00:00:12";
            output.Eta.Value = "00:00:18";
            output.FrameProgressText.Value = "720 / 1800";
            output.CurrentSize.Value = "24 MB";
            output.EstimatedSize.Value = "60 MB";
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(OutputViewProgressTests), "running");

            Assert.That(FiguresOf(view).IsVisible, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static Button ButtonWith(OutputView view, string text)
        => view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text));

    private static Grid FiguresOf(OutputView view)
        => view.GetVisualDescendants().OfType<Grid>().Single(grid => grid.RowDefinitions.Count == 4 && grid.ColumnDefinitions.Count == 2);

    private sealed class PendingExecution : IOutputExecutionController
    {
        private readonly ReactivePropertySlim<bool> _isRunning = new();
        private TaskCompletionSource? _completion;

        public IReadOnlyReactiveProperty<bool> IsRunning => _isRunning;

        public bool TryStart([NotNullWhen(true)] out Task? execution)
        {
            _completion = new TaskCompletionSource();
            _isRunning.Value = true;
            execution = _completion.Task;
            return true;
        }

        public void Cancel()
        {
            _isRunning.Value = false;
            _completion?.TrySetCanceled();
        }
    }
}
