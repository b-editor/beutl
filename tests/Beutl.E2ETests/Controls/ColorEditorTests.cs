using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

// The ColorEditor's only input gesture opens a SimpleColorPickerFlyout, which does not open
// deterministically headless, so the value-changed/confirmed event paths are not reachable here;
// this test only covers that the template applies.
[TestFixture]
public class ColorEditorTests
{
    [AvaloniaTest]
    public void Template_applies_and_exposes_the_picker_button()
    {
        var editor = new ColorEditor { Header = "Color" };
        using var host = new EditorTestHost<ColorEditor>(editor);

        host.Require<Button>("PART_ColorPickerButton");
    }
}
