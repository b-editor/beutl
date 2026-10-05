using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Layout regression for the command-palette result item template. The command title is the primary
// identifier and must never be starved: it now sits on its own full-width row, with the compact,
// width-bounded category label and the description each on their own line below it. Earlier layouts
// either shared a single horizontal row between the category and the description (crowding the
// category) or put the bounded category beside the title (starving the title at narrow widths).
// These tests build the real ListBox.ItemTemplate against a CommandPaletteItemViewModel and assert the
// arranged layout (bounds only, never pixels): the title keeps a substantial width on a narrow window,
// the category and description each sit on their own line, and the category label is width-bounded.
[TestFixture]
public class CommandPaletteViewLayoutTests
{
    private const double CategoryMaxWidth = 120;

    private const string TitleText = "Test Command";
    private const string CategoryText = "Very Long Category Name That Should Be Bounded";
    private const string DescriptionText =
        "This is a very long command description that would previously be cramped next to the category label on a narrow window";

    private static (Control Built, Window ViewWindow, Window HostWindow) BuildItem(double hostWidth)
    {
        // Inflate the real view so we can lift its ListBox.ItemTemplate out, then materialize it for a
        // single item. A null DataContext leaves the FilteredCommands binding unset (no items), but the
        // ItemTemplate object is still present on the ListBox.
        var view = new CommandPaletteView();
        var window = new Window { Content = view, Width = 480, Height = 480 };
        Window? host = null;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            ListBox? listBox = HeadlessTestHelpers.FindDescendant<ListBox>(view);
            Assert.That(listBox, Is.Not.Null, "CommandPaletteView should inflate its results ListBox");
            IDataTemplate? template = listBox!.ItemTemplate;
            Assert.That(template, Is.Not.Null, "results ListBox should declare an ItemTemplate");

            var command = new PaletteCommand(
                Id: "test.command",
                DisplayName: TitleText,
                Description: DescriptionText,
                CategoryName: CategoryText,
                KeyGesture: new KeyGesture(Key.P, KeyModifiers.Control),
                CanExecute: () => true,
                ExecuteAsync: () => Task.CompletedTask);
            var item = new CommandPaletteItemViewModel(command, isEnabled: true, relevance: 0);

            Control? built = template!.Build(item);
            Assert.That(built, Is.Not.Null, "the ItemTemplate should build a control for the item");
            built!.DataContext = item;

            host = new Window { Content = built, Width = hostWidth, Height = 200 };
            host.Show();
            HeadlessTestHelpers.Render();
            return (built, window, host);
        }
        catch
        {
            // The shared headless Application does not auto-close windows; close them on failure too.
            host?.Close();
            window.Close();
            HeadlessTestHelpers.Settle();
            throw;
        }
    }

    private static TextBlock FindTextBlock(Control root, Func<string, bool> predicate)
    {
        TextBlock? match = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.Text is { } text && predicate(text));
        Assert.That(match, Is.Not.Null, "expected a matching TextBlock in the item template");
        return match!;
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(900, false)]
    [TestCase(320, true)]
    [TestCase(900, true)]
    public async Task Command_search_starts_below_the_input_without_prompt_space(int width, bool light)
    {
        var extension = new SearchExtension();
        var provider = new ExtensionProvider();
        provider.AddExtensions(-45009, [extension]);
        var editor = new EditorService(provider);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(extension);
        var service = new CommandPaletteService(manager, new SearchHandlerProvider(), () => null, editor, provider);
        using var palette = new CommandPaletteViewModel(service, editor);
        var view = new CommandPaletteView { DataContext = palette };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 640,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        window.Show();
        try
        {
            palette.Open();
            HeadlessTestHelpers.Render();
            TextBox query = view.FindControl<TextBox>("QueryTextBox")!;
            ListBox results = view.FindControl<ListBox>("ResultsListBox")!;
            ListBoxItem first = results.GetVisualDescendants().OfType<ListBoxItem>().First();
            double queryBottom = query.TranslatePoint(new Point(0, query.Bounds.Height), view)!.Value.Y;
            double firstTop = first.TranslatePoint(default, view)!.Value.Y;
            Capture(window, $"search-{width}-{light}");

            Assert.That(firstTop - queryBottom, Is.InRange(0d, 16d),
                "The command list must follow the input without reserving blank prompt messages.");
            Assert.That(results.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "."), Is.True);

            // Returning from an interactive step must not leave its message area behind.
            Task operation = palette.ExecuteSelectedAsync();
            HeadlessTestHelpers.Render();
            palette.Close();
            await operation;
            palette.Open();
            HeadlessTestHelpers.Render();
            first = results.GetVisualDescendants().OfType<ListBoxItem>().First();
            queryBottom = query.TranslatePoint(new Point(0, query.Bounds.Height), view)!.Value.Y;
            firstTop = first.TranslatePoint(default, view)!.Value.Y;
            Assert.That(firstTop - queryBottom, Is.InRange(0d, 16d));
        }
        finally
        {
            palette.Close();
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    [TestCase(Key.OemPeriod, KeyModifiers.None, ".")]
    [TestCase(Key.OemComma, KeyModifiers.None, ",")]
    [TestCase(Key.OemPeriod, KeyModifiers.Alt, "Alt+.")]
    [TestCase(Key.OemComma, KeyModifiers.Shift, "Shift+,")]
    [TestCase(Key.OemQuestion, KeyModifiers.Control, "Ctrl+/")]
    [TestCase(Key.D1, KeyModifiers.Control, "Ctrl+1")]
    [TestCase(Key.OemComma, KeyModifiers.Meta | KeyModifiers.Shift, "Shift+Cmd+,")]
    public void Shortcut_labels_show_keys_instead_of_enum_names(Key key, KeyModifiers modifiers, string expected)
    {
        Assert.That(CreateItem(new KeyGesture(key, modifiers)).KeyGestureText, Is.EqualTo(expected));
        Assert.That(CreateItem(null).KeyGestureText, Is.Null);
    }

    private static CommandPaletteItemViewModel CreateItem(KeyGesture? gesture)
        => new(new PaletteCommand("test", "Test", null, "Test", gesture, () => true, () => Task.CompletedTask), true, 0);

    private static void Capture(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("BEUTL_COMMAND_PALETTE_LAYOUT_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        HeadlessTestHelpers.Render();
        using var image = window.CaptureRenderedFrame();
        Assert.That(image, Is.Not.Null);
        image!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class SearchExtension : ViewExtension
    {
        public override string DisplayName => "タイムライン";
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new("MoveRight", "1 フレーム右へ移動", keyGestures: [new("OemPeriod")]),
            new("MoveLeft", "1 フレーム左へ移動", keyGestures: [new("OemComma")]),
            new("MoveSecond", "1 秒右へ移動", keyGestures: [new("Alt+OemPeriod")]),
            new("MoveTen", "10 フレーム左へ移動", keyGestures: [new("Shift+OemComma")])
        ];
    }

    private sealed class SearchHandlerProvider : ICommandPaletteHandlerProvider, IContextCommandHandler
    {
        public IContextCommandHandler? Resolve(Type extensionType) => this;

        public async Task ExecuteAsync(ContextCommandExecution execution)
        {
            await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions { Prompt = "Prompt message" });
        }
    }

    [AvaloniaTest]
    public void Description_is_on_its_own_line_below_the_category()
    {
        (Control built, Window viewWindow, Window hostWindow) = BuildItem(hostWidth: 320);
        try
        {
            TextBlock category = FindTextBlock(built, t => t == CategoryText);
            TextBlock description = FindTextBlock(built, t => t.StartsWith("This is a very long"));

            double categoryY = category.TranslatePoint(new Point(0, 0), built)!.Value.Y;
            double descriptionY = description.TranslatePoint(new Point(0, 0), built)!.Value.Y;

            Assert.That(
                descriptionY,
                Is.GreaterThan(categoryY + 1),
                "the description should wrap to its own line below the category, not sit beside it");
        }
        finally
        {
            hostWindow.Close();
            viewWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Category_label_is_width_bounded()
    {
        (Control built, Window viewWindow, Window hostWindow) = BuildItem(hostWidth: 320);
        try
        {
            TextBlock category = FindTextBlock(built, t => t == CategoryText);

            Assert.That(
                category.Bounds.Width,
                Is.LessThanOrEqualTo(CategoryMaxWidth + 0.5),
                "the category label should be a compact, width-bounded label even for long category names");
        }
        finally
        {
            hostWindow.Close();
            viewWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Title_is_not_starved_at_narrow_width()
    {
        (Control built, Window viewWindow, Window hostWindow) = BuildItem(hostWidth: 320);
        try
        {
            TextBlock title = FindTextBlock(built, t => t == TitleText);
            TextBlock category = FindTextBlock(built, t => t == CategoryText);

            // On its own full-width row the title spans most of the content column; sharing a row with
            // the bounded ~120px category would leave it far narrower than this threshold.
            Assert.That(
                title.Bounds.Width,
                Is.GreaterThan(CategoryMaxWidth + 60),
                "the command title must keep a substantial, full-row width and not be starved by the category at narrow widths");

            double titleY = title.TranslatePoint(new Point(0, 0), built)!.Value.Y;
            double categoryY = category.TranslatePoint(new Point(0, 0), built)!.Value.Y;

            Assert.That(
                categoryY,
                Is.GreaterThan(titleY + 1),
                "the category should sit on its own line below the title, not share the title's row");
        }
        finally
        {
            hostWindow.Close();
            viewWindow.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
