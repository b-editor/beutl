using Avalonia.Controls;

using Beutl.Controls.Converters;

namespace Beutl.Editor.Components.ObjectPropertyTab.Views;

public sealed partial class ObjectPropertyTabView : UserControl
{
    public ObjectPropertyTabView()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
    }
}
