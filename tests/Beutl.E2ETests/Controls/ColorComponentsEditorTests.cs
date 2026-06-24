using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

public class ColorComponentsEditorTests
{
    [AvaloniaTest]
    public void Typing_rgb_components_recomputes_the_color()
    {
        var editor = new ColorComponentsEditor { Header = "RGB", Rgb = true };
        var host = new EditorTestHost<ColorComponentsEditor>(editor);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "200");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "100");
        host.TypeInto(host.Require<TextBox>("PART_InnerThirdTextBox"), "50");

        Assert.That(editor.FirstValue, Is.EqualTo(200));
        Assert.That(editor.SecondValue, Is.EqualTo(100));
        Assert.That(editor.ThirdValue, Is.EqualTo(50));
        Assert.That(editor.Color.R, Is.EqualTo(200));
        Assert.That(editor.Color.G, Is.EqualTo(100));
        Assert.That(editor.Color.B, Is.EqualTo(50));
    }
}
