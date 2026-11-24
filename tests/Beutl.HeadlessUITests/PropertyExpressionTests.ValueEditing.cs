using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Moq;
using Reactive.Bindings;
using AvaPoint = Avalonia.Point;
using Point = Beutl.Graphics.Point;
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeRect = Beutl.Graphics.RelativeRect;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

public partial class PropertyExpressionTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_number_edit_replaces_the_value_under_the_expression(bool animated)
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50", animated);
        var editor = (NumberSliderEditor<float>)scope.Control;
        Assert.Multiple(() =>
        {
            Assert.That(scope.Model.CanEdit.Value, Is.True);
            Assert.That(editor.IsReadOnly, Is.False);
            Assert.That(editor.Value, Is.EqualTo(150));
        });

        scope.Model.SetValue(editor.Value, 120);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(animated ? 100 : 120));
            Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(120));
            Assert.That(editor.Value, Is.EqualTo(170));
            Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        scope.History.Undo();
        Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(100));
    }

    [AvaloniaTest]
    public void A_float_drag_offsets_the_underlying_value_and_undoes_as_one_edit()
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50");
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(150), Is.EqualTo(150));
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(155), Is.EqualTo(155));
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(160), Is.EqualTo(160));
        Assert.That(scope.History.UndoCount, Is.Zero);
        scope.Model.SetValue(150, 160);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(110));
            Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(100));
        scope.History.Redo();
        Assert.That(scope.Property.Value, Is.EqualTo(110));

        scope.Model.SetValue(160, 125);
        Assert.That(scope.Property.Value, Is.EqualTo(125), "The next direct edit is absolute.");
    }

    // A typed component comes with the other fields' shown results, which must not replace their values below.
    [AvaloniaTest]
    public void Typing_one_component_keeps_the_others_under_the_expression()
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 2, Value.Y * 2)");
        scope.Model.SetCurrentValueAndGetCoerced(new Point(1, 400), relative: false);
        scope.Model.SetCurrentValueAndGetCoerced(new Point(12, 400), relative: false);
        scope.Model.SetCurrentValueAndGetCoerced(new Point(120, 400), relative: false);
        scope.Model.SetValue(new Point(200, 400), new Point(120, 400));

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(new Point(120, 200)));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(100, 200)));
    }

    // Each keystroke comes with the other fields as the last one left them, which an expression mixing components changes.
    [AvaloniaTest]
    public void Typing_keeps_untouched_components_when_the_expression_mixes_them()
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 2, Value.X + Value.Y)");
        scope.Model.SetCurrentValueAndGetCoerced(new Point(1, 300), relative: false);
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(1, 200)));
        scope.Model.SetCurrentValueAndGetCoerced(new Point(12, 201), relative: false);
        scope.Model.SetValue(new Point(200, 300), new Point(12, 201));

        Assert.That(scope.Property.Value, Is.EqualTo(new Point(12, 200)));
    }

    [AvaloniaTest]
    public void A_masked_preview_writes_a_point_component_equal_to_the_shown_result()
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 12, Value.Y)");
        var context = (IValueEditorContext<(float, float)>)scope.Model;

        Assert.That(context.PreviewEdit((1, 200), relative: false, editedComponents: 0b01), Is.EqualTo((12f, 200f)));
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(1, 200)));
        var shown = context.PreviewEdit((12, 200), relative: false, editedComponents: 0b01);
        Assert.That(shown, Is.EqualTo((144f, 200f)));
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(12, 200)));
        Assert.That(scope.History.UndoCount, Is.Zero);

        context.ConfirmEdit((1200, 200), shown);
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(100, 200)));
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Typing_the_current_expression_result_still_edits_the_point(bool showUnderlying)
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 12, Value.Y)");
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        if (!showUnderlying)
            editor.Connect(ShowExpressionWhileFocused((IValueEditorContext<(float, float)>)scope.Model));

        TextBox box = ComponentBox(editor, "First");
        box.Focus();
        Assert.That(box.Text, Is.EqualTo(showUnderlying ? "100" : "1200"));
        box.SelectAll();
        host.Window.KeyTextInput("1");
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(1, 200)));
        Assert.That(editor.FirstValue, Is.EqualTo(12));
        host.Window.KeyTextInput("2");
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(12, 200)));
        Assert.That(scope.History.UndoCount, Is.Zero);

        ((Border)host.Window.Content!).Focus();
        Assert.That(box.Text, Is.EqualTo("144"));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(100, 200)));
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_uniform_point_edit_writes_both_components_under_an_expression(bool showUnderlying)
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(150, 250)");
        ((IConfigureUniformEditor)scope.Model).IsUniformEditorEnabled.Value = true;
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        Assert.That(editor.IsUniform, Is.True);
        if (!showUnderlying)
            editor.Connect(ShowExpressionWhileFocused((IValueEditorContext<(float, float)>)scope.Model));

        TextBox box = ComponentBox(editor, "First");
        box.Focus();
        Assert.That(box.Text, Is.EqualTo(showUnderlying ? "100" : "150"));
        box.SelectAll();
        host.Window.KeyTextInput("250");
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(250, 250)));

        ((Border)host.Window.Content!).Focus();
        Assert.That(box.Text, Is.EqualTo("150"));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(100, 200)));
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_uniform_relative_point_edit_writes_both_components_under_an_expression(bool showUnderlying)
        => AssertUniformRelativeEdit(new RelativePoint(100, 200, RelativeUnit.Absolute),
            "new RelativePoint(150, 250, RelativeUnit.Absolute)", new RelativePoint(250, 250, RelativeUnit.Absolute), showUnderlying);

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_uniform_relative_rect_edit_writes_all_components_under_an_expression(bool showUnderlying)
        => AssertUniformRelativeEdit(new RelativeRect(100, 200, 300, 400, RelativeUnit.Absolute),
            "new RelativeRect(150, 250, 350, 450, RelativeUnit.Absolute)",
            new RelativeRect(250, 250, 250, 250, RelativeUnit.Absolute), showUnderlying);

    private static void AssertUniformRelativeEdit<T>(T initial, string expression, T edited, bool showUnderlying)
    {
        using var scope = new ValueEditScope<T>(initial, expression);
        ((IConfigureUniformEditor)scope.Model).IsUniformEditorEnabled.Value = true;
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<VectorEditorBase>().Single();
        Assert.That(editor.IsUniformValue, Is.True);
        if (!showUnderlying)
            ((IValueEditorControl<T>)editor).Connect(ShowExpressionWhileFocused((IValueEditorContext<T>)scope.Model));

        TextBox box = ComponentBox(editor, "First");
        box.Focus();
        Assert.That(box.Text, Is.EqualTo(showUnderlying ? "100" : "150"));
        box.SelectAll();
        host.Window.KeyTextInput("250");
        Assert.That(scope.Property.Value, Is.EqualTo(edited));

        ((Border)host.Window.Content!).Focus();
        Assert.That(box.Text, Is.EqualTo("150"));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(initial));
    }

    [AvaloniaTest]
    [TestCase(RelativeUnit.Absolute)]
    [TestCase(RelativeUnit.Relative)]
    public void A_masked_relative_point_preview_takes_the_input_unit_and_keeps_the_unedited_component(RelativeUnit unit)
    {
        var initial = new RelativePoint(100, 200, RelativeUnit.Absolute);
        using var scope = new ValueEditScope<RelativePoint>(initial, "new RelativePoint(150, 250, RelativeUnit.Absolute)");
        var context = (IValueEditorContext<RelativePoint>)scope.Model;

        var shown = context.PreviewEdit(new(150, 250, unit), relative: false, editedComponents: 0b01);
        Assert.That(scope.Property.Value, Is.EqualTo(new RelativePoint(150, 200, unit)));
        context.ConfirmEdit(shown, shown);
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(initial));
    }

    [AvaloniaTest]
    [TestCase(RelativeUnit.Absolute)]
    [TestCase(RelativeUnit.Relative)]
    public void A_masked_relative_rect_preview_takes_the_input_unit_and_keeps_the_unedited_components(RelativeUnit unit)
    {
        var initial = new RelativeRect(100, 200, 300, 400, RelativeUnit.Absolute);
        using var scope = new ValueEditScope<RelativeRect>(initial, "new RelativeRect(150, 250, 350, 450, RelativeUnit.Absolute)");
        var context = (IValueEditorContext<RelativeRect>)scope.Model;

        var shown = context.PreviewEdit(new(150, 250, 350, 450, unit), relative: false, editedComponents: 0b1010);
        Assert.That(scope.Property.Value, Is.EqualTo(new RelativeRect(100, 250, 300, 450, unit)));
        context.ConfirmEdit(shown, shown);
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(initial));
    }

    // A picked color replaces the whole value below, even in the channels where it matches the expression's result.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_picked_color_replaces_every_channel_under_the_expression(bool previewed)
    {
        using var scope = new ValueEditScope<Color>(Colors.Blue, "Colors.Red");
        if (previewed)
            scope.Model.SetCurrentValueAndGetCoerced(Colors.Yellow, relative: false);
        scope.Model.SetValue(Colors.Red, Colors.Yellow);

        Assert.That(scope.Property.Value, Is.EqualTo(Colors.Yellow));
    }

    // A double click on the slider puts the default below the expression instead of offsetting by it.
    [AvaloniaTest]
    public void Resetting_the_slider_to_its_default_is_absolute_under_the_expression()
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50");
        using var host = scope.Show();
        var editor = (NumberSliderEditor<float>)scope.Control;
        float before = editor.Value;

        editor.ApplyAbsoluteValue(20);
        editor.ConfirmValue(before);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(20));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
    }

    // The expression can already show the default while the value below differs; the reset still writes it there.
    [AvaloniaTest]
    public void Resetting_the_slider_writes_the_default_even_when_the_expression_shows_it()
    {
        using var scope = new ValueEditScope<float>(50, "Value + 50");
        using var host = scope.Show();
        var editor = (NumberSliderEditor<float>)scope.Control;
        Assert.That(editor.Value, Is.EqualTo(100));

        editor.ApplyAbsoluteValue(100);
        editor.ConfirmValue(100);

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(100));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
    }

    // Typing after a wheel step replaces the value, and a wheel step after typing offsets from what was typed.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void Switching_between_wheel_and_typing_keeps_each_inputs_meaning(bool wheelFirst)
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50");
        if (wheelFirst)
        {
            scope.Model.SetCurrentValueAndGetCoerced(151, relative: true);
            Assert.That(scope.Property.Value, Is.EqualTo(101));
            scope.Model.SetCurrentValueAndGetCoerced(120, relative: false);
            Assert.That(scope.Property.Value, Is.EqualTo(120));
        }
        else
        {
            scope.Model.SetCurrentValueAndGetCoerced(120, relative: false);
            Assert.That(scope.Property.Value, Is.EqualTo(120));
            scope.Model.SetCurrentValueAndGetCoerced(171, relative: true);
            Assert.That(scope.Property.Value, Is.EqualTo(121));
        }
        scope.Model.SetValue(150, wheelFirst ? 170 : 171);

        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(100), "Undo goes back to the value before the first input.");
    }

    [AvaloniaTest]
    public void Rational_and_time_drags_offset_the_value_under_the_expression()
    {
        using (var scope = new ValueEditScope<Rational>(new Rational(1), "Value + Value"))
        {
            Assert.That(scope.Model, Is.InstanceOf<RationalEditorViewModel>());
            scope.Model.SetCurrentValueAndGetCoerced(new Rational(3));
            scope.Model.SetValue(new Rational(2), new Rational(3));
            Assert.That(scope.Property.Value, Is.EqualTo(new Rational(2)));
        }

        using (var scope = new ValueEditScope<MediaTime>(MediaTime.FromSeconds(10), "Value + Value"))
        {
            Assert.That(scope.Model, Is.InstanceOf<MediaTimeEditorViewModel>());
            scope.Model.SetCurrentValueAndGetCoerced(MediaTime.FromSeconds(30));
            scope.Model.SetValue(MediaTime.FromSeconds(20), MediaTime.FromSeconds(30));
            Assert.That(scope.Property.Value, Is.EqualTo(MediaTime.FromSeconds(20)));
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_point_drag_offsets_each_component_including_separated_keyframes(bool separated)
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X + 50, Value.Y - 20)", animated: separated, separated: separated);
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(new(155, 190)), Is.EqualTo(new Point(155, 190)));
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(new(160, 185)), Is.EqualTo(new Point(160, 185)));
        scope.Model.SetValue(new(150, 180), new(160, 185));

        Assert.Multiple(() =>
        {
            Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(new Point(110, 205)));
            Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        scope.History.Undo();
        Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(new Point(100, 200)));
    }

    [AvaloniaTest]
    public void Changing_relative_units_during_a_drag_uses_the_absolute_input()
    {
        using var scope = new ValueEditScope<RelativePoint>(new(0.1f, 0.2f, RelativeUnit.Relative),
            "new RelativePoint(Value.Point.X + 0.5f, Value.Point.Y + 0.5f, Value.Unit)");
        var input = new RelativePoint(80, 90, RelativeUnit.Absolute);
        scope.Model.SetCurrentValueAndGetCoerced(input);
        scope.Model.SetValue(new(0.6f, 0.7f, RelativeUnit.Relative), input);
        Assert.That(scope.Property.Value, Is.EqualTo(input));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new RelativePoint(0.1f, 0.2f, RelativeUnit.Relative)));
    }

    [AvaloniaTest]
    public void A_color_drag_can_decrease_unsigned_components_without_wrapping()
    {
        using var scope = new ValueEditScope<Color>(Color.FromArgb(255, 100, 200, 30),
            "Color.FromArgb(Value.A, (byte)(Value.R + 50), Value.G, Value.B)");
        Color shown = Color.FromArgb(255, 150, 200, 30);
        Color input = Color.FromArgb(245, 140, 210, 20);
        scope.Model.SetCurrentValueAndGetCoerced(input);
        scope.Model.SetValue(shown, input);
        Assert.That(scope.Property.Value, Is.EqualTo(Color.FromArgb(245, 90, 210, 20)));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(Color.FromArgb(255, 100, 200, 30)));
    }

    [AvaloniaTest]
    public void Opening_and_closing_a_color_picker_does_not_replace_the_underlying_value()
    {
        using var scope = new ValueEditScope<Color>(Colors.Blue, "Colors.Red");
        using var host = scope.Show();
        Button button = scope.Control.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PART_ColorPickerButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        Assert.That(scope.Property.Value, Is.EqualTo(Colors.Blue));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        Assert.That(scope.Property.Value, Is.EqualTo(Colors.Blue));
        Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
        Assert.That(scope.History.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public void Refreshing_an_open_grading_picker_does_not_write_the_expression_result_back()
    {
        using var scope = new ValueEditScope<GradingColor>(GradingColor.Zero, "Value + new GradingColor(0.5f, 0.5f, 0.5f)");
        using var host = scope.Show();
        Button button = scope.Control.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PART_ColorPickerButton");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        scope.Property.Value = GradingColor.One;
        Assert.That(scope.Property.Value, Is.EqualTo(GradingColor.One));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        Assert.That(scope.Property.Value, Is.EqualTo(GradingColor.One));
        Assert.That(scope.History.UndoCount, Is.Zero, "A display refresh must not commit an edit.");
    }

    [AvaloniaTest]
    public void A_non_arithmetic_edit_is_absolute_even_when_the_input_equals_the_shown_value()
    {
        using var scope = new ValueEditScope<bool>(false, "!Value");
        scope.Model.SetValue(true, true);
        Assert.That(scope.Property.Value, Is.True);
        Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.False);
    }

    [AvaloniaTest]
    public void An_object_editor_stays_disabled_under_an_expression()
    {
        var shape = new RectShape();
        shape.Fill.Value = new SolidColorBrush(Colors.Red);
        shape.Fill.Expression = new StringExpression<Brush?>("Value");
        var adapter = PropertyAdapterFactory.CreateAdapter(shape.Fill, shape);
        (PropertyEditorViewModel model, _) = PropertyEditorTestHost.CreateControl(adapter);
        using (model)
        {
            Assert.That(model, Is.InstanceOf<BrushEditorViewModel>());
            Assert.That(model.CanEdit.Value, Is.False);
        }
    }

    [AvaloniaTest]
    public void A_locked_element_still_blocks_an_expression_value_edit()
    {
        var element = new Element { IsLocked = { Value = true } };
        using var scope = new ValueEditScope<float>(100, "Value + 50", element: element);
        Assert.That(scope.Model.CanEdit.Value, Is.False);
        Assert.That(((NumberSliderEditor<float>)scope.Control).IsReadOnly, Is.True);
        scope.Model.SetValue(150, 120);
        Assert.That(scope.Model.SetCurrentValueAndGetCoerced(160), Is.EqualTo(150));
        Assert.That(scope.Property.Value, Is.EqualTo(100));
        Assert.That(scope.History.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    [TestCase("Value + 50", 160)]
    [TestCase("Value * 2", 220)]
    [TestCase("150f", 150)]
    public void The_number_field_keeps_drag_input_separate_from_the_expression_result(string expression, float expected)
    {
        using var scope = new ValueEditScope<float>(100, expression);
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        using var host = scope.Show();
        var slider = scope.Control.GetVisualDescendants().OfType<ValueSliderField>().Single();
        var editor = (NumberSliderEditor<float>)scope.Control;
        AvaPoint press = HeadlessTestHelpers.Center(slider, host.Window);
        TimelineTestHost.WaitForHitTest(host.Window, slider, press);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(press + new AvaPoint(4, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(scope.Property.Value, Is.EqualTo(100), "Crossing the drag threshold must not jump.");
        host.Window.MouseMove(press + new AvaPoint(9, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(scope.Property.Value, Is.EqualTo(105));
        AvaPoint end = press + new AvaPoint(14, 0);
        host.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.That(scope.Property.Value, Is.EqualTo(110));
        Assert.That(editor.Value, Is.EqualTo(expected));
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.Window, nameof(PropertyExpressionTests), $"number-result-{expected}");
        Assert.That(editor.Value, Is.EqualTo(expected));
        Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(100));
    }

    [AvaloniaTest]
    public void Typing_in_the_number_field_is_absolute_and_then_shows_the_expression_result()
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50");
        using var host = scope.Show();
        var box = scope.Control.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PART_InnerTextBox");
        Assert.That(box.Text, Is.EqualTo("150"));
        box.Focus();
        Assert.That(box.Text, Is.EqualTo("100"), "Focusing shows the underlying value before the expression.");
        box.SelectAll();
        host.Window.KeyTextInput("120");
        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(120));
            Assert.That(((NumberSliderEditor<float>)scope.Control).Value, Is.EqualTo(170));
            Assert.That(box.Text, Is.EqualTo("170"));
            Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(100));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Focusing_the_number_field_shows_the_underlying_value_without_committing_on_blur(bool animated)
    {
        using var scope = new ValueEditScope<float>(100, "Value + 50", animated);
        using var host = scope.Show();
        var box = scope.Control.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PART_InnerTextBox");
        Assert.That(box.Text, Is.EqualTo("150"));

        box.Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("100"));
            Assert.That(scope.History.UndoCount, Is.Zero);
        });

        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("150"));
            Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(100));
            Assert.That(scope.History.UndoCount, Is.Zero);
        });

        // Stepping by wheel while focused keeps showing the underlying value until blur.
        box.Focus();
        AvaPoint point = HeadlessTestHelpers.Center(box, host.Window);
        host.Window.MouseWheel(point, new Avalonia.Vector(0, 1));
        HeadlessTestHelpers.Render();
        Assert.That(box.Text, Is.EqualTo("110"));
        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(110));
            Assert.That(box.Text, Is.EqualTo("160"));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Focusing_a_vector_field_shows_the_underlying_component_and_accepts_the_shown_value(bool animated)
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 2, Value.Y * 2)", animated);
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        TextBox[] boxes = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("200", "400")));

        boxes[0].Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(boxes[0].Text, Is.EqualTo("100"));
            Assert.That(boxes[1].Text, Is.EqualTo("400"));
            Assert.That(scope.History.UndoCount, Is.Zero);
        });

        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("200", "400")));
            Assert.That(scope.History.UndoCount, Is.Zero);
        });

        // Replacing the underlying component with the currently displayed value in one shot (as paste does) still writes it.
        boxes[0].Focus();
        boxes[0].SetCurrentValue(TextBox.TextProperty, "200");
        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(new Point(200, 200)));
            Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("400", "400")));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_point_field_drag_does_not_feed_the_expression_result_into_the_next_move()
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 2, Value.Y * 2)");
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        var box = editor.GetVisualDescendants().OfType<TextBox>().First();
        AvaPoint press = HeadlessTestHelpers.Center(box, host.Window);
        TimelineTestHost.WaitForHitTest(host.Window, box, press);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(press + new AvaPoint(4, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseMove(press + new AvaPoint(9, 0), RawInputModifiers.LeftMouseButton);
        AvaPoint end = press + new AvaPoint(14, 0);
        host.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(new Point(110, 200)));
            Assert.That((editor.FirstValue, editor.SecondValue), Is.EqualTo((220f, 400f)));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        HeadlessCapture.SaveIfRequested(host.Window, nameof(PropertyExpressionTests), "point-drag");
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(100, 200)));
    }

    [AvaloniaTest]
    public void Stepping_one_point_component_does_not_feed_back_the_other_components_result()
    {
        using var scope = new ValueEditScope<Point>(new(100, 200), "new Point(Value.X * 2, Value.Y * 2)");
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        var box = editor.GetVisualDescendants().OfType<TextBox>().First();
        box.Focus();
        AvaPoint point = HeadlessTestHelpers.Center(box, host.Window);
        host.Window.MouseWheel(point, new Avalonia.Vector(0, 1));
        host.Window.MouseWheel(point, new Avalonia.Vector(0, 1));
        ((Border)host.Window.Content!).Focus();
        Assert.That(scope.Property.Value, Is.EqualTo(new Point(120, 200)));
        Assert.That((editor.FirstValue, editor.SecondValue), Is.EqualTo((240f, 400f)));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Stepping_a_focused_relative_field_keeps_the_underlying_unit_and_steps_the_underlying_value(bool animated)
    {
        using var scope = new ValueEditScope<RelativePoint>(
            new(0.1f, 0.2f, RelativeUnit.Relative),
            "new RelativePoint(80, 90, RelativeUnit.Absolute)",
            animated);
        using var host = scope.Show();
        var editor = scope.Control.GetVisualDescendants().OfType<RelativePointEditor>().Single();
        TextBox[] boxes = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("80", "90")));

        boxes[0].Focus();
        HeadlessTestHelpers.Render();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(($"{10f:f}%", "90")));

        AvaPoint point = HeadlessTestHelpers.Center(boxes[0], host.Window);
        host.Window.MouseWheel(point, new Avalonia.Vector(0, 1));
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            RelativePoint current = scope.Model.GetEditingValue();
            Assert.That(current.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(current.Point.X, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(current.Point.Y, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(($"{20f:f}%", "90")));
        });

        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            RelativePoint current = scope.Model.GetEditingValue();
            Assert.That(current.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(current.Point.X, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(current.Point.Y, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("80", "90")));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });

        scope.History.Undo();
        Assert.That(scope.Model.GetEditingValue(), Is.EqualTo(new RelativePoint(0.1f, 0.2f, RelativeUnit.Relative)));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void The_grading_wheel_commits_the_underlying_drag_even_with_a_constant_result(bool constant, bool live)
    {
        string expression = "new GradingColor(0.5f, 0.5f, 0.5f)";
        using var scope = new ValueEditScope<GradingColor>(GradingColor.Zero, constant ? expression : "Value + " + expression);
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        var wheel = new ColorGradingWheel();
        var model = (GradingColorEditorViewModel)scope.Model;
        model.IsLivePreviewEnabled.Value = live;
        model.Accept(wheel);
        using var host = scope.Show(wheel, height: 380);
        var pad = wheel.GetVisualDescendants().OfType<ColorBalancePad>().Single();
        AvaPoint start = pad.TranslatePoint(pad.DiskBounds.Center, host.Window)!.Value;
        host.Window.MouseMove(start);
        host.Window.MouseDown(start, MouseButton.Left);
        host.Window.MouseMove(start + new Avalonia.Vector(15, 0), RawInputModifiers.LeftMouseButton);
        AvaPoint end = start + new Avalonia.Vector(30, 0);
        host.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.That(scope.Property.Value, Is.Not.EqualTo(GradingColor.Zero));
        Assert.That(wheel.Color, Is.EqualTo(scope.Property.Evaluate(new EvaluationContext(MediaTime.Zero))));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        Assert.That(scope.History.IsEditing, Is.False);
        scope.History.Undo();
        Assert.That(scope.Property.Value, Is.EqualTo(GradingColor.Zero));
        Assert.That(scope.Property.Expression, Is.SameAs(scope.Expression));
    }

    // The wheel shows a constant result while the drag moves the value below, so moving back to the start must still undo it.
    [AvaloniaTest]
    public void A_grading_wheel_drag_back_to_its_start_leaves_the_value_under_a_constant_result()
    {
        using var scope = new ValueEditScope<GradingColor>(GradingColor.Zero, "new GradingColor(0.5f, 0.5f, 0.5f)");
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        var wheel = new ColorGradingWheel();
        var model = (GradingColorEditorViewModel)scope.Model;
        model.IsLivePreviewEnabled.Value = true;
        model.Accept(wheel);
        using var host = scope.Show(wheel, height: 380);
        var pad = wheel.GetVisualDescendants().OfType<ColorBalancePad>().Single();
        AvaPoint start = pad.TranslatePoint(pad.DiskBounds.Center, host.Window)!.Value;
        host.Window.MouseMove(start);
        host.Window.MouseDown(start, MouseButton.Left);
        host.Window.MouseMove(start + new Avalonia.Vector(15, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseMove(start + new Avalonia.Vector(30, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(scope.Property.Value, Is.Not.EqualTo(GradingColor.Zero), "The drag moved the value below.");
        host.Window.MouseMove(start, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(start, MouseButton.Left);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(scope.Property.Value, Is.EqualTo(GradingColor.Zero));
            Assert.That(wheel.Color, Is.EqualTo(new GradingColor(0.5f, 0.5f, 0.5f)), "The wheel shows the result again.");
        });
    }

    [AvaloniaTest]
    public void A_combined_editor_maps_the_drag_once_and_commits_all_components_together()
    {
        var transform = new TranslateTransform { X = { Value = 100 }, Y = { Value = 200 } };
        using var history = new PropertyEditorTestHost.HistoryScope(transform);
        using var model = CreateCombinedPointEditor(transform);
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        Assert.That(model.SetExpression("new Point(150, 250)", out string? error), Is.True, error);
        int before = history.History.UndoCount;
        Assert.That(model.PreviewEdit((155, 260)), Is.EqualTo((150f, 250f)));
        model.ConfirmEdit((150, 250), (150, 250));
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((105f, 210f)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
        history.History.Undo();
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((100f, 200f)));
        Assert.That(transform.X.HasExpression && transform.Y.HasExpression, Is.True);
    }

    [AvaloniaTest]
    public void A_masked_combined_preview_writes_every_linked_property()
    {
        var transform = new TranslateTransform { X = { Value = 100 }, Y = { Value = 200 } };
        using var history = new PropertyEditorTestHost.HistoryScope(transform);
        using var model = CreateCombinedPointEditor(transform);
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        Assert.That(model.SetExpression("new Point(150, 250)", out string? error), Is.True, error);
        int before = history.History.UndoCount;

        var shown = model.PreviewEdit((250, 250), relative: false, editedComponents: 0b11);
        Assert.That(shown, Is.EqualTo((150f, 250f)));
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((250f, 250f)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before));
        model.ConfirmEdit(shown, shown);

        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
        history.History.Undo();
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((100f, 200f)));
    }

    [AvaloniaTest]
    [TestCase(true, "250", false, 250f)]
    [TestCase(false, "250", false, 250f)]
    [TestCase(true, "100", false, 100f)]
    [TestCase(true, "100", true, 100f)]
    public async Task A_uniform_combined_field_edit_writes_both_properties(bool showUnderlying, string input, bool paste, float expected)
    {
        var transform = new TranslateTransform { X = { Value = 100 }, Y = { Value = 200 } };
        using var history = new PropertyEditorTestHost.HistoryScope(transform);
        using var model = CreateCombinedPointEditor(transform);
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        Assert.That(model.SetExpression("new Point(150, 250)", out string? error), Is.True, error);
        int before = history.History.UndoCount;

        model.IsUniformEditorEnabled.Value = true;
        Assert.That(PropertyEditorHost.TryCreateControl(model, PropertyEditorSurface.Panel, out Control? control), Is.True);
        control!.DataContext = model;
        var container = new Border { Focusable = true, Child = control };
        using var host = new ControlHost(container, width: 500, height: 120);
        var editor = container.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        Assert.That(editor.IsUniform, Is.True);
        if (!showUnderlying)
            editor.Connect(ShowExpressionWhileFocused(model));

        TextBox box = ComponentBox(editor, "First");
        box.Focus();
        Assert.That(box.Text, Is.EqualTo(showUnderlying ? "100" : "150"));
        box.SelectAll();
        if (paste)
        {
            await host.Window.Clipboard!.SetTextAsync(input);
            box.Paste();
            HeadlessTestHelpers.Settle();
        }
        else
        {
            host.Window.KeyTextInput(input);
        }
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((expected, expected)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before));

        container.Focus();
        Assert.That(box.Text, Is.EqualTo("150"));
        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
        history.History.Undo();
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((100f, 200f)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_two_component_vector_reports_the_edited_components(bool uniform)
        => AssertComponentPreviews(new Vector2Editor<float> { IsUniform = uniform }, (10f, 20f), "Second", uniform ? 0b11 : 0b10);

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_three_component_vector_reports_the_edited_components(bool uniform)
        => AssertComponentPreviews(new Vector3Editor<float> { IsUniform = uniform }, (10f, 20f, 30f), "Third", uniform ? 0b111 : 0b100);

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_four_component_vector_reports_the_edited_components(bool uniform)
        => AssertComponentPreviews(new Vector4Editor<float> { IsUniform = uniform }, (10f, 20f, 30f, 40f), "Fourth", uniform ? 0b1111 : 0b1000);

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_relative_point_reports_the_edited_components(bool uniform)
        => AssertComponentPreviews(new RelativePointEditor { IsUniform = uniform },
            new RelativePoint(10, 20, RelativeUnit.Absolute), "Second", uniform ? 0b11 : 0b10);

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void A_relative_rect_reports_the_edited_components(bool uniform)
        => AssertComponentPreviews(new RelativeRectEditor { IsUniform = uniform },
            new RelativeRect(10, 20, 30, 40, RelativeUnit.Absolute), "Fourth", uniform ? 0b1111 : 0b1000);

    private static void AssertComponentPreviews<TEdit>(VectorEditorBase editor, TEdit shown, string individualPart, int expectedMask)
    {
        using var context = new RecordingValueContext<TEdit>(Observable.Return(shown));
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        editor.Header = "Components";
        ((IValueEditorControl<TEdit>)editor).Connect(context);
        var container = new Border { Focusable = true, Child = editor };
        using var host = new ControlHost(container, width: 500, height: 120);
        TextBox box = ComponentBox(editor, editor.IsUniformValue ? "First" : individualPart);

        box.Focus();
        box.SelectAll();
        host.Window.KeyTextInput("42");
        Assert.That(context.Previews, Is.EqualTo(new[] { (false, expectedMask) }));

        AvaPoint center = HeadlessTestHelpers.Center(box, host.Window);
        TimelineTestHost.WaitForHitTest(host.Window, box, center);
        host.Window.MouseWheel(center, new Avalonia.Vector(0, 1));
        Assert.That(context.Previews, Is.EqualTo(new[] { (false, expectedMask), (true, expectedMask) }));
        container.Focus();
        Assert.That(context.ConfirmCount, Is.EqualTo(1));

        context.Previews.Clear();
        host.Window.MouseMove(center);
        host.Window.MouseDown(center, MouseButton.Left);
        host.Window.MouseMove(center + new AvaPoint(4, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseMove(center + new AvaPoint(10, 0), RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(center + new AvaPoint(10, 0), MouseButton.Left);
        Assert.That(context.Previews, Is.Not.Empty);
        Assert.That(context.Previews, Is.All.EqualTo((true, expectedMask)));
        Assert.That(context.ConfirmCount, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public void Nested_value_changes_keep_the_mask_of_each_routed_event()
    {
        var editor = new Vector2Editor<float>();
        using var context = new RecordingValueContext<(float, float)>(Observable.Return((10f, 20f)));
        var outer = new PropertyEditorValueChangedEventArgs<(float, float)>((30, 20), (10, 20), PropertyEditor.ValueChangedEvent);
        var inner = new PropertyEditorValueChangedEventArgs<(float, float)>((10, 40), (10, 20), PropertyEditor.ValueChangedEvent);
        var raw = new PropertyEditorValueChangedEventArgs<(float, float)>((10, 50), (10, 20), PropertyEditor.ValueChangedEvent);
        editor.ValueChanged += (_, args) =>
        {
            if (ReferenceEquals(args, outer))
            {
                editor.RaiseEvent(raw);
                editor.RaiseValueChanged(inner, 0b10);
            }
        };
        editor.Connect(context);

        editor.RaiseValueChanged(outer, 0b01);
        Assert.That(context.LegacyPreview, Is.EqualTo(((10f, 50f), true)));
        Assert.That(context.Previews, Is.EqualTo(new[] { (true, 0b10), (true, 0b01) }));
    }

    [Test]
    public void The_default_component_preview_delegates_to_the_earlier_overload()
    {
        using var legacy = new LegacyValueContext<int>(Observable.Return(10));
        IValueEditorContext<int> context = legacy;

        Assert.That(context.PreviewEdit(20, relative: true, editedComponents: 0b01), Is.EqualTo(20));
        Assert.That(legacy.LegacyPreview, Is.EqualTo((20, true)));
    }

    [AvaloniaTest]
    public void A_custom_focused_preview_receives_the_complete_underlying_value()
    {
        using var context = new FocusedPointContext();
        var editor = new Vector2Editor<float>();
        editor.Connect(context);
        using var host = new ControlHost(editor, width: 500, height: 120);
        TextBox box = ComponentBox(editor, "First");
        box.Focus();
        Assert.That(box.Text, Is.EqualTo("100"));

        box.SelectAll();
        host.Window.KeyTextInput("120");
        Assert.That(context.FocusedPreview, Is.EqualTo((120f, 200f)));
        Assert.That(context.LegacyPreview, Is.Null);
    }

    private static TextBox ComponentBox(Control editor, string part)
        => editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == $"PART_Inner{part}TextBox");

    private static CombinedEditorViewModel<float, (float, float)> CreateCombinedPointEditor(TranslateTransform transform)
    {
        var contexts = new List<IPropertyEditorContext>();
        PropertyEditorResolver.CreateContexts(PropertyAdapterFactory.CreateAdapters(transform),
            [PropertyEditorExtension.Instance], PropertyEditorSurface.Panel, contexts.Add);
        var row = contexts.OfType<CombinedEditorViewModel<float, (float, float)>>().Single();
        foreach (IPropertyEditorContext context in contexts)
        {
            if (!ReferenceEquals(context, row)) context.Dispose();
        }
        return row;
    }

    private static RecordingValueContext<TEdit> ShowExpressionWhileFocused<TEdit>(IValueEditorContext<TEdit> context)
        => new(context.EditValue, context.PreviewEdit, context.ConfirmEdit);

    private class LegacyValueContext<TEdit>(IObservable<TEdit> editValue) : IValueEditorContext<TEdit>
    {
        public IObservable<TEdit> EditValue => editValue;
        public PropertyEditorExtension Extension => PropertyEditorExtension.Instance;
        public (TEdit Value, bool Relative)? LegacyPreview { get; private set; }
        public int ConfirmCount { get; private set; }

        public TEdit PreviewEdit(TEdit value) => PreviewEdit(value, relative: false);

        public TEdit PreviewEdit(TEdit value, bool relative)
        {
            LegacyPreview = (value, relative);
            return value;
        }

        public virtual void ConfirmEdit(TEdit oldValue, TEdit newValue) => ConfirmCount++;

        public void Accept(IPropertyEditorContextVisitor visitor) { }
        public void Dispose() { }
        public void ReadFromJson(JsonObject json) { }
        public void WriteToJson(JsonObject json) { }
    }

    private sealed class RecordingValueContext<TEdit>(
        IObservable<TEdit> editValue, Func<TEdit, bool, int, TEdit>? preview = null, Action<TEdit, TEdit>? confirm = null)
        : LegacyValueContext<TEdit>(editValue), IValueEditorContext<TEdit>
    {
        public List<(bool Relative, int Mask)> Previews { get; } = [];

        public TEdit PreviewEdit(TEdit value, bool relative, int editedComponents)
        {
            Previews.Add((relative, editedComponents));
            return preview is null ? value : preview(value, relative, editedComponents);
        }

        public override void ConfirmEdit(TEdit oldValue, TEdit newValue)
        {
            base.ConfirmEdit(oldValue, newValue);
            confirm?.Invoke(oldValue, newValue);
        }
    }

    private sealed class FocusedPointContext : LegacyValueContext<(float, float)>, IValueEditorContext<(float, float)>
    {
        public FocusedPointContext() : base(Observable.Return((150f, 250f))) { }

        public (float, float) FocusedPreview { get; private set; } = (100, 200);

        public bool TryGetFocusedEditValue(out (float, float) value)
        {
            value = FocusedPreview;
            return true;
        }

        public (float, float) PreviewFocusedEdit((float, float) value)
        {
            FocusedPreview = value;
            return (150, 250);
        }
    }

    private sealed class ValueEditScope<T> : IDisposable
    {
        private readonly PropertyEditorTestHost.HistoryScope _history;

        public ValueEditScope(T value, string expression, bool animated = false, bool separated = false, Element? element = null)
        {
            var owner = new ComponentTestObject<T>();
            Property = owner.Value;
            Property.Value = value;
            if (animated)
            {
                var animation = new KeyFrameAnimation<T>();
                animation.KeyFrames.Add(new KeyFrame<T> { Value = { Value = value } });
                Property.SetKeyFrameAnimation(animation);
            }
            if (separated) ComponentAnimations.Separate(Property);
            Property.Expression = new StringExpression<T>(expression);
            Expression = (StringExpression<T>)Property.Expression;
            _history = new PropertyEditorTestHost.HistoryScope(owner);
            (PropertyEditorViewModel model, Control control) = PropertyEditorTestHost.CreateControl(new AnimatablePropertyAdapter<T>(Property, owner));
            Model = (PropertyEditorViewModel<T>)model;
            Control = control;
            var clock = new Mock<IEditorClock>();
            clock.SetupGet(x => x.CurrentTime).Returns(new ReactivePropertySlim<MediaTime>(MediaTime.Zero));
            model.Accept(new PropertyEditorTestHost.ServiceVisitor(
                (typeof(HistoryManager), History), (typeof(Element), element), (typeof(IEditorClock), clock.Object)));
        }

        public AnimatableProperty<T> Property { get; }
        public StringExpression<T> Expression { get; }
        public PropertyEditorViewModel<T> Model { get; }
        public Control Control { get; }
        public HistoryManager History => _history.History;

        public ControlHost Show(Control? content = null, int height = 100)
        {
            var window = new Window
            {
                Content = new Border { Focusable = true, Child = content ?? Control },
                Width = 500,
                Height = height
            };
            Beutl.Controls.ControlConfiguration.SetConfiguration(window, TestShell.Configuration);
            Beutl.Controls.ControlFonts.SetFonts(window, TestShell.FontManager);
            Beutl.Controls.ControlNotifications.SetNotifications(window, TestShell.Notifications);
            return new ControlHost(window);
        }

        public void Dispose()
        {
            Model.Dispose();
            _history.Dispose();
        }
    }
}
