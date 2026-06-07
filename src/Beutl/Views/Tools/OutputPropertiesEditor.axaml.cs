using Avalonia.Controls;
using Beutl.Controls.Converters;

namespace Beutl.Views.Tools;

public partial class OutputPropertiesEditor : UserControl
{
    public OutputPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.HideMenu;
        InitializeComponent();
    }
}
