using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class HistoryToolBarTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task History_actions_keep_their_bindings_after_bar_migration(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"history-bar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "history-bar", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var shape = new RectShape();
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => shape))], CancellationToken.None);
        using var history = new HistoryViewModel(editor);
        var historyView = new HistoryView { DataContext = history };
        var window = new Window
        {
            Content = historyView,
            Width = width,
            Height = 360,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            int beforeEdit = history.CurrentIndex.Value;
            ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).SetEnabled(shape, false);
            HeadlessTestHelpers.Settle();
            int afterEdit = history.CurrentIndex.Value;
            Assert.That(afterEdit, Is.GreaterThan(beforeEdit));
            CheckBar(historyView, "history");
            Click(historyView.FindControl<Button>("UndoButton")!);
            await WaitUntil(() => shape.IsEnabled && history.CurrentIndex.Value == beforeEdit);
            Assert.That(historyView.FindControl<TextBlock>("CurrentIndexText")!.Text, Is.EqualTo($"#{beforeEdit}"));
            Click(historyView.FindControl<Button>("RedoButton")!);
            await WaitUntil(() => !shape.IsEnabled && history.CurrentIndex.Value == afterEdit);
            CheckBar(historyView, "history-redo");

        }
        finally { window.Close(); }

        async Task WaitUntil(Func<bool> condition)
        {
            for (int i = 0; i < 500 && !condition(); i++)
            {
                await Task.Delay(10);
                HeadlessTestHelpers.Settle();
            }
            Assert.That(condition(), Is.True);
        }

        void Click(Control control)
        {
            Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
        }

        void CheckBar(Control view, string name)
        {
            window.UpdateLayout();
            HeadlessTestHelpers.Render();
            ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
            Assert.That(bar.Bounds.Height, Is.GreaterThanOrEqualTo(bar.MinHeight));
            foreach (Button button in bar.GetLogicalDescendants().OfType<Button>())
            {
                Point position = button.TranslatePoint(default, bar)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(position.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(position.Y, Is.GreaterThanOrEqualTo(0));
                    Assert.That(position.X + button.Bounds.Width, Is.LessThanOrEqualTo(bar.Bounds.Width));
                    Assert.That(position.Y + button.Bounds.Height, Is.LessThanOrEqualTo(bar.Bounds.Height));
                });
            }
            if (Environment.GetEnvironmentVariable("BEUTL_HISTORY_BAR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
