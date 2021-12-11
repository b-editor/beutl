using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditorNext.Views.Editors;
public partial class EditorBadge : UserControl
{
    public EditorBadge()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
