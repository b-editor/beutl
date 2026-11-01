using Avalonia.Controls;
using Beutl.Controls.Converters;
using Beutl.Editor.Components.PropertyEditors.Views;

namespace Beutl.Editor.Components.SoundPannerProperties.Views;

public sealed partial class SoundPannerEditor : UserControl
{
    public SoundPannerEditor()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
    }
}
