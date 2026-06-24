using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests;

public class HeadlessHarnessTests
{
    [AvaloniaTest]
    public void Headless_session_lays_out_a_control()
    {
        var border = new Border { Width = 120, Height = 40 };
        var window = new Window { Content = border };
        window.Show();
        HeadlessTestHelpers.Settle();

        Assert.That(border.Bounds.Width, Is.EqualTo(120));
        Assert.That(border.Bounds.Height, Is.EqualTo(40));
    }

    [AvaloniaTest]
    public void BooleanEditor_applies_template_and_round_trips_value()
    {
        var editor = new BooleanEditor();
        var window = new Window { Content = editor };
        window.Show();
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Bounds.Width, Is.GreaterThan(0));

        CheckBox? toggle = HeadlessTestHelpers.FindDescendant<CheckBox>(editor);
        Assert.That(toggle, Is.Not.Null);

        editor.Value = true;
        HeadlessTestHelpers.Settle();
        Assert.That(editor.Value, Is.True);
    }
}
