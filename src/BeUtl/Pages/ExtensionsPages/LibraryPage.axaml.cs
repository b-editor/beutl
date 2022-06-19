using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BeUtl.Pages.ExtensionsPages;
public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
