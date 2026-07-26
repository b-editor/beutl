using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// FluentAvalonia's focused TextBox border is 1,1,1,2 (a thick bottom accent line);
// TextControlBorderThemeThicknessFocused (Beutl.Controls/Styles.axaml) flattens it to a uniform
// 1px so the focus accent reads through color, not a heavier bottom edge.
[TestFixture]
public class TextBoxFocusBorderTests
{
    [AvaloniaTest]
    public void Focused_textbox_border_is_uniform_one_pixel()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        Assert.That(Application.Current!.TryGetResource("TextControlBorderThemeThicknessFocused", ThemeVariant.Dark, out object? focused), Is.True,
            "the focused border-thickness token should resolve");
        Assert.That(focused, Is.EqualTo(new Thickness(1)),
            "the focused TextBox border must be a uniform 1px, not FluentAvalonia's 1,1,1,2 bottom accent line");

        var textBox = new TextBox { Text = "1920", Width = 160 };
        var window = new Window { Content = textBox, Width = 260, Height = 120 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Border border = textBox.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(1)), "resting TextBox border");

            textBox.Focus();
            HeadlessTestHelpers.Render(3);
            Assert.That(border.BorderThickness, Is.EqualTo(new Thickness(1)),
                "focused TextBox border must stay a uniform 1px");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
