using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class CompactToggleSwitchThemeTests
{
    // The compact theme is based on FluentAvalonia's, so its knob slides as FluentAvalonia's does.
    [AvaloniaTest]
    public void Compact_toggle_switch_takes_the_knob_transitions()
    {
        var toggle = new ToggleSwitch { Theme = (ControlTheme)Application.Current!.FindResource("CompactToggleSwitchStyle")! };
        var window = new Window { Content = toggle };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Assert.That(toggle.KnobTransitions, Is.Not.Null.And.Not.Empty);
        }
        finally
        {
            window.Close();
        }
    }
}
