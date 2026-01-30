using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Engine;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class CoreObjectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;

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
    }

    protected virtual void OnNavigate()
    {
    }

    protected virtual void OnNew()
    {
    }

    protected virtual void OnDelete()
    {
    }

    protected virtual void OnSelectTargetRequested()
    {
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        OnNavigate();
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void NewClick(object? sender, RoutedEventArgs e)
    {
        OnNew();
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        OnDelete();
    }

    private void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        OnSelectTargetRequested();
    }
}

public sealed class CoreObjectEditor<T> : CoreObjectEditor
    where T : CoreObject
{
    private bool _flyoutOpen;

    protected override void OnNavigate()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyEditorViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>(i =>
                  ReferenceEquals(i.ChildContext.Value?.Target, viewModel.Value.Value))
              ?? new ObjectPropertyEditorViewModel(editViewModel);

        objViewModel.NavigateCore(viewModel.Value.Value, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    protected override async void OnNew()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return;

        Type propertyType = viewModel.PropertyAdapter.PropertyType;

        if (propertyType.IsSealed)
        {
            if (propertyType.GetConstructor([])?.Invoke(null) is T typed)
            {
                viewModel.SetValue(viewModel.Value.Value, typed);
            }
            return;
        }

        object? result = await SelectTypeOrReference();

        switch (result)
        {
            case Type selectedType:
                if (selectedType.GetConstructor([])?.Invoke(null) is T typed)
                {
                    viewModel.SetValue(viewModel.Value.Value, typed);
                }
                break;
            case T target:
                Type? presenterType = PresenterTypeAttribute.GetPresenterType(propertyType);
                if (presenterType?.GetConstructor([])?.Invoke(null) is T presenterInstance)
                {
                    viewModel.SetValue(viewModel.Value.Value, presenterInstance);
                    viewModel.SetTarget(target);
                }
                break;
        }
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return null;

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

    protected override void OnDelete()
    {
        if (DataContext is not CoreObjectEditorViewModel<T> { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
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
