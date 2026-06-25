using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

// The ColorEditor's only input gesture opens a SimpleColorPickerFlyout, which does not open
// deterministically headless, so the value-changed/confirmed event paths are not reachable here;
// these tests only cover that the template applies and the Value DirectProperty round-trips.
[TestFixture]
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
    public void Value_assignment_round_trips_through_the_direct_property()
    {
        var editor = new ColorEditor { Header = "Color" };
        _ = new EditorTestHost<ColorEditor>(editor);

        var red = Color.FromArgb(255, 200, 10, 20);
        editor.Value = red;
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Value, Is.EqualTo(red));
    }
}
