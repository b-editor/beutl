using Avalonia.Interactivity;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

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
