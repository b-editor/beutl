using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BeUtl.Pages.SettingsPages;
public partial class InfomationPage : UserControl
{
    public InfomationPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
