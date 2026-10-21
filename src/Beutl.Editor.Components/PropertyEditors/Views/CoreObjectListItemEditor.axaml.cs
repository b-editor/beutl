using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class CoreObjectListItemEditor : PresenterListItemEditor
{
    public CoreObjectListItemEditor()
    {
        InitializeComponent();
        AttachParts(reorderHandle, content, presenterEditor);
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        await CoreObjectPickerHelper.NewInstanceOrPickAsync(this, viewModel);
    }
}
