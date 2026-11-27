using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.Pages;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;
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
        var settings = TestShell.Configuration.TerminalConfig;
        var previous = (settings.ColorScheme.Value, settings.FontSize.Value, settings.FontFamily.Value, settings.ScrollbackLines.Value);
        var culture = CultureInfo.CurrentUICulture;
        using var model = TestShell.MainViewModel.CreateSettingsDialog();
        SettingsDialog? dialog = null;
        using var view = new TerminalTabView();
        using var host = new ControlHost(view);
        ControlConfiguration.SetConfiguration(host.Window, TestShell.Configuration);
        Beutl.Controls.ControlFonts.SetFonts(host.Window, TestShell.FontManager);
        Beutl.Controls.ControlNotifications.SetNotifications(host.Window, TestShell.Notifications);
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
            Assert.That(page.DataContext, Is.SameAs(model.Terminal));
            Assert.That(model.Terminal.Config, Is.SameAs(settings));
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
            using var nextHost = new ControlHost(next);
            ControlConfiguration.SetConfiguration(nextHost.Window, TestShell.Configuration);
            Beutl.Controls.ControlFonts.SetFonts(nextHost.Window, TestShell.FontManager);
            Beutl.Controls.ControlNotifications.SetNotifications(nextHost.Window, TestShell.Notifications);
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
    public void Display_settings_follow_owner_inheritance_and_default_sources()
    {
        var first = new GlobalConfiguration();
        first.TerminalConfig.FontSize.Value = 18;
        first.TerminalConfig.ColorScheme.Value = TerminalColorScheme.Light;
        var second = new GlobalConfiguration();
        second.TerminalConfig.FontSize.Value = 22;
        second.TerminalConfig.ColorScheme.Value = TerminalColorScheme.Dark;
        var owned = new GlobalConfiguration();
        owned.TerminalConfig.FontSize.Value = 26;
        owned.TerminalConfig.ColorScheme.Value = TerminalColorScheme.Light;
        var editor = TestShell.CreateEditorContextMock();
        editor.Setup(x => x.GetService(typeof(GlobalConfiguration))).Returns(owned);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
        editor.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
        editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
        using var owner = new TerminalTabViewModel(editor.Object);
        using var view = new TerminalTabView();
        var terminal = view.FindControl<TerminalControl>("Terminal")!;
        double defaultFontSize = new TerminalConfig().FontSize.Value * 0.75;
        Assert.That(terminal.TerminalFontSize, Is.EqualTo(defaultFontSize));
        var window = new Window { Content = view };
        try
        {
            ControlConfiguration.SetConfiguration(window, first);
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(13.5));
            Assert.That(terminal.DefaultBackground.ToUInt32(), Is.EqualTo(TerminalTheme.Light.DefaultBackground));

            ControlConfiguration.SetConfiguration(window, second);
            first.TerminalConfig.FontSize.Value = 30;
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(16.5));
            Assert.That(terminal.DefaultBackground.ToUInt32(), Is.EqualTo(TerminalTheme.Dark.DefaultBackground));

            view.DataContext = owner;
            ControlConfiguration.SetConfiguration(window, first);
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(19.5));
            Assert.That(terminal.DefaultBackground.ToUInt32(), Is.EqualTo(TerminalTheme.Light.DefaultBackground));
            owned.TerminalConfig.FontSize.Value = 24;
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(18));

            view.DataContext = null;
            owned.TerminalConfig.FontSize.Value = 28;
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(22.5));
            ControlConfiguration.SetConfiguration(window, null);
            first.TerminalConfig.FontSize.Value = 32;
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(defaultFontSize));

            ControlConfiguration.SetConfiguration(window, second);
            view.Dispose();
            second.TerminalConfig.FontSize.Value = 34;
            Assert.That(terminal.TerminalFontSize, Is.EqualTo(16.5));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task Shell_edits_take_effect_on_restart_of_an_open_tab()
    {
        var settings = TestShell.Configuration.TerminalConfig;
        var previous = (settings.ShellPath.Value, settings.ShellArguments.Value, settings.FontSize.Value);
        try
        {
            bool windows = OperatingSystem.IsWindows();
            settings.ShellPath.Value = windows ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh";
            // Keep the first shell running while the settings are edited.
            settings.ShellArguments.Value = windows
                ? "/D\n/Q\n/C\nset /p input="
                : "-c\nread -r input";
            using var owner = new TerminalTabViewModel(TestShell.CreateEditorContextMock().Object);
            using var view = new TerminalTabView { DataContext = owner };
            using var host = new ControlHost(view, 640, 240);
            var terminal = view.FindControl<TerminalControl>("Terminal")!;
            await HeadlessTestHelpers.WaitUntilAsync(() => terminal.HasActiveSession,
                message: "The original terminal shell did not start.");
            using var originalShell = Process.GetProcessById(terminal.Pty!.ChildPid);

            var page = new TerminalSettingsPage { DataContext = new TerminalSettingsPageViewModel(settings) };
            using (var settingsHost = new ControlHost(page, 640, 520))
            {
                page.FindControl<NumericUpDown>("FontSizeBox")!.Value = 22;
                Assert.That(terminal.Renderer!.FontSize, Is.EqualTo(22));
                page.FindControl<TextBox>("ShellPathBox")!.Text = windows ? "cmd.exe" : "/usr/bin/printf";
                page.FindControl<TextBox>("ShellArgumentsBox")!.Text = windows
                    ? "/D\n/C\necho [terminal argument]"
                    : "[%s]\nterminal argument";
            }

            // Apply the layout changes from the font edit while the original shell is still running.
            HeadlessTestHelpers.Render();
            Assert.That(terminal.Pty?.ChildPid, Is.EqualTo(originalShell.Id));
            Assert.That(owner.IsProcessExited.Value, Is.False);

            originalShell.Kill();
            await HeadlessTestHelpers.WaitUntilAsync(() => owner.IsProcessExited.Value,
                message: "The terminal tab did not report the original shell's exit.");
            HeadlessTestHelpers.Render();
            var restart = view.FindControl<Button>("RestartButton")!;
            Assert.That(restart.IsEffectivelyVisible, Is.True);
            HeadlessTestHelpers.Click(restart);
            await HeadlessTestHelpers.WaitUntilAsync(() => terminal.TryExportSnapshot(
                TerminalSnapshotExportFormat.PlainText, default, out string text) && text.Contains("[terminal argument]"),
                message: "The restarted shell did not print the configured argument.");
        }
        finally
        {
            (settings.ShellPath.Value, settings.ShellArguments.Value, settings.FontSize.Value) = previous;
        }
    }
}
