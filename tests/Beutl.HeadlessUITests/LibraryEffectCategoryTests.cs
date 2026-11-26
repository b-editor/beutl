using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Audio.Effects;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Editor.Components.LibraryThumbnails;
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
                Assert.That(viewModel.EffectSections.First().Key.Id, Is.EqualTo(KnownLibraryGroupIds.FilterEffectBlurAndSharpen));
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

    [AvaloniaTest, SetUICulture("en-US")]
    public void The_gallery_recognizes_built_in_categories_registered_in_another_language()
    {
        using var library = new LibraryService();
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        string registeredTitle;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            LibraryRegistrar.RegisterAll(library);
            registeredTitle = GraphicsStrings.FilterEffectCategory_BlurAndSharpen;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }

        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        LibraryGallerySection section = viewModel.EffectSections.Single(section =>
            section.Key.Id == KnownLibraryGroupIds.FilterEffectBlurAndSharpen);
        Assert.Multiple(() =>
        {
            Assert.That(registeredTitle, Is.Not.EqualTo(GraphicsStrings.FilterEffectCategory_BlurAndSharpen));
            Assert.That(section.Title, Is.EqualTo(registeredTitle));
            Assert.That(section.Items.Select(item => item.Type), Does.Contain(typeof(Blur)));
        });
    }

    [AvaloniaTest, SetUICulture("en-US")]
    [TestCase(false, TestName = "An_extension_without_ids_joins_the_built_in_groups_by_name")]
    [TestCase(true, TestName = "An_extension_with_built_in_ids_joins_groups_with_different_display_names")]
    public void An_extension_joins_built_in_effect_categories(bool useIds)
    {
        using var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        int initialSectionCount = viewModel.EffectSections.Count;

        if (useIds)
        {
            library.RegisterGroup("Extension's effect heading", null, KnownLibraryGroupIds.FilterEffect, root => root
                .AddGroup("Extension's blur heading", null, KnownLibraryGroupIds.FilterEffectBlurAndSharpen, group => group
                    .Add<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect, "Extension effect")));
        }
        else
        {
            library.RegisterGroup(GraphicsStrings.FilterEffect, root => root
                .AddGroup(GraphicsStrings.FilterEffectCategory_BlurAndSharpen, group => group
                    .Add<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect, "Extension effect")));
        }

        Dispatcher.UIThread.RunJobs();
        var root = library.Items.OfType<GroupLibraryItem>().Single(group => group.Id == KnownLibraryGroupIds.FilterEffect);
        var category = root.Items.OfType<GroupLibraryItem>().Single(group => group.Id == KnownLibraryGroupIds.FilterEffectBlurAndSharpen);
        LibraryGallerySection section = viewModel.EffectSections.Single(section =>
            section.Key.Id == KnownLibraryGroupIds.FilterEffectBlurAndSharpen);
        Assert.Multiple(() =>
        {
            Assert.That(category.Items.OfType<SingleTypeLibraryItem>().Select(item => item.ImplementationType),
                Does.Contain(typeof(FilterEffectGroup)));
            Assert.That(viewModel.EffectSections, Has.Count.EqualTo(initialSectionCount));
            Assert.That(section.Title, Is.EqualTo(GraphicsStrings.FilterEffectCategory_BlurAndSharpen));
            Assert.That(section.Items.Select(item => item.Type), Does.Contain(typeof(FilterEffectGroup)));
        });
    }

    [AvaloniaTest]
    public async Task Categories_with_different_ids_stay_separate_when_their_titles_match()
    {
        using var library = new LibraryService();
        const string title = "Adjustments";
        library.RegisterGroup(title, null, "Test.Blur", group => group.Add<Blur>(KnownLibraryItemFormats.FilterEffect, "Blur"));
        library.RegisterGroup(title, null, "Test.Sharpen", group => group.Add<Sharpen>(KnownLibraryItemFormats.FilterEffect, "Sharpen"));
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        LibraryGallerySection[] sections = [.. viewModel.EffectSections];
        library.Register<FilterEffectGroup>(KnownLibraryItemFormats.FilterEffect, "Extension effect");
        Dispatcher.UIThread.RunJobs();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EffectSections, Has.Count.EqualTo(3));
            Assert.That(viewModel.EffectSections[0], Is.SameAs(sections[0]));
            Assert.That(viewModel.EffectSections[1], Is.SameAs(sections[1]));
            Assert.That(sections[0].Items.Single().Type, Is.EqualTo(typeof(Blur)));
            Assert.That(sections[1].Items.Single().Type, Is.EqualTo(typeof(Sharpen)));
        });

        var picker = new SelectLibraryItemDialogViewModel(TestShell.FontManager, library,
            KnownLibraryItemFormats.FilterEffect, typeof(FilterEffect));
        try
        {
            await picker.SearchTask;
            Assert.That(picker.Categories, Is.EqualTo(new[] { Strings.AllEffectCategories, title, title, Strings.Others }));
            picker.SelectedCategoryIndex.Value = 2;
            await picker.SearchTask;
            Assert.That(((SingleTypeLibraryItem)picker.Items.Single().UserData).ImplementationType, Is.EqualTo(typeof(Sharpen)));
        }
        finally
        {
            picker.ReleaseThumbnails();
        }
    }

    [AvaloniaTest, SetUICulture("en-US")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_gallery_displays_a_description_only_when_one_is_registered(bool withDescription)
    {
        const string description = "Soften details, add motion blur, or sharpen edges.";
        using var library = new LibraryService();
        library.RegisterGroup(GraphicsStrings.FilterEffect, root => root
            .AddGroup(GraphicsStrings.FilterEffectCategory_BlurAndSharpen, withDescription ? description : null, group => group
                .Add<Blur>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.Blur)
                .Add<Sharpen>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.Sharpen)
                .Add<MotionBlur>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.MotionBlur))
            .AddGroup(GraphicsStrings.FilterEffectCategory_ColorCorrection, group => group
                .Add<Brightness>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.Brightness)
                .Add<Curves>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.Curves)
                .Add<Saturate>(KnownLibraryItemFormats.FilterEffect, GraphicsStrings.Saturate)));
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        var gallery = new LibraryGalleryView { DataContext = viewModel, Margin = new Avalonia.Thickness(12) };
        var window = new Window { Content = gallery, Width = 490, Height = 350 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            string?[] descriptions = gallery.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsVisible && text.Classes.Contains("effectCategoryDescription"))
                .Select(text => text.Text)
                .ToArray();
            Assert.That(descriptions, Is.EqualTo(withDescription ? new[] { description } : Array.Empty<string>()));

            if (HeadlessCapture.IsRequested)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                {
                    HeadlessTestHelpers.Render();
                    AnimatedThumbnail[] shown = gallery.GetVisualDescendants().OfType<AnimatedThumbnail>()
                        .Where(thumbnail => thumbnail.IsOnScreen && thumbnail.Source != null).ToArray();
                    return shown.Length > 0 && shown.All(thumbnail =>
                        thumbnail.Source!.Still != null || thumbnail.Source is LibraryThumbnailSource { Failed: true });
                });
                HeadlessCapture.SaveIfRequested(window, nameof(LibraryEffectCategoryTests),
                    withDescription ? "library-gallery-after" : "library-gallery-before");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest, SetUICulture("en-US")]
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
            window.KeyTextInput(nameof(RadialBlur));
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
    public async Task The_expanded_effect_picker_offers_other_and_returns_to_all_categories_when_collapsed()
    {
        using var library = new LibraryService();
        LibraryRegistrar.RegisterAll(library);
        const string format = KnownLibraryItemFormats.FilterEffect;
        var viewModel = new SelectLibraryItemDialogViewModel(TestShell.FontManager, library, format, typeof(FilterEffect));
        var host = new Button { Content = "Pick" };
        var window = new Window { Content = host, Width = 540, Height = 640 };
        var flyout = new LibraryItemPickerFlyout(viewModel);
        try
        {
            window.Show();
            flyout.ShowAt(host, true);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render(3);
            var presenter = (LibraryItemPickerFlyoutPresenter)flyout.Popup.Child!;
            var picker = presenter.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "CategoryPicker");
            Assert.That(picker.Items, Does.Not.Contain(Strings.Others));

            picker.SetCurrentValue(ComboBox.SelectedIndexProperty, 1);
            presenter.ShowAll = true;
            await viewModel.SearchTask.WaitAsync(TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render();
            Assert.That(picker.Items.Last(), Is.EqualTo(Strings.Others));
            Assert.That(picker.SelectedItem, Is.EqualTo(GraphicsStrings.FilterEffectCategory_BlurAndSharpen));

            picker.SetCurrentValue(ComboBox.SelectedIndexProperty, picker.Items.Count - 1);
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render();
            IReadOnlySet<Type> registered = library.GetTypesFromFormat(format);
            Type[] otherTypes = viewModel.Items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType).ToArray();
            Assert.That(otherTypes, Does.Contain(typeof(FilterEffectGroup)));
            Assert.That(otherTypes.Any(registered.Contains), Is.False);
            HeadlessCapture.SaveIfRequested(window, nameof(LibraryEffectCategoryTests), "expanded-effect-picker-other");

            presenter.ShowAll = false;
            await viewModel.SearchTask;
            HeadlessTestHelpers.Render();
            Assert.That(picker.Items, Does.Not.Contain(Strings.Others));
            Assert.That(picker.SelectedItem, Is.EqualTo(Strings.AllEffectCategories));
            Assert.That(viewModel.Items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType), Is.EquivalentTo(registered));
        }
        finally
        {
            flyout.Hide();
            window.Close();
            viewModel.ReleaseThumbnails();
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
