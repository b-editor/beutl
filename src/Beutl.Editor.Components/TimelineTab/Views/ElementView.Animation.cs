using Beutl.Editor.Components.TimelineTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    // What PopulateAnimationMenu put before the items the XAML holds, which the next opening replaces.
    private readonly List<object> _animationMenuItems = [];

    // A submenu for each object, and below it for each property that holds an object, down to the properties with
    // keyframes; each of those opens or closes its inline layer under the clip. Show all opens every one.
    private void PopulateAnimationMenu(ElementViewModel viewModel, bool editable)
    {
        foreach (object item in _animationMenuItems)
        {
            animationMenu.Items.Remove(item);
        }

        _animationMenuItems.Clear();
        List<AnimationMenuItem> rows = viewModel.GetAnimationMenu();
        IAnimatablePropertyAdapter[] properties = [.. rows.SelectMany(row => row.Adapters)];
        if (properties.Length == 0)
        {
            _animationMenuItems.Add(new FAMenuFlyoutItem { Text = Strings.NoAnimations, IsEnabled = false });
        }
        else
        {
            var showAll = new FAMenuFlyoutItem
            {
                Text = Strings.ShowAllAnimations,
                IsEnabled = editable && properties.Any(property => !viewModel.IsInlineShown(property))
            };
            showAll.Click += (_, _) => (DataContext as ElementViewModel)?.ShowAllInlines(properties);
            _animationMenuItems.Add(showAll);
            _animationMenuItems.Add(new FAMenuFlyoutSeparator());

            foreach (AnimationMenuItem row in rows)
            {
                _animationMenuItems.Add(CreateAnimationMenuItem(row, viewModel, editable));
            }
        }

        _animationMenuItems.Add(new FAMenuFlyoutSeparator());
        for (int i = 0; i < _animationMenuItems.Count; i++)
        {
            animationMenu.Items.Insert(i, _animationMenuItems[i]);
        }
    }

    private object CreateAnimationMenuItem(AnimationMenuItem row, ElementViewModel viewModel, bool editable)
    {
        if (row.Children.Count == 0)
            return CreateInlineToggle(row.Label, row.Adapter!, viewModel, editable);

        var submenu = new FAMenuFlyoutSubItem { Text = row.Label };
        // A property with keyframes of its own that also holds an object with some.
        if (row.Adapter is { } adapter)
        {
            submenu.Items.Add(CreateInlineToggle(row.Label, adapter, viewModel, editable));
            submenu.Items.Add(new FAMenuFlyoutSeparator());
        }

        foreach (AnimationMenuItem child in row.Children)
        {
            submenu.Items.Add(CreateAnimationMenuItem(child, viewModel, editable));
        }

        return submenu;
    }

    private FAToggleMenuFlyoutItem CreateInlineToggle(
        string label, IAnimatablePropertyAdapter property, ElementViewModel viewModel, bool editable)
    {
        var item = new FAToggleMenuFlyoutItem
        {
            Text = label,
            IsChecked = viewModel.IsInlineShown(property),
            IsEnabled = editable && !property.IsReadOnly
        };
        item.Click += (_, _) => (DataContext as ElementViewModel)?.ToggleInline(property);
        return item;
    }
}
