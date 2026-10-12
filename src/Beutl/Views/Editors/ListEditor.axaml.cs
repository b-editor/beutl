using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.Behaviors;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public sealed class ListEditorDragBehavior : GenericDragBehavior
{
    public ListEditorDragBehavior()
    {
        Orientation = Orientation.Vertical;
    }

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
    private static readonly Lazy<CrossFade> s_crossFade = new(() => new(TimeSpan.FromSeconds(0.25)));
    private CancellationTokenSource? _lastTransitionCts;
    private bool _flyoutOpen;

    public ListEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async value =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();

                if (value == true)
                {
                    await s_crossFade.Value.Start(null, expandItem, _lastTransitionCts.Token);
                }
                else
                {
                    await s_crossFade.Value.Start(expandItem, null, _lastTransitionCts.Token);
                }

                expandItem.IsVisible = value == true;
            });
    }

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
                NotificationService.ShowError(Strings.Error, ex.Message);
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
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        }
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;
        if (DataContext is not IListEditorViewModel { IsDisposed: false } viewModel) return null;

        try
        {
            _flyoutOpen = true;
            Type propertyType = viewModel.ItemType;
            string format = propertyType.FullName!;
            var selectVm = new SelectLibraryItemDialogViewModel(format, propertyType);

            return await LibraryItemPickerHelper.ShowAsync(this, selectVm, format);
        }
        finally
        {
            _flyoutOpen = false;
        }
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
                progress.IsVisible = progress.IsIndeterminate = true;

                var availableTypes = await Task.Run(() => FindAddableItemTypes(viewModel.ItemType));

                progress.IsVisible = progress.IsIndeterminate = false;

                Type? selectedType = await ChooseItemTypeAsync(availableTypes);

                if (selectedType != null)
                {
                    viewModel.AddItem(selectedType);
                }
                else
                {
                    NotificationService.ShowError(Strings.Error, "ListEditor<TItem>.OnAddClick");
                }
            }
        }
    }

    private static Type[] FindAddableItemTypes(Type itemType)
    {
        Type[]? availableTypes;

        if (itemType.IsSealed
            && (itemType.GetConstructor([]) != null
                || itemType.GetConstructors().Length == 0))
        {
            availableTypes = [itemType];
        }
        else
        {
            availableTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => !x.IsAbstract
                            && x.IsPublic
                            && x.IsAssignableTo(itemType)
                            && (x.GetConstructor([]) != null
                                || x.GetConstructors().Length == 0))
                .ToArray();
        }

        return availableTypes;
    }

    private async Task<Type?> ChooseItemTypeAsync(Type[] availableTypes)
    {
        Type? selectedType = null;

        if (availableTypes.Length == 1)
        {
            selectedType = availableTypes[0];
        }
        else if (availableTypes.Length > 1)
        {
            var result = await SelectTypeOrReference();
            if (result is Type t)
                selectedType = t;
        }

        return selectedType;
    }
}
