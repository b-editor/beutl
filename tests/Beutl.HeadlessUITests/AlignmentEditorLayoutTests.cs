using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AlignmentEditorLayoutTests
{
    [AvaloniaTest]
    [TestCase(EditorSurface.Property)]
    [TestCase(EditorSurface.Node)]
    [TestCase(EditorSurface.Settings)]
    [TestCase(EditorSurface.ListItem)]
    public void Text_alignment_icons_edit_the_text_property_and_support_undo(EditorSurface surface)
    {
        var text = new Beutl.Graphics.Shapes.TextBlock();
        Assert.That(text.GetDisplayProperties(), Does.Contain(text.TextAlignment));
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TextAlignment>(text.TextAlignment, text), surface);
        using var viewModel = vm;
        using var history = new HistoryScope(text);
        vm.Accept(ServiceVisitor.For(history.History));
        Assert.That(control, Is.TypeOf<TextAlignmentEditor>());
        var editor = (TextAlignmentEditor)control;
        var window = new Window { Content = control, Width = 400, Height = 180 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            RadioButton[] buttons = editor.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.That(buttons, Has.Length.EqualTo(3));
            Assert.That(buttons.Select(button => ((FluentIcon)button.Content!).Icon), Is.EqualTo(new[]
            {
                Icon.TextAlignLeft, Icon.TextAlignCenter, Icon.TextAlignRight,
            }));
            string[] labels =
            {
                GraphicsStrings.TextAlignment_Left, GraphicsStrings.TextAlignment_Center, GraphicsStrings.TextAlignment_Right,
            };
            Assert.That(buttons.Select(ToolTip.GetTip), Is.EqualTo(labels));
            Assert.That(buttons.Select(AutomationProperties.GetName), Is.EqualTo(labels));
            Assert.That(buttons[0].IsChecked, Is.True);

            foreach (int index in new[] { 1, 2, 0 })
            {
                RadioButton button = buttons[index];
                Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
                Assert.That(text.TextAlignment.CurrentValue, Is.EqualTo((TextAlignment)index));
                Assert.That(button.IsChecked, Is.True);
                Assert.That(buttons.Count(b => b.IsChecked == true), Is.EqualTo(1));
            }

            Assert.That(history.History.UndoCount, Is.EqualTo(3));
            history.History.Undo();
            HeadlessTestHelpers.Render();
            Assert.That(text.TextAlignment.CurrentValue, Is.EqualTo(TextAlignment.End));
            Assert.That(editor.Value, Is.EqualTo(TextAlignment.End));
            Assert.That(buttons[2].IsChecked, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(220, 0)]
    [TestCase(220, 3)]
    [TestCase(280, 0)]
    [TestCase(280, 3)]
    [TestCase(400, 0)]
    [TestCase(400, 3)]
    [TestCase(760, 0)]
    [TestCase(760, 3)]
    public void Alignment_buttons_keep_compact_and_right_aligned_layouts_and_share_the_wide_input_edge(int width, int keyFrames)
    {
        var number = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var x = new AlignmentXEditor { Header = "Alignment X", Value = AlignmentX.Center };
        var y = new AlignmentYEditor { Header = "Alignment Y", Value = AlignmentY.Center };
        var text = new TextAlignmentEditor { Header = "Text Alignment", Value = TextAlignment.Center };
        var scope = new StackPanel { Children = { number, x, y, text } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        foreach (PropertyEditor editor in new PropertyEditor[] { number, x, y, text })
        {
            editor.KeyFrameCount = keyFrames;
            editor.MenuContent = new Border { Width = 24, Height = 24 };
        }
        var window = new Window { Content = scope, Width = width, Height = 400 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            double inputX = number.GetVisualDescendants().OfType<TextBox>().Single().TranslatePoint(default, scope)!.Value.X;
            foreach (PropertyEditor editor in new PropertyEditor[] { x, y, text })
            {
                RadioButton[] buttons = editor.GetVisualDescendants().OfType<RadioButton>().ToArray();
                Assert.That(buttons, Has.Length.EqualTo(3));
                var header = editor.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "PART_HeaderTextBlock");
                double firstX = buttons[0].TranslatePoint(default, scope)!.Value.X;
                double firstY = buttons[0].TranslatePoint(default, scope)!.Value.Y;
                if (width <= 224)
                {
                    Assert.That(firstY, Is.GreaterThanOrEqualTo(header.TranslatePoint(default, scope)!.Value.Y + header.Bounds.Height));
                }
                else if (width >= 640)
                {
                    Assert.That(firstX, Is.EqualTo(inputX).Within(1));
                }
                else
                {
                    var panel = editor.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "PART_AlignmentButtons");
                    Assert.That(panel.HorizontalAlignment, Is.EqualTo(Avalonia.Layout.HorizontalAlignment.Right));
                    Control next = keyFrames > 0
                        ? editor.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PART_LeftButton")
                        : editor.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_MenuContentPresenter");
                    double lastRight = buttons[^1].TranslatePoint(default, scope)!.Value.X + buttons[^1].Bounds.Width;
                    Assert.That(lastRight, Is.EqualTo(next.TranslatePoint(default, scope)!.Value.X - 4).Within(1));
                }
                double previousRight = firstX;
                foreach (RadioButton button in buttons)
                {
                    double left = button.TranslatePoint(default, scope)!.Value.X;
                    Assert.That(left, Is.GreaterThanOrEqualTo(previousRight - 1));
                    Assert.That(button.Bounds.Width, Is.GreaterThanOrEqualTo(26));
                    previousRight = left + button.Bounds.Width;
                }
                foreach (Button arrow in editor.GetVisualDescendants().OfType<Button>()
                             .Where(b => b.IsEffectivelyVisible && b.Name is "PART_LeftButton" or "PART_RightButton"))
                {
                    Point position = arrow.TranslatePoint(default, scope)!.Value;
                    if (width <= 224)
                        Assert.That(position.Y + arrow.Bounds.Height, Is.LessThanOrEqualTo(firstY));
                    else
                        Assert.That(position.X, Is.GreaterThanOrEqualTo(previousRight - 1));
                }

                RadioButton last = buttons[2];
                Point point = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                HeadlessTestHelpers.Render();
                Assert.That(last.IsChecked, Is.True);
                Assert.That(buttons.Count(b => b.IsChecked == true), Is.EqualTo(1));
            }
            Assert.That(x.Value, Is.EqualTo(AlignmentX.Right));
            Assert.That(y.Value, Is.EqualTo(AlignmentY.Bottom));
            Assert.That(text.Value, Is.EqualTo(TextAlignment.End));
        }
        finally { window.Close(); }
    }
}
