using Avalonia.Controls;
using Beutl.Controls.Converters;

namespace Beutl.Editor.Components.EqualizerProperties.Views;

public sealed partial class EqualizerPropertiesEditor : UserControl
{
    public EqualizerPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
    }
}
