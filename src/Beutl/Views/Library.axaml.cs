using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Configuration;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.Views.LibraryViews;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIcon = FluentIcons.FluentAvalonia.SymbolIcon;

namespace Beutl.Views;

public sealed partial class Library : UserControl
{
    private static readonly (Symbol Icon, string Text, string Id, Func<Control> Create)[] s_tabItems =
    [
        (Symbol.Search, Strings.Search, "Search", () => new SearchView()),
        (Symbol.BezierCurveSquare, Strings.Easings, "Easings", () => new EasingsView()),
        (Symbol.Library, Strings.Library, "Library", () => new LibraryView()),
        (Symbol.Flow, Strings.NodeTree, "Nodes", () => new NodesView()),
    ];

    public Library()
    {
        InitializeComponent();

        tabStrip.ItemsSource = s_tabItems
            .Select(item =>
            {
                var tabItem = new TabStripItem();
                var binding = new Binding($"{nameof(LibraryViewModel.LibraryTabDisplayModes)}[{item.Id}]", BindingMode.OneWay)
                {
                    Converter = new FuncValueConverter<LibraryTabDisplayMode, bool>(v => v == LibraryTabDisplayMode.Show)
                };
                tabItem.Bind(IsVisibleProperty, binding);
                tabItem.Content = new StackPanel
                {
                    Children =
                    {
                        new SymbolIcon { Symbol = item.Icon },
                        new TextBlock { Text = item.Text }
                    }
                };
                var switchMenu = new ToggleMenuFlyoutItem
                {
                    [!ToggleMenuFlyoutItem.IsCheckedProperty] = binding,
                    Text = Strings.AlwaysDisplay
                };
                switchMenu.Click += (s, e) =>
                {
                    if (DataContext is LibraryViewModel viewModel)
                    {
                        viewModel.LibraryTabDisplayModes[item.Id] = !switchMenu.IsChecked
                            ? LibraryTabDisplayMode.Show : LibraryTabDisplayMode.Hide;
                    }
                };
                tabItem.ContextFlyout = new FAMenuFlyout
                {
                    ItemsSource = new[] { switchMenu }
                };

                return tabItem;
            })
            .ToArray();

        moreButton.ContextFlyout = new FAMenuFlyout
        {
            ItemsSource = s_tabItems.Select(item =>
            {
                var binding = new Binding($"{nameof(LibraryViewModel.LibraryTabDisplayModes)}[{item.Id}]", BindingMode.OneWay)
                {
                    Converter = new FuncValueConverter<LibraryTabDisplayMode, bool>(v => v == LibraryTabDisplayMode.Show)
                };
                var switchMenu = new ToggleMenuFlyoutItem
                {
                    [!ToggleMenuFlyoutItem.IsCheckedProperty] = binding,
                    Text = item.Text
                };
                switchMenu.Click += (s, e) =>
                {
                    if (DataContext is LibraryViewModel viewModel)
                    {
                        viewModel.LibraryTabDisplayModes[item.Id] = !switchMenu.IsChecked
                            ? LibraryTabDisplayMode.Show : LibraryTabDisplayMode.Hide;
                    }
                };
                return switchMenu;
            })
            .ToArray()
        };

        carousel.ItemsSource = s_tabItems.Select(item => item.Create())
            .ToArray();

        scroll.GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(_ => OnOffsetChanged());
        scroll.TemplateApplied += OnScrollViewerTemplateApplied;

        scroll.AddHandler(PointerWheelChangedEvent, OnScrollPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private void OnScrollPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        scroll.Offset = scroll.Offset.WithX(scroll.Offset.X - (e.Delta.Y * 16));
        e.Handled = true;
    }

    private void OnScrollViewerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        void OnScrollBarTemplateApplied(object? sender, TemplateAppliedEventArgs e)
        {
            if (sender is ScrollBar scrollBar)
            {
                OnOffsetChanged();
                scrollBar.TemplateApplied -= OnScrollBarTemplateApplied;
            }
        }

        ScrollBar? bar = e.NameScope.Find<ScrollBar>("PART_HorizontalScrollBar");

        if (bar != null)
        {
            bar.TemplateApplied += OnScrollBarTemplateApplied;
        }
        OnOffsetChanged();
        scroll.TemplateApplied -= OnScrollViewerTemplateApplied;
    }

    private void OnOffsetChanged()
    {
        Vector offset = scroll.Offset;
        Visual? left = scroll.GetVisualDescendants().FirstOrDefault(v => v.Name == "PART_LineUpButton");
        Visual? right = scroll.GetVisualDescendants().FirstOrDefault(v => v.Name == "PART_LineDownButton");
        if (left != null)
        {
            left.IsVisible = !MathUtilities.IsZero(offset.X);
        }

        if (right != null)
        {
            right.IsVisible = !MathUtilities.LessThanOrClose(tabStackPanel.Bounds.Width, scroll.Viewport.Width + offset.X);
        }
    }

    private void MoreButton_Click(object? sender, RoutedEventArgs e)
    {
        moreButton.ContextFlyout?.ShowAt(moreButton);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LibraryViewModel viewModel)
        {
            tabStrip.SelectedIndex = viewModel.SelectedTab;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is LibraryViewModel viewModel)
        {
            viewModel.SelectedTab = tabStrip.SelectedIndex;
        }
    }
}
