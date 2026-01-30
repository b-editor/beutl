using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class CoreObjectListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;

    public CoreObjectListItemEditor()
    {
        InitializeComponent();
        reorderHandle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NewClick(object? sender, RoutedEventArgs e)
    {
        OnNew();
    }

    protected virtual void OnNew()
    {
    }

    protected virtual void OnSelectTargetRequested()
    {
    }

    private void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        OnSelectTargetRequested();
    }
}

public sealed class CoreObjectListItemEditor<T> : CoreObjectListItemEditor
    where T : CoreObject
{
    private bool _flyoutOpen;

    protected override async void OnNew()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return;

        Type propertyType = viewModel.PropertyAdapter.PropertyType;
        Type? selectedType;

        if (propertyType.IsSealed)
        {
            selectedType = propertyType;
        }
        else
        {
            selectedType = await SelectType();
        }

        if (selectedType?.GetConstructor([])?.Invoke(null) is T typed)
        {
            viewModel.SetValue(viewModel.Value.Value, typed);
        }
    }

    private async Task<Type?> SelectType()
    {
        if (_flyoutOpen) return null;
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return null;

        try
        {
            _flyoutOpen = true;
            Type propertyType = viewModel.PropertyAdapter.PropertyType;
            string format = propertyType.FullName!;
            var selectVm = new SelectLibraryItemDialogViewModel(format, propertyType);
            var dialog = new LibraryItemPickerFlyout(selectVm);
            dialog.ShowAt(this);
            var tcs = new TaskCompletionSource<Type?>();
            dialog.Pinned += (_, item) => selectVm.Pin(item);
            dialog.Unpinned += (_, item) => selectVm.Unpin(item);
            dialog.Dismissed += (_, _) => tcs.SetResult(null);
            dialog.Confirmed += (_, _) =>
            {
                switch (selectVm.SelectedItem.Value?.UserData)
                {
                    case SingleTypeLibraryItem single:
                        tcs.SetResult(single.ImplementationType);
                        break;
                    case MultipleTypeLibraryItem multi:
                        tcs.SetResult(multi.Types.GetValueOrDefault(format));
                        break;
                    default:
                        tcs.SetResult(null);
                        break;
                }
            };

            return await tcs.Task;
        }
        finally
        {
            _flyoutOpen = false;
        }
    }

    protected override async void OnSelectTargetRequested()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            var targets = vm.GetAvailableTargets();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this);

            var tcs = new TaskCompletionSource<T?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as T);

            var result = await tcs.Task;
            if (result != null)
            {
                vm.SetTarget(result);
            }
        }
        finally
        {
            _flyoutOpen = false;
        }
    }
}
