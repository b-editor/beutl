using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditorNext.Views;
public partial class AnimationTimeline : UserControl
{
    public AnimationTimeline()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
