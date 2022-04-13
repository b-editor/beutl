using System.Collections;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

using FATabViewItem = FluentAvalonia.UI.Controls.TabViewItem;

namespace BeUtl.Views.Editors;

public partial class EditorBadge : UserControl
{
    public EditorBadge()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && viewModel.Setter is IAnimatablePropertyInstance setter)
        {
            EditView editView = this.FindLogicalAncestorOfType<EditView>();
            if (editView.DataContext is EditViewModel editViewModel)
            {
                AnimationTimelineViewModel? anmViewModel =
                    editViewModel.AnimationTimelines.FirstOrDefault(i => ReferenceEquals(i.Setter, setter));

                if (anmViewModel != null)
                {
                    anmViewModel.IsSelected.Value = true;
                }
                else
                {
                    Layer? layer = setter.FindRequiredLogicalParent<Layer>();
                    editViewModel.AnimationTimelines.Add(
                        new AnimationTimelineViewModel(layer, setter, viewModel.Description));
                }
            }
        }
    }
}
