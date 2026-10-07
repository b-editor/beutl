using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Configuration;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views.LibraryViews;
using Beutl.Utilities;

using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.Editor.Components.LibraryTab.Views;

public sealed partial class LibraryTabView : UserControl
{
    private static readonly (Icon Icon, string Text, string Id, Func<Control> Create)[] s_tabItems =
    [
        (Icon.Search, Strings.Search, "Search", () => new SearchView()),
        (Icon.BezierCurveSquare, Strings.Easings, "Easings", () => new EasingsView()),
        (Icon.Library, Strings.Library, "Library", () => new LibraryView()),
        (Icon.Flow, Strings.NodeGraph, "Nodes", () => new NodesView()),
    ];

    public LibraryTabView()
    {
        InitializeComponent();

        tabStrip.ItemsSource = s_tabItems
            .Select(CreateTabItem)
            .ToArray();

        moreButton.ContextFlyout = new FAMenuFlyout
        {
            ItemsSource = s_tabItems.Select(CreateMoreMenuItem)
                .ToArray()
        };

        carousel.ItemsSource = s_tabItems.Select(item => item.Create())
            .ToArray();

        tabStrip.GetObservable(SelectingItemsControl.SelectedIndexProperty).Skip(1).Subscribe(index =>
        {
            if (index >= 0 && index < s_tabItems.Length && (tabStrip.IsPointerOver || tabStrip.IsKeyboardFocusWithin))
                Beutl.Editor.Services.UsageTelemetry.Current?.Record("tool.setting", "Library", "Section." + s_tabItems[index].Id);
        });

        scroll.GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(_ => OnOffsetChanged());
        scroll.TemplateApplied += OnScrollViewerTemplateApplied;

        scroll.AddHandler(PointerWheelChangedEvent, OnScrollPointerWheelChanged, RoutingStrategies.Tunnel);
    }

    private TabStripItem CreateTabItem((Icon Icon, string Text, string Id, Func<Control> Create) item)
    {
        var tabItem = new TabStripItem();
        var binding = CreateDisplayModeBinding(item.Id);
        tabItem.Bind(IsVisibleProperty, binding);
        AutomationProperties.SetName(tabItem, item.Text);
        ToolTip.SetTip(tabItem, item.Text);
        tabItem.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                new FluentIcon { Icon = item.Icon },
                new TextBlock { Text = item.Text, [Grid.ColumnProperty] = 1 }
            }
        };
        var switchMenu = CreateDisplayModeToggle(item.Id, Strings.AlwaysDisplay, binding);
        tabItem.ContextFlyout = new FAMenuFlyout
        {
            ItemsSource = new[] { switchMenu }
        };

        return tabItem;
    }

    private FAToggleMenuFlyoutItem CreateMoreMenuItem((Icon Icon, string Text, string Id, Func<Control> Create) item)
    {
        return CreateDisplayModeToggle(item.Id, item.Text, CreateDisplayModeBinding(item.Id));
    }

    private static ReflectionBinding CreateDisplayModeBinding(string id)
    {
        return new ReflectionBinding($"{nameof(LibraryTabViewModel.LibraryTabDisplayModes)}[{id}]")
        {
            Mode = BindingMode.OneWay,
            Converter = new FuncValueConverter<LibraryTabDisplayMode, bool>(v => v == LibraryTabDisplayMode.Show)
        };
    }

    private FAToggleMenuFlyoutItem CreateDisplayModeToggle(string id, string text, ReflectionBinding binding)
    {
        var switchMenu = new FAToggleMenuFlyoutItem
        {
            [!FAToggleMenuFlyoutItem.IsCheckedProperty] = binding,
            Text = text
        };
        switchMenu.Click += (s, e) =>
        {
            if (DataContext is LibraryTabViewModel viewModel)
            {
                viewModel.LibraryTabDisplayModes[id] = !switchMenu.IsChecked
                    ? LibraryTabDisplayMode.Show : LibraryTabDisplayMode.Hide;
            }
        };
        return switchMenu;
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
        if (DataContext is LibraryTabViewModel viewModel)
        {
            tabStrip.SelectedIndex = viewModel.SelectedTab;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (DataContext is LibraryTabViewModel viewModel)
        {
            viewModel.SelectedTab = tabStrip.SelectedIndex;
        }
    }
}
