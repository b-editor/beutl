using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ViewSettingsPageNativeContextMenuTests
{
    private static OptionsDisplayItem FindItem(ViewSettingsPage page) => page.GetLogicalDescendants()
        .OfType<OptionsDisplayItem>()
        .Single(item => Equals(item.Header, SettingsStrings.UseNativeContextMenus));

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void The_switch_shows_only_where_native_menus_exist(bool supported)
    {
        using var viewModel = new ViewSettingsPageViewModel { IsNativeContextMenuSupported = supported };
        var page = new ViewSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Assert.That(FindItem(page).IsVisible, Is.EqualTo(supported));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void By_default_the_switch_is_there_on_macOS_only()
    {
        using var viewModel = new ViewSettingsPageViewModel();

        Assert.That(viewModel.IsNativeContextMenuSupported, Is.EqualTo(OperatingSystem.IsMacOS()));
    }

    [AvaloniaTest]
    public void The_switch_and_the_setting_follow_each_other()
    {
        // ViewSettingsPageViewModel reads the ViewConfig singleton, so hand it back unchanged.
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        bool original = config.UseNativeContextMenus.Value;
        config.UseNativeContextMenus.Value = true;
        using var viewModel = new ViewSettingsPageViewModel { IsNativeContextMenuSupported = true };
        var page = new ViewSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var toggle = (ToggleSwitch)FindItem(page).ActionButton!;
            Assert.That(toggle.IsChecked, Is.True);

            toggle.IsChecked = false;
            HeadlessTestHelpers.Settle();
            Assert.That(config.UseNativeContextMenus.Value, Is.False);

            config.UseNativeContextMenus.Value = true;
            HeadlessTestHelpers.Settle();
            Assert.That(toggle.IsChecked, Is.True);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            config.UseNativeContextMenus.Value = original;
        }
    }
}
