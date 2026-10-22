using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BrushEditorDrawableNameTests
{
    [AvaloniaTest]
    public void Clearing_the_drawable_offers_to_create_a_new_one()
    {
        var brush = new DrawableBrush { Drawable = { Value = new EllipseShape() } };
        var shape = new RectShape { Fill = { Value = brush } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new BrushEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 480, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            editor.BrushPreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();
            BrushEditorFlyoutPresenter presenter = window.GetVisualDescendants().OfType<BrushEditorFlyoutPresenter>().Single();
            Assert.That(presenter.DrawableName, Is.Not.EqualTo(Strings.CreateNew));

            brush.Drawable.Value = null;

            Assert.Multiple(() =>
            {
                Assert.That(presenter.DrawableName, Is.EqualTo(Strings.CreateNew));
                Assert.That(presenter.CanEditDrawable, Is.False);
            });

            // A brush set while the flyout is open is followed as well.
            var next = new DrawableBrush { Drawable = { Value = new EllipseShape() } };
            shape.Fill.Value = next;
            HeadlessTestHelpers.Render();
            Assert.That(presenter.DrawableName, Is.Not.EqualTo(Strings.CreateNew));
            next.Drawable.Value = null;
            Assert.That(presenter.DrawableName, Is.EqualTo(Strings.CreateNew));
        }
        finally
        {
            window.Close();
        }
    }
}
