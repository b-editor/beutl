using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Graphics.Shapes;
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
    // A window showing a new scene's editor with one rectangle clip, in the dark theme.
    private sealed class ElementScope : IDisposable
    {
        private readonly ThemeVariantScope _theme;

        private ElementScope(ThemeVariantScope theme, Window window, ElementView element)
        {
            _theme = theme;
            Window = window;
            Element = element;
        }

        public Window Window { get; }

        public ElementView Element { get; }

        public static async Task<ElementScope> CreateAsync(string name)
        {
            ThemeVariantScope theme = ThemeVariantScope.Use(ThemeVariant.Dark);
            try
            {
                (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
                await TestShell.AddElementAsync(editor, () => new RectShape(), TimeSpan.Zero, TimeSpan.FromSeconds(2));

                var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 800 };
                window.Show();
                HeadlessTestHelpers.Render(5);

                ElementView? element = window.GetVisualDescendants().OfType<ElementView>().FirstOrDefault();
                Assert.That(element, Is.Not.Null, "The timeline should inflate an ElementView for the added clip.");
                return new ElementScope(theme, window, element!);
            }
            catch
            {
                theme.Dispose();
                throw;
            }
        }

        public Control Part(string name) => Element.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);

        public void Dispose()
        {
            Window.Close();
            HeadlessTestHelpers.Settle();
            _theme.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Label_is_inset_clear_of_the_rounded_corner()
    {
        using ElementScope scope = await ElementScope.CreateAsync("elementview-inset");
        double labelLeftInset = scope.Part("textBlock").TranslatePoint(new Point(0, 0), scope.Part("border"))!.Value.X;

        // A 6px margin on top of the 1px border stroke clears the 4px corner arc.
        Assert.That(labelLeftInset, Is.EqualTo(7).Within(0.5),
            $"Label left inset was {labelLeftInset}; expected 7 (1px border stroke + 6px margin).");
    }

    [AvaloniaTest]
    public async Task Locked_clip_keeps_the_label_clear_of_the_lock_glyph()
    {
        using ElementScope scope = await ElementScope.CreateAsync("elementview-locked");
        var viewModel = (ElementViewModel)scope.Element.DataContext!;
        viewModel.Model.IsLocked = true;
        HeadlessTestHelpers.Render(5);
        Assert.That(viewModel.IsEditable.Value, Is.False, "Locking the element must make it non-editable.");

        Control lockIcon = scope.Part("lockIcon");
        Control label = scope.Part("textBlock");
        Assert.That(lockIcon.IsVisible, Is.True, "A locked clip must show the lock glyph.");

        double glyphRight = lockIcon.TranslatePoint(new Point(lockIcon.Bounds.Width, 0), scope.Element)!.Value.X;
        double labelLeft = label.TranslatePoint(new Point(0, 0), scope.Element)!.Value.X;

        // Asserted against the glyph's measured width rather than a reserved constant, so growing
        // the glyph's FontSize fails here instead of silently overlapping the name.
        Assert.That(labelLeft, Is.GreaterThanOrEqualTo(glyphRight),
            $"The name overlaps the lock glyph: glyph ends at {glyphRight}, label starts at {labelLeft}.");
    }

    [AvaloniaTest]
    public async Task Rename_textbox_text_starts_where_the_label_does()
    {
        using ElementScope scope = await ElementScope.CreateAsync("elementview-rename");
        Control border = scope.Part("border");
        var textBox = (TextBox)scope.Part("textBox");

        // Read the label first: entering rename hides it, and an unarranged control has no
        // position to compare against.
        double labelInset = scope.Part("textBlock").TranslatePoint(new Point(0, 0), border)!.Value.X;

        // Focus-time styling changes the editor's border, so an unfocused probe would miss a
        // shift that renaming actually shows.
        ((ElementViewModel)scope.Element.DataContext!).RenameRequested();
        HeadlessTestHelpers.Render(5);
        Assert.That(textBox.IsVisible && textBox.IsFocused, Is.True,
            "Rename did not put the focused editor on screen, so this comparison would prove nothing.");

        TextPresenter presenter = textBox.GetVisualDescendants().OfType<TextPresenter>().First();
        double editInset = presenter.TranslatePoint(new Point(0, 0), border)!.Value.X;

        Assert.That(editInset, Is.EqualTo(labelInset).Within(0.5),
            $"Renaming must not shift the name horizontally: label at {labelInset}px, edit text at "
            + $"{editInset}px (off by {editInset - labelInset}px). Check ElementNameTextBoxTheme's "
            + "Padding against the label's Margin.");
    }

    [AvaloniaTest]
    public async Task Media_host_rounds_and_clips_the_thumbnail_and_waveform()
    {
        using ElementScope scope = await ElementScope.CreateAsync("elementview-rounded");

        // Filmstrips and waveforms need async decode, so the rounding is asserted structurally: the element
        // keeps the 4px timeline radius, and its media host rounds to 3px and clips.
        var outer = (Border)scope.Part("border");
        var mediaClip = (Border)scope.Part("mediaClip");
        Assert.Multiple(() =>
        {
            Assert.That(outer.CornerRadius, Is.EqualTo(new CornerRadius(4)),
                "Elements must keep the timeline's tighter rounding.");
            Assert.That(outer.ClipToBounds, Is.True,
                "The clip's Border must clip its children, or renaming paints the focus accent into the corner.");
            Assert.That(mediaClip.CornerRadius, Is.EqualTo(new CornerRadius(3)),
                "Media host must round to the clip's inner radius.");
            Assert.That(mediaClip.ClipToBounds, Is.True,
                "Media host must clip its children to the rounded rect.");
        });

        Control thumbnailStrip = scope.Part("thumbnailStrip");
        Control waveform = scope.Part("waveformControl");
        Assert.Multiple(() =>
        {
            Assert.That(thumbnailStrip.GetVisualAncestors(), Does.Contain(mediaClip),
                "The thumbnail strip must sit inside the rounded media host.");
            Assert.That(waveform.GetVisualAncestors(), Does.Contain(mediaClip),
                "The waveform must sit inside the rounded media host.");
        });
    }
}
