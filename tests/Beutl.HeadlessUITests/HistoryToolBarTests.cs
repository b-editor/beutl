using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Components.HistoryTab.ViewModels;
using Beutl.Editor.Components.HistoryTab.Views;
using Beutl.Editor.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
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
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("history-bar");
        var shape = new RectShape();
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
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
            CheckBar(historyView);
            Click(historyView.FindControl<Button>("UndoButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => shape.IsEnabled.Value && history.CurrentIndex.Value == beforeEdit);
            Assert.That(historyView.FindControl<TextBlock>("CurrentIndexText")!.Text, Is.EqualTo($"#{beforeEdit}"));
            Click(historyView.FindControl<Button>("RedoButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => !shape.IsEnabled.Value && history.CurrentIndex.Value == afterEdit);
            CheckBar(historyView);

        }
        finally { window.Close(); }

        void Click(Control control)
        {
            Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Settle();
        }

        void CheckBar(Control view)
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
        }
    }
}
