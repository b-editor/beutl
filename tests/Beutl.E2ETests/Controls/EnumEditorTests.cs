using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class EnumEditorTests
{
    private static EnumEditor CreateEditor()
    {
        return new EnumEditor
        {
            Header = "Mode",
            Items =
            [
                new EnumItem("First", "first", 0),
                new EnumItem("Second", "second", 1),
                new EnumItem("Third", "third", 2),
            ],
        };
    }

    [AvaloniaTest]
    public void Selecting_an_item_in_the_combobox_updates_selected_index_and_confirms()
    {
        EnumEditor editor = CreateEditor();
        var host = new EditorTestHost<EnumEditor>(editor);

        var confirmed = new List<int>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<int>)e).NewValue);

        ComboBox combo = host.Require<ComboBox>("PART_InnerComboBox");
        combo.SelectedIndex = 2;
        HeadlessTestHelpers.Settle();

        Assert.That(editor.SelectedIndex, Is.EqualTo(2));
        Assert.That(confirmed, Is.EqualTo(new[] { 2 }));
    }

    [AvaloniaTest]
    public void Setting_selected_index_on_the_editor_reflects_in_the_combobox()
    {
        EnumEditor editor = CreateEditor();
        var host = new EditorTestHost<EnumEditor>(editor);

        editor.SelectedIndex = 1;
        HeadlessTestHelpers.Settle();

        ComboBox combo = host.Require<ComboBox>("PART_InnerComboBox");
        Assert.That(combo.SelectedIndex, Is.EqualTo(1));
    }
}
