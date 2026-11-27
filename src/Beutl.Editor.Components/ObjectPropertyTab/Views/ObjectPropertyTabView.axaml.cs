using Avalonia.Controls;

using Beutl.Controls.Converters;
using Beutl.Editor.Components.PropertyEditors.Views;

namespace Beutl.Editor.Components.ObjectPropertyTab.Views;

public sealed partial class ObjectPropertyTabView : UserControl
{
    private readonly PropertyLinkSelectionAttachment _linkSelection;

    public ObjectPropertyTabView()
    {
        Resources["ViewModelToViewConverter"] = PropertyEditorContextToViewConverter.Instance;
        InitializeComponent();
        _linkSelection = new PropertyLinkSelectionAttachment(this, linkSelectionPrompt);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _linkSelection.SetDataContext(DataContext);
    }
}
