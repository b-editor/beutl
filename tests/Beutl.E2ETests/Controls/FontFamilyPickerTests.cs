using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using Beutl.Media;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class FontFamilyPickerTests
{
    private const string PinnedItemsKey = "FontManager.PinnedItems";

    private static readonly FontFamily[] s_families =
    [
        new("Noto Sans"),
        new("Noto Sans Arabic"),
        new("Noto Sans JP"),
        new("Noto Serif"),
        new("DejaVu Sans"),
        new("Sans"),
        new("Beta Mono"),
    ];

    private string? _pinnedItems;

    [SetUp]
    public void ClearPinnedFonts()
    {
        _pinnedItems = Preferences.Default.Get(PinnedItemsKey, "[]");
        Preferences.Default.Set(PinnedItemsKey, "[]");
    }

    [TearDown]
    public void RestorePinnedFonts()
    {
        Preferences.Default.Set(PinnedItemsKey, _pinnedItems ?? "[]");
    }

    [Test]
    public void A_search_lists_the_best_matches_first_and_highlights_the_first()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);

        viewModel.SearchText.Value = "sans";

        Assert.Multiple(() =>
        {
            Assert.That(Names(viewModel), Is.EqualTo(new[]
            {
                "Sans", "DejaVu Sans", "Noto Sans", "Noto Sans Arabic", "Noto Sans JP"
            }));
            Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Sans"));
        });
    }

    [Test]
    public void Pinned_fonts_come_first_but_a_search_puts_better_matches_above_them()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        viewModel.Pin(Item(viewModel, "Noto Sans JP"));

        Assert.That(Names(viewModel)[0], Is.EqualTo("Noto Sans JP"));

        viewModel.SearchText.Value = "noto sans";

        Assert.That(Names(viewModel), Is.EqualTo(new[] { "Noto Sans", "Noto Sans JP", "Noto Sans Arabic" }));
    }

    [Test]
    public void Clearing_a_search_that_matched_nothing_highlights_the_font_again()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");

        viewModel.SearchText.Value = "zzz";
        Assert.That(viewModel.SelectedItem.Value, Is.Null);

        viewModel.SearchText.Value = "";
        Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Noto Serif"));
    }

    [Test]
    public void Pinning_keeps_the_highlighted_font()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");

        viewModel.Pin(Item(viewModel, "Beta Mono"));

        Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Noto Serif"));
    }

    // The delayed search hands its result to the UI thread, so it needs the dispatcher's context.
    [AvaloniaTest]
    public void A_search_waits_for_typing_to_pause_unless_flushed()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.FromHours(1));

        viewModel.SearchText.Value = "beta";
        Assert.That(viewModel.Items, Has.Count.EqualTo(s_families.Length));

        viewModel.FlushSearch();
        Assert.Multiple(() =>
        {
            Assert.That(Names(viewModel), Is.EqualTo(new[] { "Beta Mono" }));
            Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Beta Mono"));
        });
    }

    [AvaloniaTest]
    public void Pinning_while_a_search_waits_highlights_the_best_match_of_the_search()
    {
        // Pinning rebuilds the list, which applies the query still waiting out the typing pause.
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.FromHours(1));
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");

        viewModel.SearchText.Value = "beta";
        viewModel.Pin(Item(viewModel, "Noto Sans"));

        Assert.Multiple(() =>
        {
            Assert.That(Names(viewModel), Is.EqualTo(new[] { "Beta Mono" }));
            Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Beta Mono"));
        });
    }

    [AvaloniaTest]
    public void The_search_box_has_focus_when_the_picker_opens()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");
        using var picker = PickerHost.Open(viewModel);

        Assert.Multiple(() =>
        {
            Assert.That(picker.SearchBox.IsEffectivelyVisible, Is.True);
            Assert.That(picker.FocusedElement, Is.SameAs(picker.SearchBox));
            Assert.That((picker.List.SelectedItem as PinnableLibraryItem)?.DisplayName, Is.EqualTo("Noto Serif"));
        });
    }

    [AvaloniaTest]
    public void Arrow_keys_in_the_search_box_move_the_highlight_and_keep_the_focus()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);

        picker.Type("noto");
        Assert.That(picker.HighlightedName, Is.EqualTo("Noto Sans"));

        picker.Press(Key.Down, PhysicalKey.ArrowDown);
        picker.Press(Key.Down, PhysicalKey.ArrowDown);
        picker.Press(Key.Up, PhysicalKey.ArrowUp);

        Assert.Multiple(() =>
        {
            Assert.That(picker.HighlightedName, Is.EqualTo("Noto Sans Arabic"));
            Assert.That(picker.FocusedElement, Is.SameAs(picker.SearchBox));
            Assert.That(picker.SearchBox.Text, Is.EqualTo("noto"));
        });
    }

    [AvaloniaTest]
    public void Enter_right_after_typing_confirms_the_best_match_for_the_typed_text()
    {
        // The search would wait for an hour; Enter must not act on the list from before the typing.
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.FromHours(1));
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");
        using var picker = PickerHost.Open(viewModel);
        bool confirmed = false;
        picker.Flyout.Confirmed += (_, _) => confirmed = true;

        picker.Type("beta");
        picker.Press(Key.Enter, PhysicalKey.Enter);

        Assert.Multiple(() =>
        {
            Assert.That(confirmed, Is.True);
            Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Beta Mono"));
            Assert.That(picker.Flyout.IsOpen, Is.False);
        });
    }

    [AvaloniaTest]
    public void The_checkmark_right_after_typing_confirms_the_best_match_for_the_typed_text()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.FromHours(1));
        viewModel.SelectedItem.Value = Item(viewModel, "Noto Serif");
        using var picker = PickerHost.Open(viewModel);
        bool confirmed = false;
        picker.Flyout.Confirmed += (_, _) => confirmed = true;

        picker.Type("beta");
        picker.Find<Button>("AcceptButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(confirmed, Is.True);
            Assert.That(viewModel.SelectedItem.Value?.DisplayName, Is.EqualTo("Beta Mono"));
            Assert.That(picker.Flyout.IsOpen, Is.False);
        });
    }

    [AvaloniaTest]
    public void A_search_without_matches_says_so()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        TextBlock message = picker.Find<TextBlock>("PART_NoResultsTextBlock");
        Assert.That(message.IsEffectivelyVisible, Is.False);

        picker.Type("zzz");
        Assert.Multiple(() =>
        {
            Assert.That(message.IsEffectivelyVisible, Is.True);
            Assert.That(message.Text, Is.EqualTo(Strings.FontFamilyPicker_NoResults));
        });

        picker.Presenter.SearchText = "";
        HeadlessTestHelpers.Render();
        Assert.That(message.IsEffectivelyVisible, Is.False);
    }

    [AvaloniaTest]
    public void Escape_in_the_search_box_cancels_the_picker()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        bool dismissed = false;
        picker.Flyout.Dismissed += (_, _) => dismissed = true;

        picker.Type("noto");
        picker.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Multiple(() =>
        {
            Assert.That(dismissed, Is.True);
            Assert.That(picker.Flyout.IsOpen, Is.False);
        });
    }

    [AvaloniaTest]
    public void Typing_while_the_list_has_focus_continues_in_the_search_box()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        picker.List.Focus();
        HeadlessTestHelpers.Render();

        picker.Type("b");
        picker.Type("eta");

        Assert.Multiple(() =>
        {
            Assert.That(picker.FocusedElement, Is.SameAs(picker.SearchBox));
            Assert.That(picker.SearchBox.Text, Is.EqualTo("beta"));
            Assert.That(Names(viewModel), Is.EqualTo(new[] { "Beta Mono" }));
        });
    }

    [AvaloniaTest]
    public void A_space_typed_on_the_list_separates_the_next_word_of_the_search()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        picker.Type("noto");
        picker.List.Focus();
        HeadlessTestHelpers.Render();

        picker.Type(" ");
        picker.Type("arabic");

        Assert.Multiple(() =>
        {
            Assert.That(picker.SearchBox.Text, Is.EqualTo("noto arabic"));
            Assert.That(Names(viewModel), Is.EqualTo(new[] { "Noto Sans Arabic" }));
        });
    }

    [AvaloniaTest]
    public void A_space_typed_on_the_list_without_a_search_stays_with_the_list()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        picker.List.Focus();
        HeadlessTestHelpers.Render();
        object? focused = picker.FocusedElement;

        picker.Type(" ");

        Assert.Multiple(() =>
        {
            Assert.That(picker.SearchBox.Text, Is.Null.Or.Empty);
            Assert.That(picker.FocusedElement, Is.SameAs(focused));
        });
    }

    [AvaloniaTest]
    public void Hiding_the_search_box_clears_the_search()
    {
        var viewModel = new FontFamilyPickerFlyoutViewModel(s_families, TimeSpan.Zero);
        using var picker = PickerHost.Open(viewModel);
        picker.Type("beta");

        picker.Presenter.ShowSearchBox = false;
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Presenter.SearchText, Is.Null.Or.Empty);
            Assert.That(viewModel.Items, Has.Count.EqualTo(s_families.Length));
        });
    }

    [AvaloniaTest]
    public void The_editor_confirms_the_font_found_by_name()
    {
        FontFamily target = FontManager.Instance.DefaultTypeface.FontFamily;
        FontFamily initial = FontManager.Instance.FontFamilies.First(family => family != target);
        var editor = new FontFamilyEditor { Header = "Font", Value = initial };
        using var host = new EditorTestHost<FontFamilyEditor>(editor, 600, 500);
        var confirmed = new List<FontFamily>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<FontFamily>)e).NewValue);
        DropDownButton button = host.Require<DropDownButton>("PART_InnerButton");

        OpenPicker(button);
        host.Window.KeyTextInput(target.Name);
        HeadlessTestHelpers.Settle();
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(editor.Value, Is.EqualTo(target));
            Assert.That(confirmed, Is.EqualTo(new[] { target }));
            Assert.That(button.Content, Is.EqualTo(FontFamilyNames.GetDisplayName(target)));
        });
    }

    [AvaloniaTest]
    public void Confirming_a_search_without_matches_keeps_the_previewed_font()
    {
        FontFamily initial = FontManager.Instance.FontFamilies.OrderBy(FontFamilyNames.GetDisplayName).First();
        var editor = new FontFamilyEditor { Header = "Font", Value = initial };
        using var host = new EditorTestHost<FontFamilyEditor>(editor, 600, 500);
        var previewed = new List<FontFamily>();
        var confirmed = new List<FontFamily>();
        editor.ValueChanged += (_, e) => previewed.Add(((PropertyEditorValueChangedEventArgs<FontFamily>)e).NewValue);
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<FontFamily>)e).NewValue);

        OpenPicker(host.Require<DropDownButton>("PART_InnerButton"));
        host.Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        HeadlessTestHelpers.Settle();
        Assume.That(previewed, Has.Count.EqualTo(1), "Needs a second installed font to preview.");
        host.Window.KeyTextInput("qqqqqqqqqqqqqqqqqqqq");
        HeadlessTestHelpers.Settle();
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(editor.Value, Is.EqualTo(previewed[0]));
            Assert.That(confirmed, Is.EqualTo(new[] { previewed[0] }));
        });
    }

    private static void OpenPicker(DropDownButton button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
    }

    private static string[] Names(FontFamilyPickerFlyoutViewModel viewModel)
    {
        return [.. viewModel.Items.Select(item => item.DisplayName)];
    }

    private static PinnableLibraryItem Item(FontFamilyPickerFlyoutViewModel viewModel, string name)
    {
        return viewModel.Items.Single(item => item.DisplayName == name);
    }

    private sealed class PickerHost : IDisposable
    {
        private PickerHost(Window window, FontFamilyPickerFlyout flyout)
        {
            Window = window;
            Flyout = flyout;
            Presenter = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
            SearchBox = Find<TextBox>("SearchTextBox");
            List = Find<ListBox>("PART_ListBox");
        }

        public Window Window { get; }

        public FontFamilyPickerFlyout Flyout { get; }

        public LibraryItemPickerFlyoutPresenter Presenter { get; }

        public TextBox SearchBox { get; }

        public ListBox List { get; }

        public object? FocusedElement => Window.FocusManager?.GetFocusedElement();

        public string? HighlightedName => (List.SelectedItem as PinnableLibraryItem)?.DisplayName;

        public static PickerHost Open(FontFamilyPickerFlyoutViewModel viewModel)
        {
            var anchor = new Button { Content = "Font" };
            var window = new Window { Width = 600, Height = 500, Content = anchor };
            window.Show();
            HeadlessTestHelpers.Render();
            var flyout = new FontFamilyPickerFlyout(viewModel);
            flyout.ShowAt(anchor);
            HeadlessTestHelpers.Render(3);
            return new PickerHost(window, flyout);
        }

        public T Find<T>(string name)
            where T : Control
        {
            return Presenter.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
        }

        public void Type(string text)
        {
            Window.KeyTextInput(text);
            HeadlessTestHelpers.Render();
        }

        public void Press(Key key, PhysicalKey physicalKey)
        {
            Window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Flyout.Hide();
            Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
