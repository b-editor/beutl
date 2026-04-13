using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class AudioEffectListItemEditor : UserControl, IListItemEditor
{
    public AudioEffectListItemEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(reorderHandle, content, ExpandTransitionHelper.ListItemDuration);
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));

        reorderHandle.ContextFlyout = new FAMenuFlyout { Placement = PlacementMode.Pointer };
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)reorderHandle.ContextFlyout);
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
