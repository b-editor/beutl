using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AlignmentEditorLayoutTests
{
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
        var scope = new StackPanel { Children = { number, x, y } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        foreach (PropertyEditor editor in new PropertyEditor[] { number, x, y })
        {
            editor.KeyFrameCount = keyFrames;
            editor.MenuContent = new Border { Width = 24, Height = 24 };
        }
        var window = new Window { Content = scope, Width = width, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            double inputX = number.GetVisualDescendants().OfType<TextBox>().Single().TranslatePoint(default, scope)!.Value.X;
            foreach (PropertyEditor editor in new PropertyEditor[] { x, y })
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
        }
        finally { window.Close(); }
    }
}
