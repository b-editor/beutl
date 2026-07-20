using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Locks the rounded timeline-clip layout: the name label is inset clear of the corner arc and of
// the lock glyph, renaming does not move it, and the thumbnail strip + waveform are wrapped in a
// rounded, clipped media host so their corners no longer poke past the clip's border.
[TestFixture]
public class ElementViewLayoutTests
{
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static void AddRectangle(EditViewModel editor)
    {
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        adder.AddElement(new ElementDescription(
            Start: TimeSpan.Zero,
            Length: TimeSpan.FromSeconds(2),
            Layer: 0,
            EngineObjectFactory: () => new RectShape()));
        HeadlessTestHelpers.Settle();
    }

    private static async Task<(Window Window, ElementView Element)> InflateFirstElementView(string name)
    {
        await TestReset.ResetShellAsync();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        EditViewModel editor = await OpenEditorForNewScene(name);
        AddRectangle(editor);

        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 800 };
        window.Show();
        HeadlessTestHelpers.Render(5);

        ElementView? element = window.GetVisualDescendants().OfType<ElementView>().FirstOrDefault();
        Assert.That(element, Is.Not.Null, "The timeline should inflate an ElementView for the added clip.");
        return (window, element!);
    }

    [AvaloniaTest]
    public async Task Label_is_inset_clear_of_the_rounded_corner()
    {
        (Window window, ElementView element) = await InflateFirstElementView("elementview-inset");
        try
        {
            Control border = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "border");
            Control label = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBlock");
            double labelLeftInset = label.TranslatePoint(new Point(0, 0), border)!.Value.X;

            // The 4px corner arc eats a flush-left label, which is where it sat before (1px, the border
            // stroke alone). A 6px margin on top of that stroke clears the arc.
            Assert.That(labelLeftInset, Is.EqualTo(7).Within(0.5),
                $"Label left inset was {labelLeftInset}; expected 7 (1px border stroke + 6px margin).");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Locked_clip_keeps_the_label_clear_of_the_lock_glyph()
    {
        (Window window, ElementView element) = await InflateFirstElementView("elementview-locked");
        try
        {
            var viewModel = (ElementViewModel)element.DataContext!;
            viewModel.Model.IsLocked = true;
            HeadlessTestHelpers.Render(5);
            Assert.That(viewModel.IsEditable.Value, Is.False, "Locking the element must make it non-editable.");

            Control lockIcon = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "lockIcon");
            Control label = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBlock");
            Assert.That(lockIcon.IsVisible, Is.True, "A locked clip must show the lock glyph.");

            double glyphRight = lockIcon.TranslatePoint(new Point(lockIcon.Bounds.Width, 0), element)!.Value.X;
            double labelLeft = label.TranslatePoint(new Point(0, 0), element)!.Value.X;

            // Asserted against the glyph's measured width rather than a reserved constant, so growing
            // the glyph's FontSize fails here instead of silently overlapping the name.
            Assert.That(labelLeft, Is.GreaterThanOrEqualTo(glyphRight),
                $"The name overlaps the lock glyph: glyph ends at {glyphRight}, label starts at {labelLeft}.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Rename_textbox_text_starts_where_the_label_does()
    {
        (Window window, ElementView element) = await InflateFirstElementView("elementview-rename");
        try
        {
            Control border = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "border");
            Control label = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBlock");
            var textBox = (TextBox)element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBox");

            textBox.IsVisible = true;
            HeadlessTestHelpers.Render(5);

            double labelInset = label.TranslatePoint(new Point(0, 0), border)!.Value.X;
            TextPresenter presenter = textBox.GetVisualDescendants().OfType<TextPresenter>().First();
            double editInset = presenter.TranslatePoint(new Point(0, 0), border)!.Value.X;

            Assert.That(editInset, Is.EqualTo(labelInset).Within(0.5),
                $"Renaming must not shift the name horizontally: label at {labelInset}px, edit text at "
                + $"{editInset}px (off by {editInset - labelInset}px). Check ElementNameTextBoxTheme's "
                + "Padding against the label's Margin.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Renaming_does_not_paint_into_the_rounded_corner()
    {
        (Window window, ElementView element) = await InflateFirstElementView("elementview-corner");
        try
        {
            Control border = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "border");
            var textBox = (TextBox)element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBox");
            Point origin = border.TranslatePoint(new Point(0, 0), window)!.Value;

            // The probe pixel straddles the clip's corner arc, so a correctly clipped repaint still
            // shifts it by antialiasing alone. An unclipped one fills it with the editor's focus
            // accent instead. Measured drift: 39 clipped, 133 unclipped.
            PixelPoint probe = ToPixel(window, origin + new Point(1, 0));
            uint atRest = SamplePixel(window, probe);

            // Drive the real rename entry point: a probe taken without focus would sample the
            // unfocused editor and pass even with the clip removed.
            ((ElementViewModel)element.DataContext!).RenameRequested();
            HeadlessTestHelpers.Render(5);
            Assert.That(textBox.IsVisible && textBox.IsFocused, Is.True,
                "Rename did not put the focused editor on screen, so this probe would prove nothing.");

            uint whileRenaming = SamplePixel(window, probe);

            int drift = MaxChannelDelta(atRest, whileRenaming);
            Assert.That(drift, Is.LessThan(64),
                $"Renaming repainted the rounded corner: {Rgba(atRest)} became {Rgba(whileRenaming)} "
                + $"(max channel drift {drift}). The clip's Border needs ClipToBounds to round its children.");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static PixelPoint ToPixel(Window window, Point dip)
    {
        double scale = window.RenderScaling;
        return new PixelPoint((int)Math.Round(dip.X * scale), (int)Math.Round(dip.Y * scale));
    }

    // Reads raw framebuffer memory, so every assumption it makes has to fail loudly: an
    // out-of-range index would read unrelated memory instead of throwing.
    private static uint SamplePixel(Window window, PixelPoint point)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("CaptureRenderedFrame returned null; the window never rendered.");
        using ILockedFramebuffer buffer = frame.Lock();
        Assert.That(buffer.Format, Is.EqualTo(PixelFormat.Rgba8888).Or.EqualTo(PixelFormat.Bgra8888),
            $"SamplePixel assumes a 32bpp framebuffer but the frame is {buffer.Format}.");
        Assert.That(point.X, Is.InRange(0, buffer.Size.Width - 1), "Probe X is outside the captured frame.");
        Assert.That(point.Y, Is.InRange(0, buffer.Size.Height - 1), "Probe Y is outside the captured frame.");

        unsafe
        {
            byte* row = (byte*)buffer.Address + (point.Y * buffer.RowBytes);
            return ((uint*)row)[point.X];
        }
    }

    // The framebuffer is Rgba8888, so a little-endian uint reads back as 0xAABBGGRR — printing it
    // as hex would name the channels in the wrong order.
    private static string Rgba(uint pixel)
        => $"rgba({pixel & 0xFF},{(pixel >> 8) & 0xFF},{(pixel >> 16) & 0xFF},{pixel >> 24})";

    private static int MaxChannelDelta(uint left, uint right)
    {
        int max = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            int delta = Math.Abs((int)((left >> shift) & 0xFF) - (int)((right >> shift) & 0xFF));
            max = Math.Max(max, delta);
        }

        return max;
    }

    [AvaloniaTest]
    public async Task Media_host_rounds_and_clips_the_thumbnail_and_waveform()
    {
        (Window window, ElementView element) = await InflateFirstElementView("elementview-rounded");
        try
        {
            // A real filmstrip/waveform never renders in this headless layout pass (both need async
            // media decode), so the rounding is asserted structurally: the wrapper Border rounds to
            // the clip's inner radius (3px = 4px outer - 1px stroke) and clips its children.
            var mediaClip = (Border)element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "mediaClip");
            Assert.Multiple(() =>
            {
                Assert.That(mediaClip.CornerRadius, Is.EqualTo(new CornerRadius(3)),
                    "Media host must round to the clip's inner radius.");
                Assert.That(mediaClip.ClipToBounds, Is.True,
                    "Media host must clip its children to the rounded rect.");
            });

            Control thumbnailStrip = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "thumbnailStrip");
            Control waveform = element.GetVisualDescendants().OfType<Control>().First(c => c.Name == "waveformControl");
            Assert.Multiple(() =>
            {
                Assert.That(thumbnailStrip.GetVisualAncestors(), Does.Contain(mediaClip),
                    "The thumbnail strip must sit inside the rounded media host.");
                Assert.That(waveform.GetVisualAncestors(), Does.Contain(mediaClip),
                    "The waveform must sit inside the rounded media host.");
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
