using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Controls;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

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
        if (DataContext is not BaseEditorViewModel vm) return;

        if (vm.Setter is IAnimatablePropertyInstance setter)
        {
            EditView editView = this.FindLogicalAncestorOfType<EditView>();

            foreach (DraggableTabItem item in editView.BottomTabView.Items.OfType<DraggableTabItem>())
            {
                if (item.DataContext is AnimationTimelineViewModel anmVm && ReferenceEquals(anmVm.Setter, setter))
                {
                    editView.BottomTabView.SelectedItem = item;
                    return;
                }
            }

            PropertiesEditor propsEdit = this.FindLogicalAncestorOfType<PropertiesEditor>();

            if (propsEdit.DataContext is PropertiesEditorViewModel propsVm)
            {
                editView.BottomTabView.AddTab(new DraggableTabItem
                {
                    Header = $"{propsVm.Layer.Name} / {setter.Property.Name}",
                    DataContext = new AnimationTimelineViewModel(propsVm.Layer, setter, vm),
                    Content = new AnimationTimeline(),
                    IsClosable = true
                });
            }
        }
    }
}
