using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ColorEditorAlignmentTests
{
    [AvaloniaTest]
    [TestCase(280, 0)]
    [TestCase(280, 3)]
    [TestCase(400, 0)]
    [TestCase(400, 3)]
    [TestCase(760, 0)]
    [TestCase(760, 3)]
    public void Color_and_number_boxes_start_at_the_same_position(int width, int keyFrames)
    {
        var number = new NumberEditor<float> { Header = "Opacity", Value = 75 };
        var color = new ColorEditor { Header = "Color" };
        var grading = new GradingColorEditor { Header = "Gain" };
        var scope = new StackPanel { Children = { number, color, grading } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        foreach (PropertyEditor editor in new PropertyEditor[] { number, color, grading })
        {
            editor.KeyFrameCount = keyFrames;
            editor.MenuContent = new Border { Width = 24, Height = 24 };
        }
        var window = new Window { Content = scope, Width = width, Height = 300 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBox input = number.GetVisualDescendants().OfType<TextBox>().Single();
            double expected = input.TranslatePoint(default, scope)!.Value.X;
            foreach (PropertyEditor editor in new PropertyEditor[] { color, grading })
            {
                Button button = editor.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "PART_ColorPickerButton");
                Assert.That(button.TranslatePoint(default, scope)!.Value.X, Is.EqualTo(expected).Within(1));
                Assert.That(button.Bounds.Width, Is.EqualTo(input.Bounds.Width).Within(1));
            }
        }
        finally { window.Close(); }
    }
}
