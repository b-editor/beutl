using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

// The ColorEditor's only input gesture opens a SimpleColorPickerFlyout, which does not open
// deterministically headless; the reachable assertion is the template part plus the Value round-trip.
public class ColorEditorTests
{
    [AvaloniaTest]
    public void Template_applies_and_exposes_the_picker_button()
    {
        var editor = new ColorEditor { Header = "Color" };
        var host = new EditorTestHost<ColorEditor>(editor);

        Button button = host.Require<Button>("PART_ColorPickerButton");
        Assert.That(button, Is.Not.Null);
    }

    [AvaloniaTest]
    public void Value_round_trips_through_the_direct_property()
    {
        var editor = new ColorEditor { Header = "Color" };
        var host = new EditorTestHost<ColorEditor>(editor);

        var changed = new List<Color>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<Color>)e).NewValue);

        var red = Color.FromArgb(255, 200, 10, 20);
        editor.Value = red;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.That(editor.Value, Is.EqualTo(red));
    }
}
