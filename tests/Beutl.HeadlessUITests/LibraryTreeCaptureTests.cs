using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[Explicit("Produces library tree captures for visual review.")]
public sealed class LibraryTreeCaptureTests
{
    [AvaloniaTest]
    public void Capture_the_English_library_tree() => CaptureLibraryTree("en-US");

    [AvaloniaTest]
    public void Capture_the_Japanese_library_tree() => CaptureLibraryTree("ja-JP");

    private static void CaptureLibraryTree(string cultureName)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            using var library = new LibraryService();
            LibraryRegistrar.RegisterAll(library);
            using var viewModel = new LibraryTabViewModel(TestShell.FontManager, library, new EditorConfig());
            var window = new Window
            {
                Width = 420,
                Height = 720,
                RequestedThemeVariant = ThemeVariant.Dark,
                Content = new LibraryTabView { DataContext = viewModel },
            };
            try
            {
                window.Show();
                HeadlessTestHelpers.Render(5);
                var tree = window.GetVisualDescendants().OfType<TreeView>().Single(view => view.Name == "LibraryTree");
                if (cultureName == "ja-JP")
                {
                    LibraryItemViewModel group = viewModel.LibraryItems.Single(item =>
                        item.Children.Any(child => child.DisplayName == GraphicsStrings.Scene3D));
                    Expand(tree, group);
                }

                HeadlessCapture.SaveIfRequested(window, "library-names", $"library-tree-{cultureName}");
                if (cultureName == "en-US")
                {
                    CaptureEffects(window, tree, viewModel);
                }
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static void CaptureEffects(Window window, TreeView tree, LibraryTabViewModel viewModel)
    {
        window.Height = 1740;
        LibraryItemViewModel effects = viewModel.LibraryItems.Single(item => item.DisplayName == GraphicsStrings.FilterEffect);
        TreeViewItem container = Expand(tree, effects);
        string[] categories =
        [
            GraphicsStrings.FilterEffectCategory_BlurAndSharpen,
            GraphicsStrings.FilterEffectCategory_ColorCorrection,
            GraphicsStrings.FilterEffectCategory_LightAndShadow,
            GraphicsStrings.FilterEffectCategory_MaskingAndKeying,
            GraphicsStrings.FilterEffectCategory_TransformAndDistort,
            GraphicsStrings.FilterEffectCategory_TimeAndAnimation,
        ];
        foreach (LibraryItemViewModel category in effects.Children.Where(item => categories.Contains(item.DisplayName)))
        {
            Expand(container, category);
        }

        var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        Point position = container.TranslatePoint(default, scroll)!.Value;
        scroll.Offset += new Vector(0, position.Y);
        HeadlessTestHelpers.Render(5);
        HeadlessCapture.SaveIfRequested(window, "library-names", "library-effects-en-US");
    }

    private static TreeViewItem Expand(ItemsControl parent, LibraryItemViewModel item)
    {
        var container = parent.ContainerFromItem(item) as TreeViewItem;
        Assert.That(container, Is.Not.Null, item.DisplayName);
        container!.IsExpanded = true;
        HeadlessTestHelpers.Render(3);
        return container;
    }
}
