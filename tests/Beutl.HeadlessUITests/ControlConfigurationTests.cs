using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.NativeContextMenu;

namespace Beutl.HeadlessUITests;

public class ControlConfigurationTests
{
    [AvaloniaTest]
    public void Controls_follow_their_window_settings_when_reparented_and_use_defaults_without_a_host()
    {
        var first = new GlobalConfiguration();
        var second = new GlobalConfiguration();
        first.EditorConfig.EnablePointerLockInProperty.Value = false;
        first.ViewConfig.UseNativeContextMenus.Value = false;
        var control = new Button();
        var firstPanel = new StackPanel { Children = { control } };
        var secondPanel = new StackPanel();
        var firstWindow = new Window { Content = firstPanel };
        var secondWindow = new Window { Content = secondPanel };
        ControlConfiguration.SetConfiguration(firstWindow, first);
        ControlConfiguration.SetConfiguration(secondWindow, second);
        try
        {
            firstWindow.Show();
            secondWindow.Show();
            Assert.Multiple(() =>
            {
                Assert.That(ControlConfiguration.GetConfiguration(control), Is.SameAs(first));
                Assert.That(PointerLockHelper.IsEnabled(control), Is.False);
                Assert.That(NativeContextMenus.IsEnabled(control), Is.False);
            });

            firstPanel.Children.Remove(control);
            secondPanel.Children.Add(control);
            Assert.Multiple(() =>
            {
                Assert.That(ControlConfiguration.GetConfiguration(control), Is.SameAs(second));
                Assert.That(PointerLockHelper.IsEnabled(control), Is.True);
                Assert.That(NativeContextMenus.IsEnabled(control), Is.True);
            });

            second.EditorConfig.EnablePointerLockInProperty.Value = false;
            second.ViewConfig.UseNativeContextMenus.Value = false;
            Assert.That(PointerLockHelper.IsEnabled(control), Is.False);
            Assert.That(NativeContextMenus.IsEnabled(control), Is.False);
            secondPanel.Children.Remove(control);
            Assert.Multiple(() =>
            {
                Assert.That(ControlConfiguration.GetConfiguration(control), Is.Null);
                Assert.That(PointerLockHelper.IsEnabled(control), Is.EqualTo(new EditorConfig().EnablePointerLockInProperty.Value));
                Assert.That(NativeContextMenus.IsEnabled(control), Is.EqualTo(new ViewConfig().UseNativeContextMenus.Value));
            });
        }
        finally
        {
            firstWindow.Close();
            secondWindow.Close();
        }
    }
}
