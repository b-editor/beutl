using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Pages.SettingsPages;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

// This page enabled x:CompileBindings at the root, which changes how every {Binding} in the file is
// resolved — including the accent palette's untyped item bindings, whose DataContext is a Color and
// not the page's x:DataType. Inflate the real page and read a swatch back.
[TestFixture]
public class ViewSettingsPagePaletteTests
{
    [AvaloniaTest]
    public void AccentPaletteSwatches_BindToTheirColorItem()
    {
        GlobalConfiguration.Instance.ViewConfig.UseCustomAccentColor = true;

        var viewModel = new ViewSettingsPageViewModel(
            new Lazy<EditorSettingsPageViewModel>(() => new EditorSettingsPageViewModel()));
        var page = new ViewSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700 };

        window.Show();
        viewModel.UseCustomAccent.Value = true;

        // The palette lives inside the collapsed accent OptionsDisplayItem, so its containers are
        // not realized until it expands.
        foreach (OptionsDisplayItem item in page.GetLogicalDescendants().OfType<OptionsDisplayItem>()
                     .Where(x => x.Expands))
        {
            item.IsExpanded = true;
        }

        window.UpdateLayout();

        ListBox palette = page.GetLogicalDescendants().OfType<ListBox>()
            .First(x => ReferenceEquals(x.ItemsSource, viewModel.PredefinedColors));
        palette.UpdateLayout();

        Color expected = viewModel.PredefinedColors[0];
        var container = palette.ContainerFromIndex(0) as ListBoxItem;
        Assert.That(container, Is.Not.Null, "the palette should realize its item containers once expanded");
        container!.ApplyTemplate();

        SolidColorBrush?[] brushes = container.GetVisualDescendants()
            .OfType<Border>()
            .Select(x => x.Background as SolidColorBrush)
            .Where(x => x != null)
            .ToArray();

        Assert.That(brushes, Is.Not.Empty, "the swatch template should produce a SolidColorBrush");
        Assert.That(
            brushes.Select(x => x!.Color), Does.Contain(expected),
            $"a swatch should paint its own palette color ({expected}), not the page's DataContext");
    }
}
