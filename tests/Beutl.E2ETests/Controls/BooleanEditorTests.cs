using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class BooleanEditorTests
{
    [AvaloniaTest]
    public void Clicking_the_checkbox_flips_value_and_raises_events()
    {
        var editor = new BooleanEditor { Header = "Flag" };
        var host = new EditorTestHost<BooleanEditor>(editor);

        var changed = new List<bool>();
        var confirmed = new List<bool>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<bool>)e).NewValue);
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<bool>)e).NewValue);

        ToggleButton checkBox = host.Require<ToggleButton>("PART_CheckBox");
        Assert.That(editor.Value, Is.False);

        host.Click(checkBox);

        Assert.That(editor.Value, Is.True);
        Assert.That(changed, Is.EqualTo(new[] { true }));
        Assert.That(confirmed, Is.EqualTo(new[] { true }));

        host.Click(checkBox);

        Assert.That(editor.Value, Is.False);
        Assert.That(changed, Is.EqualTo(new[] { true, false }));
        Assert.That(confirmed, Is.EqualTo(new[] { true, false }));
    }

    [AvaloniaTest]
    public void Setting_value_programmatically_updates_the_checkbox()
    {
        var editor = new BooleanEditor { Header = "Flag" };
        var host = new EditorTestHost<BooleanEditor>(editor);

        ToggleButton checkBox = host.Require<ToggleButton>("PART_CheckBox");
        Assert.That(checkBox.IsChecked, Is.False);

        editor.Value = true;
        HeadlessTestHelpers.Settle();

        Assert.That(checkBox.IsChecked, Is.True);
    }
}
