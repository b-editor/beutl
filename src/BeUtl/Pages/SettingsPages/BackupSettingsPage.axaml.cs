using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BeUtl.Pages.SettingsPages;
public partial class BackupSettingsPage : UserControl
{
    public BackupSettingsPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
