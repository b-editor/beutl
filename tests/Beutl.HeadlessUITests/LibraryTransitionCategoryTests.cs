using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LibraryTransitionCategoryTests
{
    private static readonly (string Id, Type[] Types)[] s_categories =
    [
        (KnownLibraryGroupIds.ClipTransitionFadesAndDissolves,
            [typeof(CrossDissolveTransition), typeof(FadeTransition), typeof(DipToColorTransition)]),
        (KnownLibraryGroupIds.ClipTransitionWipes,
            [typeof(WipeTransition), typeof(IrisTransition), typeof(SplitTransition), typeof(BlindsTransition), typeof(CheckerboardTransition)]),
        (KnownLibraryGroupIds.ClipTransitionMotion,
            [typeof(PushTransition), typeof(SlideTransition), typeof(ZoomTransition), typeof(ZoomBlurTransition), typeof(WhipPanTransition)]),
        (KnownLibraryGroupIds.ClipTransitionStylized,
            [typeof(GlitchTransition), typeof(LightLeakTransition)]),
    ];

    [AvaloniaTest, SetUICulture("en")]
    [TestCase("en")]
    [TestCase("ja")]
    [TestCase("es")]
    [TestCase("ko-KR")]
    [TestCase("zh-CN")]
    public async Task The_gallery_sections_use_the_same_ids_in_every_registration_language(string language)
    {
        using var library = new LibraryService();
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        string[] localizedTitles;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            LibraryRegistrar.RegisterAll(library);
            localizedTitles =
            [
                GraphicsStrings.ClipTransitionCategory_FadesAndDissolves,
                GraphicsStrings.ClipTransitionCategory_Wipes,
                GraphicsStrings.ClipTransitionCategory_Motion,
                GraphicsStrings.ClipTransitionCategory_Stylized,
            ];
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }

        const string format = KnownLibraryItemFormats.ClipTransition;
        using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, TestShell.Configuration.EditorConfig);
        Assert.That(viewModel.TransitionSections.Select(section => section.Key.Id), Is.EqualTo(s_categories.Select(category => category.Id)));
        Assert.That(library.GetTypesFromFormat(format), Is.EquivalentTo(s_categories.SelectMany(category => category.Types)));

        var gallery = new LibraryGalleryView(format)
        {
            DataContext = viewModel,
            Margin = new Avalonia.Thickness(8),
        };
        var window = new Window { Content = gallery, Width = 640, Height = 880 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            string?[] headings = gallery.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("effectCategoryHeading"))
                .Select(text => text.Text)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(headings, Is.EqualTo(localizedTitles));
                for (int i = 0; i < s_categories.Length; i++)
                {
                    Assert.That(viewModel.TransitionSections[i].Items.Select(tile => tile.Type),
                        Is.EqualTo(s_categories[i].Types), s_categories[i].Id);
                }
            });

            if (HeadlessCapture.IsRequested && language == "en")
            {
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                {
                    HeadlessTestHelpers.Render();
                    AnimatedThumbnail[] shown = gallery.GetVisualDescendants().OfType<AnimatedThumbnail>()
                        .Where(thumbnail => thumbnail.IsOnScreen && thumbnail.Source != null)
                        .ToArray();
                    return shown.Length > 0 && shown.All(thumbnail =>
                        thumbnail.Source!.Still != null || thumbnail.Source is LibraryThumbnailSource { Failed: true });
                }, TimeSpan.FromSeconds(30), "The transition gallery thumbnails did not settle.");
                HeadlessCapture.SaveIfRequested(gallery, nameof(LibraryTransitionCategoryTests), "transition-gallery-all-categories");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
