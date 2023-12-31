using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

using Beutl.Controls.PropertyEditors;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;
public partial class SceneSettingsTab : UserControl
{
    private readonly CompositeDisposable _disposables = [];

    public SceneSettingsTab()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SceneSettingsTabViewModel viewModel)
        {
            sizeEditor.Bind(Int2Editor.FirstValueProperty, viewModel.Width.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);
            sizeEditor.Bind(Int2Editor.SecondValueProperty, viewModel.Height.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);

            countEditor.Bind(IntEditor.ValueProperty, viewModel.LayerCount.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);
        }
        else
        {
            _disposables.Clear();
        }
    }
}

public sealed class IntEditor : NumberEditor<int>
{
}

public sealed class Int2Editor : Vector2Editor<int>
{
}
