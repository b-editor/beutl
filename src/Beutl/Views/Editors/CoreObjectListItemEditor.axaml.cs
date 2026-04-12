using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Views;
using Beutl.Engine;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

public partial class CoreObjectListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;
    private bool _flyoutOpen;

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

        this.GetObservable(DataContextProperty)
            .Select(x => x as IFallbackObjectViewModel)
            .Select(x => x?.IsFallback.Select(_ => x) ?? Observable.ReturnThenNever<IFallbackObjectViewModel?>(null))
            .Switch()
            .Where(v => v?.IsFallback.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _fallbackObjectView = new FallbackObjectView();
                content.Children.Add(_fallbackObjectView);
            });

        reorderHandle.ContextFlyout = new FAMenuFlyout();
        CopyPasteMenuHelper.AddMenus((FAMenuFlyout)reorderHandle.ContextFlyout!, this);
    }

    public Control? ReorderHandle => reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void NewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        Type propertyType = viewModel.PropertyAdapter.PropertyType;

        if (propertyType.IsSealed)
        {
            viewModel.SetNewInstance(propertyType);
            return;
        }

        object? result = await SelectTypeOrReference();

        switch (result)
        {
            case Type selectedType:
                viewModel.SetNewInstance(selectedType);
                break;
            case CoreObject target:
                viewModel.SetTarget(target);
                break;
        }
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            var targets = vm.GetAvailableTargets();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this);

            var tcs = new TaskCompletionSource<CoreObject?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object);

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

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return null;

        try
        {
            _flyoutOpen = true;
            Type propertyType = viewModel.PropertyAdapter.PropertyType;
            string format = propertyType.FullName!;
            var selectVm = new SelectLibraryItemDialogViewModel(format, propertyType);

            if (PresenterTypeAttribute.GetPresenterType(propertyType) != null)
            {
                var targets = viewModel.GetAvailableTargets();
                selectVm.InitializeReferences(targets);
            }

            var dialog = new LibraryItemPickerFlyout(selectVm);
            dialog.ShowAt(this);
            var tcs = new TaskCompletionSource<object?>();
            dialog.Pinned += (_, item) => selectVm.Pin(item);
            dialog.Unpinned += (_, item) => selectVm.Unpin(item);
            dialog.Dismissed += (_, _) => tcs.SetResult(null);
            dialog.Confirmed += (_, _) =>
            {
                switch (selectVm.SelectedItem.Value?.UserData)
                {
                    case TargetObjectInfo target:
                        tcs.SetResult(target.Object);
                        break;
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
}
