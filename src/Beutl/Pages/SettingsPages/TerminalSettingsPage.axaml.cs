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
        ShellPathBox.PlaceholderText = TerminalTabViewModel.ResolveShell().Path;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not TerminalConfig configuration) return;
        // Keep a saved font selectable if this computer does not have it installed.
        FontFamilyBox.ItemsSource = TerminalFontCatalog.GetSystemFontFamilies()
            .Prepend(string.Empty).Append(configuration.FontFamily.Value)
            .Distinct(StringComparer.Ordinal).ToArray();
    }
}
