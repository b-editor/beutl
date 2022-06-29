using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BeUtl.Views.Editors;

public partial class StyleEditor : UserControl
{
    public StyleEditor()
    {
        InitializeComponent();
        targetTypeBox.ItemSelector = (_, obj) => obj.ToString();
        targetTypeBox.FilterMode = AutoCompleteFilterMode.Contains;
    }
}
