using Avalonia.Controls;

using Beutl.Controls.Converters;

namespace Beutl.Views.Editors;

public partial class PropertiesEditor : UserControl
{
    public PropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
    }
}
