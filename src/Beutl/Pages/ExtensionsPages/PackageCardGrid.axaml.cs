using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Api.Objects;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.ViewModels.ExtensionsPages;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.Pages.ExtensionsPages;

// The store's package cards, shared by the Home, search and publisher pages. A package card opens its details, and
// the Show more card runs More to load the next page.
public sealed partial class PackageCardGrid : ItemsRepeater
{
    public static readonly StyledProperty<AsyncReactiveCommand?> MoreProperty =
        AvaloniaProperty.Register<PackageCardGrid, AsyncReactiveCommand?>(nameof(More));

    public PackageCardGrid()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnCardClick);
    }

    public AsyncReactiveCommand? More
    {
        get => GetValue(MoreProperty);
        set => SetValue(MoreProperty, value);
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { DataContext: Package package }
            && this.FindLogicalAncestorOfType<FAFrame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), package);
            e.Handled = true;
        }
        else if (e.Source is Button { DataContext: LoadMoreItem })
        {
            More?.Execute();
            e.Handled = true;
        }
    }
}
