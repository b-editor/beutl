using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Beutl.Controls/Styles.axaml centers a TextBox's text vertically. That suits a single line, but a taller
// box that accepts Return (an AI prompt, a marker note) then shows its first line, or its placeholder,
// floating in the middle. Such a box starts its text at the top, where a single-line box shows its line.
[TestFixture]
public class MultilineTextBoxAlignmentTests
{
    [AvaloniaTest]
    public void Multiline_textbox_starts_its_text_where_a_single_line_box_shows_it()
    {
        var single = new TextBox { Text = "Sample" };
        var text = new TextBox { Text = "Sample\nSecond line", Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var placeholder = new TextBox { PlaceholderText = "Sample", Height = 80, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var window = new Window
        {
            Content = new StackPanel { Spacing = 8, Children = { single, text, placeholder } },
            Width = 320,
            Height = 280,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.That(single.VerticalContentAlignment, Is.EqualTo(VerticalAlignment.Center),
                "a single-line box keeps its text centered");
            double lineTop = TopOf(single, "PART_TextPresenter");
            Assert.That(TopOf(text, "PART_TextPresenter"), Is.EqualTo(lineTop).Within(0.5),
                "the text of a multi-line box should start at the single-line box's line");
            Assert.That(TopOf(placeholder, "PART_Placeholder"), Is.EqualTo(lineTop).Within(0.5),
                "the placeholder of a multi-line box should start at the single-line box's line");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static double TopOf(TextBox textBox, string name)
    {
        Control part = textBox.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);
        return part.TranslatePoint(default, textBox)!.Value.Y;
    }
}
