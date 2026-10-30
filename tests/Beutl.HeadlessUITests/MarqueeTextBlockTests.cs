using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class MarqueeTextBlockTests
{
    private const string LongName = "A library item whose name runs far past the width of the picker";

    // The scrolling text is redrawn from the elapsed time, so two frames apart differ only while it moves.
    [AvaloniaTest]
    public void Overflowing_text_scrolls_while_active_and_returns_to_its_trimmed_look_when_it_stops()
    {
        var text = new MarqueeTextBlock
        {
            Text = LongName,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            MarqueeDelay = TimeSpan.Zero
        };
        var window = new Window { Content = text, Width = 200, Height = 40 };
        try
        {
            window.Show();
            byte[] trimmed = Capture(window);

            text.IsMarqueeActive = true;
            byte[] first = Capture(window);
            Thread.Sleep(300);
            byte[] later = Capture(window);
            Assert.That(later, Is.Not.EqualTo(first));

            text.IsMarqueeActive = false;
            Assert.That(Capture(window), Is.EqualTo(trimmed));
        }
        finally
        {
            window.Close();
        }
    }

    // A recycled picker row gets another name while it scrolls; the new name starts with the pause, which keeps
    // its trimmed look. The pause draws nothing new, so the frames only keep coming while the dispatcher loop runs
    // its timers, as it does in the app; Thread.Sleep would stop them.
    [AvaloniaTest]
    public async Task A_new_text_starts_its_pass_from_the_beginning()
    {
        var text = new MarqueeTextBlock
        {
            Text = LongName,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var window = new Window { Content = text, Width = 200, Height = 40 };
        try
        {
            window.Show();
            text.IsMarqueeActive = true;
            Capture(window);
            await Task.Delay(text.MarqueeDelay + TimeSpan.FromMilliseconds(500));
            Capture(window);

            text.Text = "Another library item whose name is also too long for the row";
            byte[] changed = Capture(window);

            text.IsMarqueeActive = false;
            Assert.That(changed, Is.EqualTo(Capture(window)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Text_that_fits_stays_still_while_active()
    {
        var text = new MarqueeTextBlock
        {
            Text = "Blur",
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            MarqueeDelay = TimeSpan.Zero
        };
        var window = new Window { Content = text, Width = 200, Height = 40 };
        try
        {
            window.Show();
            byte[] rest = Capture(window);

            text.IsMarqueeActive = true;
            Capture(window);
            Thread.Sleep(300);

            Assert.That(Capture(window), Is.EqualTo(rest));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_picker_scrolls_the_names_of_the_selected_and_hovered_items()
    {
        Assert.That(LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Drawable), Is.Not.Empty);
        PinnableLibraryItem[] items =
        [
            CreateItem(typeof(RectShape), LongName),
            CreateItem(typeof(EllipseShape), LongName + " too"),
            CreateItem(typeof(RoundedRectShape), "Short")
        ];
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 320, Height = 480 };
        var viewModel = new SelectLibraryItemDialogViewModel(
            KnownLibraryItemFormats.Drawable, typeof(Drawable), Task.FromResult(items), Task.FromResult(items));
        using var showAll = viewModel.ShowAll;
        using var searchText = viewModel.SearchText;
        var flyout = new LibraryItemPickerFlyout(viewModel);
        try
        {
            window.Show();
            flyout.ShowAt(host, true);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(2);
            var presenter = (LibraryItemPickerFlyoutPresenter)flyout.Popup.Child!;
            presenter.SelectedItem = viewModel.Items[0];
            HeadlessTestHelpers.Render(2);
            MarqueeTextBlock[] names = presenter.GetVisualDescendants().OfType<MarqueeTextBlock>().ToArray();
            Assert.That(names.Select(n => n.Text), Is.EqualTo(viewModel.Items.Select(i => i.DisplayName)));
            Assert.That(names.Select(n => n.IsMarqueeActive), Is.EqualTo(new[] { true, false, false }));

            window.MouseMove(HeadlessTestHelpers.Center(names[1], window));
            HeadlessTestHelpers.Render();

            Assert.That(names.Select(n => n.IsMarqueeActive), Is.EqualTo(new[] { true, true, false }));
        }
        finally
        {
            flyout.Hide();
            window.Close();
        }
    }

    private static byte[] Capture(Window window)
    {
        HeadlessTestHelpers.Render();
        return HeadlessTestHelpers.CaptureFramePixels(window).Pixels;
    }

    private static PinnableLibraryItem CreateItem(Type type, string displayName)
    {
        var item = new SingleTypeLibraryItem(KnownLibraryItemFormats.Drawable, type, displayName);
        return new PinnableLibraryItem(displayName, false, item);
    }
}
