using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// The font picker's own tests are in Beutl.E2ETests. This one needs the picker's typing pause to end on
// the dispatcher, which outlives a test only here: the E2E run starts a new dispatcher for every test, and
// ObserveOnUIDispatcher keeps posting to the first one.
[TestFixture]
public sealed class FontFamilyEditorTests
{
    [AvaloniaTest]
    public void A_search_still_waiting_when_the_picker_closes_does_not_change_the_font()
    {
        FontFamily target = FontManager.Instance.DefaultTypeface.FontFamily;
        FontFamily initial = FontManager.Instance.FontFamilies.First(family => family != target);
        var editor = new FontFamilyEditor { Header = "Font", Value = initial };
        var window = new Window { Width = 600, Height = 500, Content = editor };
        var previewed = new List<FontFamily>();
        editor.ValueChanged += (_, e) => previewed.Add(((PropertyEditorValueChangedEventArgs<FontFamily>)e).NewValue);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            DropDownButton button = editor.GetVisualDescendants().OfType<DropDownButton>()
                .Single(control => control.Name == "PART_InnerButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);

            window.KeyTextInput(target.Name);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            HeadlessTestHelpers.Settle();
            // The picker waits 100 ms for typing to pause; let that search land after the close.
            Thread.Sleep(300);
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Value, Is.EqualTo(initial));
                Assert.That(previewed, Is.Empty);
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
