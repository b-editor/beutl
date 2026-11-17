using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.HeadlessUITests;

public class ControlFontsTests
{
    [AvaloniaTest]
    public void DetachedFallbackYieldsToInheritedFontsWithoutChangingTheAuthoredValue()
    {
        var editor = new FontFamilyEditor();
        FontFamily original = editor.Value;
        FontManager detached = ControlFonts.GetOrCreate(editor);
        var configuration = new FontConfig();
        configuration.FontDirectories.Clear();
        var inherited = new FontManager(configuration);
        var window = new Window { Content = editor };
        ControlFonts.SetFonts(window, inherited);
        Assert.Multiple(() =>
        {
            Assert.That(ControlFonts.GetOrCreate(editor), Is.SameAs(inherited));
            Assert.That(detached, Is.Not.SameAs(inherited));
            Assert.That(editor.DataContext, Is.Null);
            Assert.That(editor.Value, Is.SameAs(original));
        });
        window.Content = null;
        Assert.That(ControlFonts.GetOrCreate(editor), Is.SameAs(detached));
    }
}
