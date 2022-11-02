using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BeUtl.Controls;

using FluentAvalonia.UI.Controls;

namespace DirectoryViewTest;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Content = new DirectoryTreeView(new FileSystemWatcher("D:\\source")
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = true
        });
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
