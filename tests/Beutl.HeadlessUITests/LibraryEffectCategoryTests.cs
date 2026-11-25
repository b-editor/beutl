using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryEffectCategoryTests
{
    [AvaloniaTest]
    public void The_gallery_shows_categories_from_the_library_and_keeps_ungrouped_effects()
    {
        using var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        var gallery = new LibraryGalleryView { DataContext = viewModel };
        var window = new Window { Content = gallery, Width = 480, Height = 640 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            string?[] headings = gallery.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsVisible && text.Classes.Contains("effectCategoryHeading"))
                .Select(text => text.Text)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(headings, Is.EqualTo(viewModel.EffectSections.Select(section => section.Title)));
                Assert.That(headings.Take(2), Is.EqualTo(new[]
                {
                    GraphicsStrings.FilterEffectCategory_BlurAndSharpen,
                    GraphicsStrings.FilterEffectCategory_ColorCorrection,
                }));
                Assert.That(viewModel.EffectSections.SelectMany(section => section.Items).Select(tile => tile.Type),
                    Is.EquivalentTo(library.GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect)));
            });

            library.Register<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect, "Extension effect");
            Dispatcher.UIThread.RunJobs();
            var others = viewModel.EffectSections.Last();
            Assert.Multiple(() =>
            {
                Assert.That(others.Title, Is.EqualTo(Strings.Others));
                Assert.That(others.Items.Single().Type, Is.EqualTo(typeof(FilterEffectGroup)));
                Assert.That(gallery.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                    text.Classes.Contains("effectCategoryHeading") && text.Text == Strings.Others), Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_filter_effect_picker_accepts_a_category_and_search_from_the_keyboard()
    {
        using var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        var viewModel = new SelectLibraryItemDialogViewModel(TestShell.FontManager, library,
            KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        viewModel.InitializeReferences([new TargetObjectInfo("Existing blur", new Blur())]);
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 540, Height = 640 };
        try
        {
            window.Show();
            Task<object?> picked = LibraryItemPickerHelper.ShowAsync(host, viewModel, KnownLibraryItemFormats.FilterEffect);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            var presenter = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
            var category = presenter.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "CategoryPicker");

            Assert.That(category.SelectedItem, Is.EqualTo(Strings.AllEffectCategories));
            Press(Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
            Assert.That(category.IsFocused, Is.True);
            Press(Key.Enter, PhysicalKey.Enter);
            Assert.That(category.IsDropDownOpen, Is.True);
            Press(Key.Escape, PhysicalKey.Escape);
            Assert.That(category.IsDropDownOpen, Is.False);
            Assert.That(category.IsFocused, Is.True);
            Assert.That(picked.IsCompleted, Is.False);
            Press(Key.Down, PhysicalKey.ArrowDown);
            await viewModel.SearchTask;
            Assert.Multiple(() =>
            {
                Assert.That(category.SelectedItem, Is.EqualTo(GraphicsStrings.FilterEffectCategory_BlurAndSharpen));
                Assert.That(viewModel.Items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType),
                    Is.EquivalentTo(new[] { typeof(Blur), typeof(Sharpen), typeof(RadialBlur), typeof(MotionBlur), typeof(Bokeh) }));
            });
            HeadlessCapture.SaveIfRequested(window, nameof(LibraryEffectCategoryTests), "filtered-effect-picker");

            Press(Key.Tab, PhysicalKey.Tab);
            Press(Key.F, PhysicalKey.F, RawInputModifiers.Control);
            window.KeyTextInput(GraphicsStrings.RadialBlur);
            Press(Key.Down, PhysicalKey.ArrowDown);
            Press(Key.Enter, PhysicalKey.Enter);
            Assert.That(await picked.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(typeof(RadialBlur)));
        }
        finally
        {
            window.Close();
        }

        void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, physical, null);
            window.KeyRelease(key, modifiers, physical, null);
            HeadlessTestHelpers.Render();
        }
    }

    [AvaloniaTest]
    public async Task The_audio_effect_picker_filters_the_audio_catalog()
    {
        using var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        var viewModel = new SelectLibraryItemDialogViewModel(TestShell.FontManager, library,
            KnownLibraryItemFormats.AudioEffect, typeof(AudioEffect));
        await viewModel.SearchTask;

        int dynamics = viewModel.Categories.ToList().IndexOf(AudioStrings.AudioEffectCategory_Dynamics);
        Assert.That(dynamics, Is.GreaterThan(0));
        viewModel.SelectedCategoryIndex.Value = dynamics;
        await viewModel.SearchTask;

        Assert.That(viewModel.Items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType),
            Is.EquivalentTo(new[] { typeof(CompressorEffect), typeof(LimiterEffect), typeof(GateEffect) }));
    }
}
