using Avalonia.Controls;
using Beutl.Controls.Converters;

namespace Beutl.Editor.Components.SoundPannerProperties.Views;

public sealed partial class SoundPannerEditor : UserControl
{
    public SoundPannerEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
    }
}
