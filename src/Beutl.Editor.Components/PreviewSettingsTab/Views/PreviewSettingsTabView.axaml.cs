using Avalonia.Controls;

using Beutl.Controls.Converters;

namespace Beutl.Editor.Components.PreviewSettingsTab.Views;

public partial class PreviewSettingsTabView : UserControl
{
    public PreviewSettingsTabView()
    {
        InitializeComponent();
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
    }
}
