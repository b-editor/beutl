using Avalonia.Controls;
using Beutl.Controls.Converters;

namespace Beutl.Editor.Components.CompressorProperties.Views;

public sealed partial class CompressorPropertiesEditor : UserControl
{
    public CompressorPropertiesEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
    }
}
