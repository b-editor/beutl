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
        if (DataContext is not BaseEditorViewModel vm) return;

        if (vm.Setter is IAnimatablePropertyInstance setter)
        {
            EditView editView = this.FindLogicalAncestorOfType<EditView>();

            foreach (FATabViewItem item in editView.BottomTabView.TabItems.OfType<FATabViewItem>())
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
                var item = new FATabViewItem
                {
                    Header = $"{propsVm.Layer.Name} / {setter.Property.Name}",
                    DataContext = new AnimationTimelineViewModel(propsVm.Layer, setter, vm.Description),
                    Content = new AnimationTimeline(),
                    IsClosable = true
                };
                (editView.BottomTabView.TabItems as IList)?.Add(item);
                item.IsSelected = true;
            }
        }
    }
}
