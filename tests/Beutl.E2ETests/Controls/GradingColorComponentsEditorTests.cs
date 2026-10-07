using Avalonia;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class GradingColorComponentsEditorTests
{
    [AvaloniaTest]
    public void Rgb_is_an_avalonia_property_of_the_grading_editor_itself()
    {
        var editor = new GradingColorComponentsEditor();

        Assert.That(AvaloniaPropertyRegistry.Instance.FindRegistered(editor, nameof(GradingColorComponentsEditor.Rgb)),
            Is.SameAs(GradingColorComponentsEditor.RgbProperty));
        Assert.That(AvaloniaPropertyRegistry.Instance.GetRegistered(typeof(ColorComponentsEditor))
                .Count(property => property.Name == nameof(ColorComponentsEditor.Rgb)),
            Is.EqualTo(1));

        editor.SetValue(GradingColorComponentsEditor.RgbProperty, false);

        Assert.That(editor.Rgb, Is.False);
        Assert.That(editor.GetValue(GradingColorComponentsEditor.RgbProperty), Is.False);
    }
}
