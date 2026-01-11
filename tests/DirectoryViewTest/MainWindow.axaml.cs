using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Beutl.Controls;

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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
