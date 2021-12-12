using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BEditorNext.Controls;
using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels;
using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public partial class EditorBadge : UserControl
{
    public EditorBadge()
    {
        InitializeComponent();
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel vm) return;

        if (vm.Setter.Property.IsAnimatable() && vm.Setter is IAnimatableSetter setter)
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
                    Header = $"{propsVm.Layer.Name} / {setter.Property.GetJsonName() ?? setter.Property.Name}",
                    DataContext = new AnimationTimelineViewModel(propsVm.Layer, setter),
                    Content = new AnimationTimeline(),
                    IsClosable = true
                });
            }
        }
    }
}
