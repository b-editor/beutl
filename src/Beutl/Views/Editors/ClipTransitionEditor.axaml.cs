using Avalonia.Controls;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class ClipTransitionEditor : UserControl
{
    public ClipTransitionEditor()
    {
        InitializeComponent();
    }

    // Selecting the type the transition already has, as the binding does when the value changes, changes
    // nothing. A change that is refused, for example across a locked element, puts the selection back.
    private void OnTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ClipTransitionEditorViewModel { IsDisposed: false } viewModel
            && typeComboBox.SelectedItem is ClipTransitionTypeItem item)
        {
            viewModel.ChangeType(item.Type);
            if (!ReferenceEquals(typeComboBox.SelectedItem, viewModel.SelectedType.Value))
            {
                typeComboBox.SelectedItem = viewModel.SelectedType.Value;
            }
        }
    }
}
