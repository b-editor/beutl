using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Controls.Behaviors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public sealed class ListEditorDragBehavior : GenericDragBehavior
{
    // The reorder handle can be the item's expander toggle, so the press and the pointer capture stay with it and
    // only a release that ends a drag is handled here.
    protected override bool CapturesPointer => false;

    protected override DragEventHandling HandledEvents => DragEventHandling.WhileDragging;

    protected override bool CountsItems => true;

    protected override bool CancelsDragOnDetach => false;

    protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
    {
        if (itemsControl?.DataContext is IListEditorViewModel viewModel)
        {
            viewModel.MoveItem(oldIndex, newIndex);
        }
    }
}

public partial class ListEditor : UserControl
{
    private INotificationService Notifications => ControlNotifications.FromDataContextOrDefault(this);

    public ListEditor() : this(alwaysExpanded: false)
    {
    }

    internal ListEditor(bool alwaysExpanded)
    {
        InitializeComponent();
        if (alwaysExpanded)
        {
            expandToggle.Tag = null;
            layout.Children.Remove(expandToggle);
            fixedHeader.Children.Add(headerActions);
            fixedHeader.IsVisible = true;
            expandItem.IsVisible = true;
        }
        else
        {
            ExpandTransitionHelper.Attach(expandToggle, expandItem);
        }
    }

    internal Button AddButton => addButton;

    internal FluentIcon AddIcon => addIcon;

    internal Button RemoveButton => removeButton;

    private void InitializeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IListEditorViewModel viewModel)
        {
            try
            {
                viewModel.Initialize();
            }
            catch (InvalidOperationException ex)
            {
                Notifications.ShowError(Strings.Error, ex.Message);
            }
        }
    }

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IListEditorViewModel viewModel)
        {
            try
            {
                viewModel.Delete();
            }
            catch (InvalidOperationException ex)
            {
                Notifications.ShowError(Strings.Error, ex.Message);
            }
        }
    }

    private Task<object?> SelectTypeOrReference(Control? button)
    {
        if (DataContext is not IListEditorViewModel { IsDisposed: false } viewModel) return Task.FromResult<object?>(null);

        return PickerGuard.RunAsync(this, button, () =>
        {
            Type propertyType = viewModel.ItemType;
            string format = propertyType.FullName!;
            var selectVm = CoreObjectPickerHelper.CreateTypePicker(this, viewModel, format, propertyType, captureThumbnails: true);

            return LibraryItemPickerHelper.ShowAsync(this, selectVm, format);
        });
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IListEditorViewModel { IsDisposed: false } viewModel)
        {
            if (viewModel.IsListNull && sender is Button btn)
            {
                btn.ContextFlyout?.ShowAt(btn);
            }
            else if (!viewModel.IsListNull)
            {
                await AddItemAsync(viewModel, sender as Control);
            }
        }
    }

    internal virtual async Task AddItemAsync(IListEditorViewModel viewModel, Control? button)
    {
        Type itemType = viewModel.ItemType;
        if (itemType.IsSealed && (itemType.GetConstructor([]) != null || itemType.GetConstructors().Length == 0))
        {
            viewModel.AddItem(itemType);
        }
        else if (await SelectTypeOrReference(button) is Type selectedType)
        {
            viewModel.AddItem(selectedType);
        }
    }
}
