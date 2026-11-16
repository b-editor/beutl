using Avalonia.Controls;
using Avalonia.Data.Converters;
using Beutl.Configuration;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;

namespace Beutl.Pages.SettingsPages;

public sealed partial class TerminalSettingsPage : UserControl
{
    public static readonly FuncValueConverter<string, string> FontLabel =
        new(name => string.IsNullOrEmpty(name)
            ? string.Format(SettingsStrings.TerminalDefaultFont, TerminalControl.FontFamilyNameProperty.GetDefaultValue(typeof(TerminalControl)))
            : name);

    public TerminalSettingsPage()
    {
        InitializeComponent();
        FontSizeBox.Minimum = TerminalConfig.MinFontSize;
        FontSizeBox.Maximum = TerminalConfig.MaxFontSize;
        ScrollbackBox.Maximum = TerminalConfig.MaxScrollbackLines;
        // Keep a saved font selectable if this computer does not have it installed.
        FontFamilyBox.ItemsSource = TerminalFontCatalog.GetSystemFontFamilies()
            .Prepend(string.Empty).Append(GlobalConfiguration.Instance.TerminalConfig.FontFamily.Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        ShellPathBox.PlaceholderText = TerminalTabViewModel.ResolveShell(Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()).Path;
    }
}
