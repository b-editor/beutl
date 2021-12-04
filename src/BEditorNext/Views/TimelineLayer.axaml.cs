using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditorNext.Views;
public partial class TimelineLayer : UserControl
{
    public TimelineLayer()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
