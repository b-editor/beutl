using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D.Primitives;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using RectShape = Beutl.Graphics.Shapes.RectShape;
using Vector = Avalonia.Vector;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class NumericHapticFeedbackTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Scalar_scrub_pulses_on_arrival_and_crossing_the_property_default(bool scale)
    {
        await TestReset.ResetShellAsync();
        EngineObject owner = scale ? new ScaleTransform() : new TranslateTransform();
        IExpressionProperty<float> property = scale ? ((ScaleTransform)owner).Scale : ((TranslateTransform)owner).X;
        float reference = scale ? 100 : 0;
        property.Value = reference - 4;
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property, owner);
        using var history = new PropertyEditorTestHost.HistoryScope(owner);
        var (model, control) = PropertyEditorTestHost.CreateControl(adapter);
        using var disposable = model;
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var number = (NumberEditor<float>)control;
        number.SmallChange = 1;
        using var host = new ScrubHost(number);
        using var feedback = new HapticFeedbackRecorder();
        Point start = host.Header(number);
        host.Window.MouseDown(start, MouseButton.Left);
        Move(2, reference - 2, 0);
        Move(4, reference, 1);
        Move(4, reference, 1);
        Move(6, reference + 2, 1);
        Move(2, reference - 2, 2);
        Move(8, reference + 4, 3);
        host.Window.MouseUp(start + new Vector(8, 0), MouseButton.Left);
        HeadlessCapture.SaveIfRequested(host.Window, nameof(NumericHapticFeedbackTests), scale ? "scale" : "position");
        history.History.Undo();
        Assert.That(property.Value, Is.EqualTo(reference - 4));
        history.History.Redo();
        Assert.That(property.Value, Is.EqualTo(reference + 4));
        Assert.That(feedback.Pulses, Is.EqualTo(Enumerable.Repeat(HapticFeedbackKind.Alignment, 3)));

        void Move(double x, float expected, int count)
        {
            host.Window.MouseMove(start + new Vector(x, 0), RawInputModifiers.LeftMouseButton);
            Assert.That(property.Value, Is.EqualTo(expected));
            Assert.That(feedback.Pulses, Has.Count.EqualTo(count));
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Width_and_opacity_pulse_at_zero_even_though_their_default_is_nonzero(bool opacity)
    {
        await TestReset.ResetShellAsync();
        var rectangle = new RectShape();
        IExpressionProperty<float> property = opacity ? rectangle.Opacity : rectangle.Width;
        property.Value = 4;
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property, rectangle);
        Assert.That(adapter.GetDefaultValue(), Is.Not.EqualTo(0f));
        using var history = new PropertyEditorTestHost.HistoryScope(rectangle);
        var (model, control) = PropertyEditorTestHost.CreateControl(adapter);
        using var disposable = model;
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var number = (NumberEditor<float>)control;
        number.SmallChange = 1;
        using var host = new ScrubHost(number);
        using var feedback = new HapticFeedbackRecorder();
        Point start = host.Header(number);
        host.Window.MouseDown(start, MouseButton.Left);
        Move(-2, 2, 0);
        Move(-4, 0, 1);
        Move(-6, 0, 1); // Further movement against the lower bound stays silent.
        Move(-3, 3, 1);
        Move(-7, 0, 2); // A fast scrub is coerced to zero and should still pulse.
        host.Window.MouseUp(start - new Vector(7, 0), MouseButton.Left);
        HeadlessCapture.SaveIfRequested(host.Window, nameof(NumericHapticFeedbackTests), opacity ? "opacity-zero" : "width-zero");
        history.History.Undo();
        Assert.That(property.Value, Is.EqualTo(4));
        history.History.Redo();
        Assert.That(property.Value, Is.EqualTo(0));
        Assert.That(feedback.Pulses, Has.Count.EqualTo(2));

        void Move(double x, float expected, int count)
        {
            host.Window.MouseMove(start + new Vector(x, 0), RawInputModifiers.LeftMouseButton);
            Assert.That(property.Value, Is.EqualTo(expected));
            Assert.That(feedback.Pulses, Has.Count.EqualTo(count));
        }
    }

    [AvaloniaTest]
    public async Task Vector_scrub_uses_the_metadata_default_and_combines_simultaneous_axes()
    {
        await TestReset.ResetShellAsync();
        var cube = new Cube3D();
        cube.Scale.Value = new Vector3(-1);
        var adapter = new AnimatablePropertyAdapter<Vector3>((AnimatableProperty<Vector3>)cube.Scale, cube);
        using var history = new PropertyEditorTestHost.HistoryScope(cube);
        var (model, control) = PropertyEditorTestHost.CreateControl(adapter);
        using var disposable = model;
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        using var host = new ScrubHost(control);
        var vector = host.Window.GetVisualDescendants().OfType<Vector3Editor<float>>().Single();
        vector.SmallChange = 1;
        using var feedback = new HapticFeedbackRecorder();
        Point start = host.Header(vector);
        host.Window.MouseDown(start, MouseButton.Left);
        Move(1, Vector3.Zero, 1);
        Move(2, Vector3.One, 2);
        Move(4, new Vector3(3), 2);
        Move(1, Vector3.Zero, 3); // Crossing both references in one move produces one pulse.
        host.Window.MouseUp(start + new Vector(1, 0), MouseButton.Left);
        HeadlessCapture.SaveIfRequested(host.Window, nameof(NumericHapticFeedbackTests), "vector");
        history.History.Undo();
        Assert.That(cube.Scale.Value, Is.EqualTo(new Vector3(-1)));
        Assert.That(feedback.Pulses, Has.Count.EqualTo(3));

        void Move(double x, Vector3 expected, int count)
        {
            host.Window.MouseMove(start + new Vector(x, 0), RawInputModifiers.LeftMouseButton);
            Assert.That(cube.Scale.Value, Is.EqualTo(expected));
            Assert.That(feedback.Pulses, Has.Count.EqualTo(count));
        }
    }

    [AvaloniaTest]
    public void A_coerced_scrub_that_never_reaches_the_reference_and_text_edits_are_silent()
    {
        var number = new NumberEditor<float> { Header = "Constrained value", Value = 2, SmallChange = 1 };
        number.AddHandler(PropertyEditor.ValueChangedEvent, (_, _) => number.Value = Math.Max(1, number.Value));
        using var host = new ScrubHost(number);
        using var feedback = new HapticFeedbackRecorder();
        Point start = host.Header(number);
        host.Window.MouseDown(start, MouseButton.Left);
        host.Window.MouseMove(start - new Vector(4, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(start - new Vector(4, 0), MouseButton.Left);
        Assert.That(number.Value, Is.EqualTo(1));
        Assert.That(feedback.Pulses, Is.Empty);
        number.Value = -2; // Binding updates do not emit feedback either.
        TextBox box = number.GetVisualDescendants().OfType<TextBox>().Single();
        box.Focus();
        box.SelectAll();
        host.Window.KeyTextInput("2");
        Assert.That(number.Value, Is.EqualTo(2));
        Assert.That(feedback.Pulses, Is.Empty);
    }

    private sealed class ScrubHost : IDisposable
    {
        private readonly bool _pointerLock = GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value;

        public ScrubHost(Control control)
        {
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = false;
            Window = new Window { Content = new StackPanel { Margin = new Thickness(24), Children = { control } }, Width = 420, Height = 180 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
        }

        public Window Window { get; }

        public Point Header(Control control)
        {
            TextBlock header = control.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Name == "PART_HeaderTextBlock");
            return header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), Window)!.Value;
        }

        public void Dispose()
        {
            Window.Close();
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = _pointerLock;
        }
    }
}
