using Beutl.Editor.Components.TimelineTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class ElementView
{
    // What PopulateAnimationMenu put before the items the XAML holds, which the next opening replaces.
    private readonly List<object> _animationMenuItems = [];

    // Each property with keyframes opens or closes its inline layer under the clip, and Show all opens every one.
    private void PopulateAnimationMenu(ElementViewModel viewModel, bool editable)
    {
        foreach (object item in _animationMenuItems)
        {
            animationMenu.Items.Remove(item);
        }

        _animationMenuItems.Clear();
        List<ElementAnimations.Entry> entries = viewModel.GetAnimationEntries();
        if (entries.Count == 0)
        {
            _animationMenuItems.Add(new FAMenuFlyoutItem { Text = Strings.NoAnimations, IsEnabled = false });
        }
        else
        {
            var showAll = new FAMenuFlyoutItem
            {
                Text = Strings.ShowAllAnimations,
                IsEnabled = editable && entries.Any(entry => !viewModel.IsInlineShown(entry.Adapter))
            };
            showAll.Click += (_, _) => (DataContext as ElementViewModel)?.ShowAllInlines(entries);
            _animationMenuItems.Add(showAll);
            _animationMenuItems.Add(new FAMenuFlyoutSeparator());

            foreach (ElementAnimations.Entry entry in entries)
            {
                var item = new FAToggleMenuFlyoutItem
                {
                    Text = entry.Label,
                    IsChecked = viewModel.IsInlineShown(entry.Adapter),
                    IsEnabled = editable && !entry.Adapter.IsReadOnly
                };
                item.Click += (_, _) => (DataContext as ElementViewModel)?.ToggleInline(entry.Adapter);
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
