using Beutl.Editor.Components.TimelineTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    // What PopulateAnimationMenu put before the items the XAML holds, which the next opening replaces.
    private readonly List<object> _animationMenuItems = [];

    // Each property with keyframes opens or closes its inline layer under the clip. Show all opens every one.
    private void PopulateAnimationMenu(ElementViewModel viewModel, bool editable)
    {
        foreach (object item in _animationMenuItems)
        {
            animationMenu.Items.Remove(item);
        }

        _animationMenuItems.Clear();
        List<AnimationMenuItem> rows = viewModel.GetAnimationMenu();
        if (rows.Count == 0)
        {
            _animationMenuItems.Add(new FAMenuFlyoutItem { Text = Strings.NoAnimations, IsEnabled = false });
        }
        else
        {
            var showAll = new FAMenuFlyoutItem
            {
                Text = Strings.ShowAllAnimations,
                IsEnabled = editable && rows.Any(row => !viewModel.IsInlineShown(row.Adapter))
            };
            showAll.Click += (_, _) => (DataContext as ElementViewModel)?.ShowAllInlines(rows.Select(row => row.Adapter));
            _animationMenuItems.Add(showAll);
            _animationMenuItems.Add(new FAMenuFlyoutSeparator());

            foreach (AnimationMenuItem row in rows)
            {
                var item = new FAToggleMenuFlyoutItem
                {
                    Text = row.Label,
                    IsChecked = viewModel.IsInlineShown(row.Adapter),
                    IsEnabled = editable && !row.Adapter.IsReadOnly
                };
                item.Click += (_, _) => (DataContext as ElementViewModel)?.ToggleInline(row.Adapter);
                _animationMenuItems.Add(item);
            }
        }

        _animationMenuItems.Add(new FAMenuFlyoutSeparator());
        for (int i = 0; i < _animationMenuItems.Count; i++)
        {
            animationMenu.Items.Insert(i, _animationMenuItems[i]);
        }
    }
}
