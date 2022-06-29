using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Commands;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

using Button = Avalonia.Controls.Button;

namespace BeUtl.Views.Editors;

public partial class LayerOpsEditor : UserControl
{
    private static readonly Lazy<CrossFade> s_crossFade = new(() => new(TimeSpan.FromSeconds(0.25)));
    private CancellationTokenSource? _lastTransitionCts;

    public LayerOpsEditor()
    {
        Resources["LayerOpNameConverter"] = new FuncValueConverter<LayerOperation, string>(
            obj => obj != null
                ? LayerOperationRegistry.FindItem(obj.GetType())?.DisplayName.FindOrDefault() ?? string.Empty
                : string.Empty);
        InitializeComponent();
        toggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async value =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();

                if (value == true)
                {
                    await s_crossFade.Value.Start(null, expandItem, _lastTransitionCts.Token);
                }
                else
                {
                    await s_crossFade.Value.Start(expandItem, null, _lastTransitionCts.Token);
                }

                expandItem.IsVisible = value == true;
            });
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ILogical logical
            && DataContext is LayerOpsEditorViewModel { List.Value: { } list } viewModel
            && this.FindLogicalAncestorOfType<ObjectPropertyEditor>().DataContext is ObjectPropertyEditorViewModel parentViewModel)
        {
            Grid grid = logical.FindLogicalAncestorOfType<Grid>();
            int index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0 && list[index] is LayerOperation obj
                && obj.Parent is Layer layer)
            {
                IEditorContext editorContext = parentViewModel.ParentContext;
                OperationsEditorViewModel? ops = editorContext.FindToolTab<OperationsEditorViewModel>();

                if (ops != null)
                {
                    ops.Layer.Value = layer;
                    ops.ScrollTo(obj);
                    editorContext.OpenToolTab(ops);
                }
            }
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem
            && DataContext is LayerOpsEditorViewModel { List.Value: { } list } viewModel)
        {
            Grid grid = menuItem.FindLogicalAncestorOfType<Grid>();
            int index = items.ItemContainerGenerator.IndexFromContainer(grid.Parent);

            if (index >= 0)
            {
                new RemoveCommand<LayerOperation>(list, list[index]).DoAndRecord(CommandRecorder.Default);
            }
        }
    }
}
