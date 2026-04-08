using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Engine;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class CoreObjectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;
    private FallbackObjectView? _fallbackObjectView;
    private bool _flyoutOpen;

    public CoreObjectEditor()
    {
        InitializeComponent();
        expandToggle.GetObservable(ToggleButton.IsCheckedProperty)
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
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyTabViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyTabViewModel>(i =>
                  ReferenceEquals(i.ChildContext.Value?.Target, viewModel.Value.Value))
              ?? new ObjectPropertyTabViewModel(editViewModel);

        objViewModel.NavigateCore(viewModel.Value.Value, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
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

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ICoreObjectEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
    }

    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel vm || vm.IsDisposed) return;
        try
        {
            await vm.CopyAsync();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private async void PasteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BaseEditorViewModel vm || vm.IsDisposed) return;
        try
        {
            await vm.PasteAsync();
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        OnSelectTargetRequested();
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

    private async void OnSelectTargetRequested()
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
}
