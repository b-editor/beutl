using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;
public partial class SceneSettingsTab : UserControl
{
    private IDisposable? _bindRevoker1;
    private IDisposable? _bindRevoker2;

    public SceneSettingsTab()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SceneSettingsTabViewModel viewModel)
        {
            _bindRevoker1 = sizeEditor.Bind(Int2Editor.FirstValueProperty, viewModel.Width.ToPropertyBinding(BindingMode.TwoWay));
            _bindRevoker2 = sizeEditor.Bind(Int2Editor.SecondValueProperty, viewModel.Height.ToPropertyBinding(BindingMode.TwoWay));
        }
        else
        {
            _bindRevoker1?.Dispose();
            _bindRevoker1 = null;
            _bindRevoker2?.Dispose();
            _bindRevoker2 = null;
        }
    }
}

public sealed class Int2Editor : Vector2Editor<int>
{
}
