using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Encoding;
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
            Assert.That(FiguresOf(view).IsEffectivelyVisible, Is.False);
            Assert.That(BarOf(view).IsEffectivelyVisible, Is.False);

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

            Assert.That(FiguresOf(view).IsEffectivelyVisible, Is.True);
            Assert.That(BarOf(view).IsEffectivelyVisible, Is.True);
            Assert.That(ShownFigures(view), Is.SupersetOf(new[] { "00:00:12", "00:00:18", "720 / 1800", "24 MB", "60 MB" }));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_figures_stay_after_a_run_has_finished()
    {
        await TestReset.ResetShellAsync();
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-figures-after");
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.NewWorkspace("output-figures-after"), "out.mp4");
        output.SelectedEncoder.Value = new OneFileEncoder();
        var view = new OutputView(new PendingExecution()) { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            await output.RunAsync(CancellationToken.None);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(output.DestinationFile.Value), Is.True);
                Assert.That(FiguresOf(view).IsEffectivelyVisible, Is.True);
                Assert.That(ShownFigures(view), Does.Contain(output.Elapsed.Value));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    // Cancel has to appear under the pointer that has just pressed Encode, whatever the figures do below it.
    [AvaloniaTest]
    public async Task Starting_an_export_leaves_the_button_under_the_pointer()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-button");
        using var output = new OutputViewModel(editor);
        var view = new OutputView(new PendingExecution()) { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button encode = ButtonWith(view, Strings.Encode);
            Button cancel = ButtonWith(view, Strings.Cancel);
            Point pointer = encode.TranslatePoint(new Point(encode.Bounds.Width / 2, encode.Bounds.Height / 2), window)!.Value;

            encode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            output.HasProgress.Value = true;
            HeadlessTestHelpers.Render();

            Assert.That(new Rect(cancel.Bounds.Size).Contains(window.TranslatePoint(pointer, cancel)!.Value), Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task The_progress_lines_up_with_the_form_below_it()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-alignment");
        using var output = new OutputViewModel(editor);
        var view = new OutputView(new PendingExecution()) { DataContext = output };
        var window = new Window { Content = view, Width = 650, Height = 720 };
        try
        {
            window.Show();
            output.HasProgress.Value = true;
            HeadlessTestHelpers.Render();
            TextBox destination = view.GetVisualDescendants().OfType<TextBox>().First();
            TextBlock formLabel = view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == Strings.DestinationToSaveTo);
            TextBlock figureLabel = FiguresOf(view).Children.OfType<TextBlock>().Single(text => text.Text == Strings.ElapsedTime);
            TextBlock figure = FiguresOf(view).Children.OfType<TextBlock>().Single(text => text.Text == output.Elapsed.Value);

            Assert.Multiple(() =>
            {
                Assert.That(Left(figureLabel), Is.EqualTo(Left(formLabel)).Within(0.5));
                Assert.That(Left(BarOf(view)), Is.EqualTo(Left(formLabel)).Within(0.5));
                Assert.That(Left(figure), Is.EqualTo(Left(destination)).Within(0.5));
                Assert.That(Right(BarOf(view)), Is.EqualTo(Right(destination)).Within(0.5));
                Assert.That(Right(ButtonWith(view, Strings.Encode)), Is.EqualTo(Right(destination)).Within(0.5));
            });

            double Left(Control control) => control.TranslatePoint(default, view)!.Value.X;
            double Right(Control control) => Left(control) + control.Bounds.Width;
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task A_new_run_starts_from_an_empty_bar()
    {
        await TestReset.ResetShellAsync();
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-new-run");
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.NewWorkspace("output-new-run"), "out.mp4");
        double? barAtStart = null;
        output.SelectedEncoder.Value = new OneFileEncoder(() => barAtStart = output.ProgressValue.Value);
        output.ProgressValue.Value = 100;

        await output.RunAsync(CancellationToken.None);

        Assert.That(barAtStart, Is.Zero);
    }

    // The figures stay after a run, so one that stopped must neither read as a button nor promise an end.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task A_run_that_stopped_says_so_and_estimates_nothing(bool cancelled)
    {
        await TestReset.ResetShellAsync();
        TestGpu.RequireAnyDevice();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("output-stopped");
        using var output = new OutputViewModel(editor);
        output.DestinationFile.Value = Path.Combine(TestShell.NewWorkspace("output-stopped"), "out.mp4");
        using var cancellation = new CancellationTokenSource();
        output.SelectedEncoder.Value = new OneFileEncoder(() =>
        {
            output.Eta.Value = "00:00:18";
            output.EstimatedSize.Value = "60 MB";
            if (cancelled) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The encoder failed.");
        });

        Exception? stopped = null;
        try
        {
            await output.RunAsync(cancellation.Token);
        }
        catch (Exception ex)
        {
            stopped = ex;
        }

        Assert.Multiple(() =>
        {
            Assert.That(stopped, cancelled ? Is.InstanceOf<OperationCanceledException>() : Is.InstanceOf<InvalidOperationException>());
            Assert.That(output.ProgressText.Value, Is.EqualTo(cancelled ? MessageStrings.Canceled : "The encoder failed."));
            Assert.That(output.Eta.Value, Is.EqualTo("--:--:--"));
            Assert.That(output.EstimatedSize.Value, Is.EqualTo("-- MB"));
        });
    }

    private static ProgressBar BarOf(OutputView view)
        => view.GetVisualDescendants().OfType<ProgressBar>().Single();

    private static Button ButtonWith(OutputView view, string text)
        => view.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, text));

    private static IEnumerable<string?> ShownFigures(OutputView view)
        => FiguresOf(view).GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text);

    private sealed class OneFileEncoder(Action? encoding = null) : ControllableEncodingExtension
    {
        public override IEnumerable<string> SupportExtensions()
        {
            yield return ".mp4";
        }

        public override EncodingController CreateController(string file) => new OneFileController(file, encoding);
    }

    private sealed class OneFileController(string outputFile, Action? encoding) : EncodingController(outputFile)
    {
        public override VideoEncoderSettings VideoSettings { get; } = new();

        public override AudioEncoderSettings AudioSettings { get; } = new();

        public override async ValueTask Encode(
            IFrameProvider frameProvider, ISampleProvider sampleProvider, CancellationToken cancellationToken)
        {
            encoding?.Invoke();
            await File.WriteAllBytesAsync(OutputFile, [], cancellationToken);
        }
    }

    private static Grid FiguresOf(OutputView view)
        => view.GetVisualDescendants().OfType<Grid>().Single(grid => grid.RowDefinitions.Count == 4 && grid.ColumnDefinitions.Count == 3);

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
