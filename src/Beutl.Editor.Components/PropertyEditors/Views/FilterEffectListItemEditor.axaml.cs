using Avalonia.Interactivity;
using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class FilterEffectListItemEditor : PresenterListItemEditor
{
    public FilterEffectListItemEditor()
    {
        InitializeComponent();
        AttachParts(reorderHandle, content, presenterEditor);
    }

    private void SelectEffectClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as FilterEffectEditorViewModel)?.SelectEffect();
    }
}
