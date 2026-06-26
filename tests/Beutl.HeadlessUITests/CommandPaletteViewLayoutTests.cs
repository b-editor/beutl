using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
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
                Execute: () => { });
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
