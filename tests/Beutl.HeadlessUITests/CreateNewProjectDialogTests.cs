using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

// Inflates the real CreateNewProject dialog (src/Beutl) headlessly to characterize the unit-suffix
// hints on its numeric inputs. The dialog's Carousel virtualizes, so the numeric page is realized
// only after selecting it; the page transition is cleared to realize it deterministically.
[TestFixture]
public class CreateNewProjectDialogTests
{
    [AvaloniaTest]
    public void NumericInputs_show_unit_suffixes()
    {
        var vm = new CreateNewProjectViewModel(new ProjectService());
        var dialog = new CreateNewProject { DataContext = vm };
        var window = new Window { Content = dialog, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Carousel carousel = HeadlessTestHelpers.FindDescendant<Carousel>(dialog)!;
            Assert.That(carousel, Is.Not.Null, "dialog should host the wizard Carousel");

            carousel.PageTransition = null;
            carousel.SelectedIndex = 1;
            HeadlessTestHelpers.Render();

            List<string> units = CollectDescendants<TextBox>(dialog)
                .Select(tb => (tb.InnerRightContent as TextBlock)?.Text)
                .Where(text => text is not null)
                .ToList()!;

            Assert.That(units, Is.EqualTo(new[] { "px", "fps", "Hz" }),
                "Size, FrameRate and SampleRate inputs should carry their unit suffixes in order");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static IEnumerable<T> CollectDescendants<T>(Avalonia.Visual root)
        where T : Avalonia.Visual
    {
        foreach (Avalonia.Visual child in root.GetVisualChildren())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in CollectDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
