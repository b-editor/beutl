using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Data;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;

using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.SceneSettingsTab.Views;

public partial class SceneSettingsTabView : UserControl
{
    private readonly CompositeDisposable _disposables = [];

    public SceneSettingsTabView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SceneSettingsTabViewModel viewModel)
        {
            sizeEditor.Bind(Vector2Editor<int>.FirstValueProperty, viewModel.Width.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);
            sizeEditor.Bind(Vector2Editor<int>.SecondValueProperty, viewModel.Height.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);

            countEditor.Bind(NumberEditor<int>.ValueProperty, viewModel.LayerCount.ToPropertyBinding(BindingMode.TwoWay))
                .DisposeWith(_disposables);
        }
        else
        {
            _disposables.Clear();
        }
    }
}
