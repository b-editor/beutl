using Avalonia.Controls;
using Avalonia.Headless.NUnit;

using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.HeadlessUITests;

// Reads the logical content tree rather than showing the dialog: a ContentDialog not opened via
// ShowAsync keeps its content collapsed (close animation sets LayoutRoot IsVisible=False), so the
// Carousel pages are never realized in the visual tree. The unit suffixes are static XAML
// InnerRightContent labels, which exist in the logical tree at construction without any rendering.
[TestFixture]
public class CreateNewProjectDialogTests
{
    [AvaloniaTest]
    public void NumericInputs_show_unit_suffixes()
    {
        var vm = new CreateNewProjectViewModel(new ProjectService());
        var dialog = new CreateNewProject { DataContext = vm };

        var carousel = dialog.Content as Carousel;
        Assert.That(carousel, Is.Not.Null, "dialog should host the wizard Carousel as its content");

        // Page 0 is Name/Location; page 1 hosts the Size/FrameRate/SampleRate numeric inputs.
        var numericPage = carousel!.Items[1] as Panel;
        Assert.That(numericPage, Is.Not.Null, "the second Carousel page should host the numeric inputs");

        List<string?> units = numericPage!.Children.OfType<TextBox>()
            .Select(tb => (tb.InnerRightContent as TextBlock)?.Text)
            .ToList();

        Assert.That(units, Is.EqualTo(new[] { "px", "fps", "Hz" }),
            "Size, FrameRate and SampleRate inputs should carry their unit suffixes in order");
    }
}
