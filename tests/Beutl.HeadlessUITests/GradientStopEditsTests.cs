using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AM = Avalonia.Media;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class GradientStopEditsTests
{
    [AvaloniaTest]
    public void A_dragged_stop_is_undone_as_one_edit_after_release()
    {
        var brush = new LinearGradientBrush();
        var red = new GradientStop(Colors.Red, 0f);
        brush.GradientStops.Add(red);
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 0.5f));
        var shape = new RectShape { Fill = { CurrentValue = brush } };
        using var history = new HistoryScope(shape);
        using var viewModel = new BrushEditorViewModel(
            new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)shape.Fill, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));

        // The slider reports indices it does not hold as well; those are ignored.
        GradientStopEdits.Drag(viewModel, brush.GradientStops, 2, 0, new AM.GradientStop(AM.Colors.Green, 0.1));
        GradientStopEdits.Drag(viewModel, brush.GradientStops, 0, 1, new AM.GradientStop(AM.Colors.Red, 0.8));
        GradientStopEdits.Release(viewModel, brush.GradientStops, 0, 1);

        Assert.Multiple(() =>
        {
            Assert.That(brush.GradientStops[1], Is.SameAs(red));
            Assert.That(red.Offset.CurrentValue, Is.EqualTo(0.8f));
        });

        history.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(brush.GradientStops[0], Is.SameAs(red));
            Assert.That(red.Offset.CurrentValue, Is.EqualTo(0f));
            Assert.That(brush.GradientStops, Has.Count.EqualTo(2));
        });
    }
}
