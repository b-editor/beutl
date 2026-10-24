using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class BrushEditorFlyoutLayoutTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Color_inputs_fit_on_open_and_after_changing_tabs(bool overlayPopups)
    {
        var brush = new LinearGradientBrush();
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1));
        var shape = new RectShape { Fill = { Value = brush } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new BrushEditor
        {
            DataContext = viewModel,
            Width = 360,
            Margin = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var window = new Window { Content = editor, Width = 900, Height = 800, RequestedThemeVariant = ThemeVariant.Dark };
        bool previous = TestAppBuilder.HeadlessOptions.OverlayPopups;
        try
        {
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlayPopups;
            window.Show();
            HeadlessTestHelpers.Render();
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            var presenter = (BrushEditorFlyoutPresenter)window.OpenedPopups.Single().Child!;
            Assert.That(TopLevel.GetTopLevel(presenter) is PopupRoot, Is.EqualTo(!overlayPopups),
                "Exercise the native popup layout path as well as the overlay host.");
            VerifyInputs("gradient");

            Click("SolidBrushTabButton");
            VerifyInputs("solid");
            Click("GradientBrushTabButton");
            Click("ToggleDetailsButton");
            VerifyInputs("gradient-details");
            Click("DrawableBrushTabButton");
            Click("SolidBrushTabButton");
            VerifyInputs("drawable-to-solid");
            Click("PaletteTabButton");
            Click("GradientBrushTabButton");
            VerifyInputs("palette-to-gradient");
            Click("CloseButton");
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            VerifyInputs("reopened");

            void Click(string name)
            {
                Button button = presenter.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                HeadlessTestHelpers.Click(button);
            }

            void VerifyInputs(string name)
            {
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(presenter, nameof(BrushEditorFlyoutLayoutTests),
                    $"{(overlayPopups ? "overlay" : "native")}-{name}");
                TextBox opacity = presenter.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "OpacityBox");
                var bottom = new Point(opacity.Bounds.Width, opacity.Bounds.Height);
                Point inPresenter = opacity.TranslatePoint(bottom, presenter)!.Value;
                TopLevel root = TopLevel.GetTopLevel(presenter)!;
                Point inRoot = opacity.TranslatePoint(bottom, root)!.Value;
                Assert.Multiple(() =>
                {
                    Assert.That(opacity.IsEffectivelyVisible, Is.True);
                    Assert.That(opacity.Bounds.Height, Is.GreaterThan(0));
                    Assert.That(inPresenter.Y, Is.LessThanOrEqualTo(presenter.Bounds.Height), name);
                    Assert.That(inRoot.Y, Is.LessThanOrEqualTo(root.ClientSize.Height), name);
                    Assert.That(inRoot.X, Is.LessThanOrEqualTo(root.ClientSize.Width), name);
                });
            }
        }
        finally
        {
            window.Close();
            TestAppBuilder.HeadlessOptions.OverlayPopups = previous;
        }
    }
}
