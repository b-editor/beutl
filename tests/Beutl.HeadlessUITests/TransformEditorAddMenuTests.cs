using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TransformEditorAddMenuTests
{
    [AvaloniaTest]
    public void The_add_menu_of_a_group_adds_the_clicked_transform()
    {
        var group = new TransformGroup();
        var shape = new RectShape { Transform = { CurrentValue = group } };
        using var history = new HistoryScope(shape);
        using var viewModel = new TransformEditorViewModel(
            new SimplePropertyAdapter<Transform?>((SimpleProperty<Transform?>)shape.Transform, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new TransformEditor { DataContext = viewModel };
        var window = new Window { Content = editor, Width = 480, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().Single()
                .RaiseEvent(new RoutedEventArgs(EffectEditorHeaderButtons.ClickEvent));
            HeadlessTestHelpers.Render();
            window.GetVisualDescendants().OfType<FAMenuFlyoutItem>()
                .Single(item => item.Tag is KnownTransformType.Rotation)
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));

            Assert.That(group.Children.Single(), Is.TypeOf<RotationTransform>());
        }
        finally
        {
            window.Close();
        }
    }
}
