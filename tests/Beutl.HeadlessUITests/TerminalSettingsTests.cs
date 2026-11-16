using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Moq;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TerminalSettingsTests
{
    [AvaloniaTest]
    public async Task Settings_menu_changes_display_on_open_and_new_terminals()
    {
        var settings = GlobalConfiguration.Instance.TerminalConfig;
        var previous = (settings.ColorScheme.Value, settings.FontSize.Value, settings.FontFamily.Value, settings.ScrollbackLines.Value);
        var culture = CultureInfo.CurrentUICulture;
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        SettingsDialog? dialog = null;
        using var view = new TerminalTabView();
        using var host = new ControlHost(view);
        host.Window.SetRenderScaling(2);
        var terminal = view.FindControl<TerminalControl>("Terminal")!;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            dialog = HeadlessAppWindow.Create(() => new SettingsDialog());
            dialog.DataContext = model;
            dialog.Width = 920;
            dialog.Height = 700;
            dialog.RequestedThemeVariant = ThemeVariant.Light;
            model.GoToSettingsPage();
            dialog.Show();
            HeadlessTestHelpers.Render(3);

            var nav = dialog.FindControl<FANavigationView>("nav")!;
            nav.IsPaneOpen = true;
            HeadlessTestHelpers.Render(3);
            var item = nav.MenuItemsSource!.OfType<FANavigationViewItem>().Single(i => Equals(i.Tag, typeof(TerminalSettingsPage)));
            item.BringIntoView();
            HeadlessTestHelpers.Click(item);
            var frame = dialog.FindControl<FAFrame>("frame")!;
            await HeadlessTestHelpers.WaitUntilAsync(() => frame.Content is TerminalSettingsPage);
            var page = (TerminalSettingsPage)frame.Content!;
            Assert.That(page.DataContext, Is.SameAs(settings));
            uint applicationBackground = terminal.DefaultBackground.ToUInt32();
            if (HeadlessCapture.IsRequested)
            {
                await Task.Delay(350);
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(page, nameof(TerminalSettingsTests), "settings-ja");
            }

            var schemeBox = page.FindControl<ComboBox>("ColorSchemeBox")!;
            var fontBox = page.FindControl<ComboBox>("FontFamilyBox")!;
            string family = fontBox.Items.OfType<string>().First(f => f.Length > 0);
            schemeBox.SelectedIndex = (int)TerminalColorScheme.Light;
            fontBox.SelectedItem = family;
            page.FindControl<NumericUpDown>("FontSizeBox")!.Value = 20;
            page.FindControl<NumericUpDown>("ScrollbackBox")!.Value = 0;
            HeadlessTestHelpers.Settle();
            Assert.That(terminal.DefaultBackground.ToUInt32(), Is.EqualTo(TerminalTheme.Light.DefaultBackground));
            Assert.That(terminal.FontFamilyName, Is.EqualTo(family));
            Assert.That(terminal.Renderer!.FontSize, Is.EqualTo(20));
            Assert.That(terminal.ScrollbackLimit, Is.Zero);

            using var next = new TerminalTabView();
            var nextTerminal = next.FindControl<TerminalControl>("Terminal")!;
            Assert.That(nextTerminal.Renderer!.FontSize, Is.EqualTo(20));
            schemeBox.SelectedIndex = (int)TerminalColorScheme.Dark;
            Assert.That(nextTerminal.DefaultBackground.ToUInt32(), Is.EqualTo(TerminalTheme.Dark.DefaultBackground));
            schemeBox.SelectedIndex = (int)TerminalColorScheme.FollowApplication;
            Assert.That(terminal.DefaultBackground.ToUInt32(), Is.EqualTo(applicationBackground));
            fontBox.SelectedIndex = 0;
            Assert.That(terminal.FontFamilyName, Is.EqualTo(TerminalControl.FontFamilyNameProperty.GetDefaultValue(terminal)));
        }
        finally
        {
            dialog?.Close();
            CultureInfo.CurrentUICulture = culture;
            (settings.ColorScheme.Value, settings.FontSize.Value, settings.FontFamily.Value, settings.ScrollbackLines.Value) = previous;
        }
    }

    [AvaloniaTest]
    public async Task Shell_edits_take_effect_on_restart_of_an_open_tab()
    {
        var settings = GlobalConfiguration.Instance.TerminalConfig;
        var previous = (settings.ShellPath.Value, settings.ShellArguments.Value, settings.FontSize.Value);
        try
        {
            bool windows = OperatingSystem.IsWindows();
            settings.ShellPath.Value = windows ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";
            settings.ShellArguments.Value = null;
            using var owner = new TerminalTabViewModel(Mock.Of<IEditorContext>());
            using var view = new TerminalTabView { DataContext = owner };
            using var host = new ControlHost(view, 640, 240);
            var terminal = view.FindControl<TerminalControl>("Terminal")!;
            await HeadlessTestHelpers.WaitUntilAsync(() => terminal.HasActiveSession);
            int pid = terminal.Pty!.ChildPid;

            var page = new TerminalSettingsPage { DataContext = settings };
            using var settingsHost = new ControlHost(page, 640, 520);
            page.FindControl<NumericUpDown>("FontSizeBox")!.Value = 22;
            Assert.That(terminal.Renderer!.FontSize, Is.EqualTo(22));
            page.FindControl<TextBox>("ShellPathBox")!.Text = windows ? "cmd.exe" : "/usr/bin/printf";
            page.FindControl<TextBox>("ShellArgumentsBox")!.Text = windows
                ? "/D\n/C\necho [terminal argument]"
                : "[%s]\nterminal argument";
            Assert.That(terminal.Pty!.ChildPid, Is.EqualTo(pid));
            Assert.That(owner.IsProcessExited.Value, Is.False);

            terminal.SendInput("exit\r");
            await HeadlessTestHelpers.WaitUntilAsync(() => owner.IsProcessExited.Value);
            HeadlessTestHelpers.Click(view.FindControl<Button>("RestartButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => terminal.TryExportSnapshot(
                TerminalSnapshotExportFormat.PlainText, default, out string text) && text.Contains("[terminal argument]"));
        }
        finally
        {
            (settings.ShellPath.Value, settings.ShellArguments.Value, settings.FontSize.Value) = previous;
        }
    }
}
