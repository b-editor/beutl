using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views;

public partial class PlayerView
{
    // A right click on the time chooses how the player, the timeline and the marker lists write times, in one click.
    private void ConfigureTimeDisplayFormatMenu(Control? timePanel)
    {
        if (timePanel is null)
            return;

        var menu = new FAMenuFlyout();
        foreach ((TimeDisplayFormat format, string name) in new[]
        {
            (TimeDisplayFormat.Time, SettingsStrings.TimeDisplayFormat_Time),
            (TimeDisplayFormat.Timecode, SettingsStrings.TimeDisplayFormat_Timecode),
            (TimeDisplayFormat.Frames, SettingsStrings.TimeDisplayFormat_Frames),
        })
        {
            var item = new FARadioMenuFlyoutItem { Text = name, Tag = format, GroupName = nameof(TimeDisplayFormat) };
            item.Click += OnTimeDisplayFormatClick;
            UsageTracking.SetFeature(item, "Preview.TimeDisplayFormat");
            menu.Items.Add(item);
        }

        // Marked as it opens, since another editor or the settings page can change the format meanwhile.
        menu.Opening += OnTimeDisplayFormatMenuOpening;
        timePanel.ContextFlyout = menu;
    }

    private void OnTimeDisplayFormatMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not FAMenuFlyout menu || DataContext is not PlayerViewModel viewModel)
            return;

        foreach (FARadioMenuFlyoutItem item in menu.Items.OfType<FARadioMenuFlyoutItem>())
            item.IsChecked = item.Tag is TimeDisplayFormat format && format == viewModel.TimeDisplayFormat.Value;
    }

    private void OnTimeDisplayFormatClick(object? sender, RoutedEventArgs e)
    {
        if (sender is FARadioMenuFlyoutItem { Tag: TimeDisplayFormat format } && DataContext is PlayerViewModel viewModel)
            viewModel.TimeDisplayFormat.Value = format;
    }
}
