using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class FilterEffectListItemEditor : UserControl, IListItemEditor
{
    public static readonly DirectProperty<FilterEffectListItemEditor, Control?> ReorderHandleProperty =
        AvaloniaProperty.RegisterDirect<FilterEffectListItemEditor, Control?>(
            nameof(ReorderHandle),
            o => o.ReorderHandle);

    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;
    private UnknownObjectView? _unknownObjectView;

    public FilterEffectListItemEditor()
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

        this.GetObservable(DataContextProperty)
            .Select(x => x as FilterEffectEditorViewModel)
            .Select(x => x?.IsPresenter ?? Observable.Return(false))
            .Switch()
            .CombineLatest(presenterEditor.GetObservable(PropertyEditor.ReorderHandleProperty))
            .Subscribe(t => UpdateReorderHandle(t.First, t.Second));
    }

    public Control? ReorderHandle
    {
        get;
        private set => SetAndRaise(ReorderHandleProperty, ref field, value);
    }

    private void UpdateReorderHandle(bool isPresenter, Control? presenterReorderHandle)
    {
        ReorderHandle = isPresenter
            ? presenterReorderHandle
            : reorderHandle;
    }

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FilterEffectEditorViewModel { IsDisposed: false } vm) return;

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
}
