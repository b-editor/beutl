using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels.Dialogs;
using Beutl.Editor.Components.Views;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class FilterEffectEditor : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;
    private UnknownObjectView? _unknownObjectView;
    private bool _flyoutOpen;

    public FilterEffectEditor()
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

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);

        this.GetObservable(DataContextProperty)
            .Select(x => x as FilterEffectEditorViewModel)
            .Select(x => x?.IsDummy.Select(_ => x) ?? Observable.ReturnThenNever<FilterEffectEditorViewModel?>(null))
            .Switch()
            .Where(v => v?.IsDummy.Value == true)
            .Take(1)
            .Subscribe(_ =>
            {
                _unknownObjectView = new UnknownObjectView();
                content.Children.Add(_unknownObjectView);
            });
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(BeutlDataFormats.FilterEffect) is { } typeName
            && TypeFormat.ToType(typeName) is { } type
            && DataContext is FilterEffectEditorViewModel { IsDisposed: false } viewModel)
        {
            if (viewModel.IsGroup.Value)
            {
                viewModel.AddItem(type);
            }
            else
            {
                viewModel.ChangeFilterType(type);
            }

            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BeutlDataFormats.FilterEffect)) return;

        e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        e.Handled = true;
    }

    private async void Tag_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        if (viewModel.IsGroup.Value)
        {
            object? result = await SelectTypeOrReference();

            try
            {
                switch (result)
                {
                    case Type type:
                        viewModel.AddItem(type);
                        break;
                    case FilterEffect fe:
                        Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect));
                        if (presenterType != null)
                        {
                            viewModel.AddTarget(presenterType, fe);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Strings.Error, ex.Message);
            }
        }
        else
        {
            expandToggle.ContextFlyout?.ShowAt(expandToggle);
        }
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var selectVm = new SelectFilterEffectTypeViewModel();

            if (DataContext is FilterEffectEditorViewModel { IsDisposed: false } vm
                && PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect)) != null)
            {
                var targets = vm.GetAvailableTargets();
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
                        tcs.SetResult(multi.Types.GetValueOrDefault(KnownLibraryItemFormats.FilterEffect));
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

    private async void ChangeFilterTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        object? result = await SelectTypeOrReference();

        try
        {
            switch (result)
            {
                case Type type:
                    viewModel.ChangeFilterType(type);
                    break;
                case FilterEffect target:
                    Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(FilterEffect));
                    if (presenterType != null)
                    {
                        viewModel.ChangeFilterType(presenterType);
                        viewModel.SetTarget(target);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.Error, ex.Message);
        }
    }

    private void SetNullClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.SetNull();
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            var targets = vm.GetAvailableTargets();
            var pickerVm = new TargetPickerFlyoutViewModel();
            pickerVm.Initialize(targets);

            var flyout = new TargetPickerFlyout(pickerVm);
            flyout.ShowAt(this);

            var tcs = new TaskCompletionSource<FilterEffect?>();
            flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
            flyout.Confirmed += (_, _) => tcs.TrySetResult(
                (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as FilterEffect);

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
