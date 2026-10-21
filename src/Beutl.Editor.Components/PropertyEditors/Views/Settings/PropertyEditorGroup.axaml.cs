using Avalonia.Controls;
using static Beutl.Editor.Components.PropertyEditors.Views.Settings.PropertiesEditor;

namespace Beutl.Editor.Components.PropertyEditors.Views.Settings;

public partial class PropertyEditorGroup : UserControl
{
    public PropertyEditorGroup()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
    }
}
