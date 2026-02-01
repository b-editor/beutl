using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Services;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed partial class BrushEditor : UserControl
{
    public static readonly StyledProperty<Avalonia.Media.Brush?> BrushProperty =
        AvaloniaProperty.Register<BrushEditor, Avalonia.Media.Brush?>(nameof(Brush));

    public static readonly StyledProperty<Media.Brush?> OriginalBrushProperty =
        AvaloniaProperty.Register<BrushEditor, Media.Brush?>(nameof(OriginalBrush));

    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    private BrushEditorFlyout? _flyout;
    private bool _flyoutOpen;

    public BrushEditor()
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

    public Avalonia.Media.Brush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public Media.Brush? OriginalBrush
    {
        get => GetValue(OriginalBrushProperty);
        set => SetValue(OriginalBrushProperty, value);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        (DataContext as BrushEditorViewModel)?.UpdateBrushPreview();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_flyout == null) return;

        if (change.Property == BrushProperty)
        {
            _flyout.Brush = Brush;
        }

        if (change.Property == OriginalBrushProperty)
        {
            _flyout.OriginalBrush = OriginalBrush;
            _flyout.DrawableName = GetDrawableName((OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue);
            _flyout.CanEditDrawable = (OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue is not null;

            if (change.OldValue is Media.DrawableBrush oldBrush)
            {
                oldBrush.Drawable.ValueChanged -= OnDrawableBrushPropertyChanged;
            }

            if (change.NewValue is Media.DrawableBrush newBrush)
            {
                newBrush.Drawable.ValueChanged += OnDrawableBrushPropertyChanged;
            }
        }
    }

    private void OnDrawableBrushPropertyChanged(object? sender, PropertyValueChangedEventArgs<Drawable?> e)
    {
        if (_flyout == null) return;

        if (e.NewValue != null)
        {
            _flyout.DrawableName = GetDrawableName(e.NewValue);
            _flyout.CanEditDrawable = true;
        }
        else
        {
            _flyout.CanEditDrawable = false;
        }
    }

    private static string GetDrawableName(Drawable? drawable)
    {
        if (drawable == null)
        {
            return Strings.CreateNew;
        }

        var type = drawable.GetType();
        return TypeDisplayHelpers.GetLocalizedName(type);
    }

    private async Task<object?> SelectTypeOrReference()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var selectVm = new SelectDrawableTypeViewModel();

            if (DataContext is BrushEditorViewModel { IsDisposed: false } vm
                && PresenterTypeAttribute.GetPresenterType(typeof(Brush)) != null)
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
                        tcs.SetResult(multi.Types.GetValueOrDefault(KnownLibraryItemFormats.Drawable));
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

    private void OpenFlyout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BrushEditorViewModel) return;

        if (_flyout == null)
        {
            _flyout = new BrushEditorFlyout();
            _flyout.GradientStopChanged += OnGradientStopChanged;
            _flyout.GradientStopConfirmed += OnGradientStopConfirmed;
            _flyout.GradientStopDeleted += OnGradientStopDeleted;
            _flyout.GradientStopAdded += OnGradientStopAdded;
            _flyout.ColorChanged += OnColorChanged;
            _flyout.ColorConfirmed += OnColorConfirmed;
            _flyout.BrushTypeChanged += OnBrushTypeChanged;
            _flyout.ChangeDrawableClicked += OnChangeDrawableClicked;
            _flyout.EditDrawableClicked += OnEditDrawableClicked;
        }

        _flyout.Brush = Brush;
        _flyout.OriginalBrush = OriginalBrush;
        _flyout.DrawableName = GetDrawableName((OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue);
        _flyout.CanEditDrawable = (OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue is not null;

        _flyout.ShowAt(this);
    }

    private void OnEditDrawableClicked(object? sender, EventArgs e)
    {
        // TODO: DrawablePropertyEditorを開く
        // ObjectPropertyEditorは不要なプロパティも表示されてしまうので
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.Value.Value is not DrawableBrush { Drawable.CurrentValue: { } drawable }) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyTabViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyTabViewModel>()
              ?? new ObjectPropertyTabViewModel(editViewModel);

        objViewModel.NavigateCore(drawable, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    private async void OnChangeDrawableClicked(object? sender, Button e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        object? result = await SelectTypeOrReference();

        switch (result)
        {
            case Type type:
                viewModel.ChangeDrawableType(type);
                break;
            case Brush target:
                Type? presenterType = PresenterTypeAttribute.GetPresenterType(typeof(Brush));
                if (presenterType?.GetConstructor([])?.Invoke(null) is Brush presenterInstance)
                {
                    viewModel.SetValue(viewModel.Value.Value, presenterInstance);
                    viewModel.SetTarget(target);
                }
                break;
        }
    }

    private void OnBrushTypeChanged(object? sender, BrushType e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        if (e == BrushType.SolidColorBrush)
        {
            viewModel.SetValue(viewModel.Value.Value, new SolidColorBrush { Color = { CurrentValue = Colors.White } });
        }
        else if (e == BrushType.Null)
        {
            viewModel.SetValue(viewModel.Value.Value, null);
        }
        else if (e is BrushType.ConicGradientBrush
                 or BrushType.LinearGradientBrush
                 or BrushType.RadialGradientBrush)
        {
            var gradStops = new List<GradientStop>();
            if (viewModel.Value.Value is GradientBrush oldBrush)
            {
                gradStops.AddRange(oldBrush.GradientStops.Select(v => new GradientStop(v.Color.CurrentValue, v.Offset.CurrentValue)));
            }
            else
            {
                gradStops.Add(new GradientStop(Colors.White, 0));
                gradStops.Add(new GradientStop(Colors.Black, 1));
            }

            GradientBrush brush = e switch
            {
                BrushType.LinearGradientBrush => new LinearGradientBrush(),
                BrushType.ConicGradientBrush => new ConicGradientBrush(),
                BrushType.RadialGradientBrush => new RadialGradientBrush(),
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, null)
            };
            brush.GradientStops.Replace(gradStops);

            viewModel.SetValue(viewModel.Value.Value, brush);
        }
        else if (e == BrushType.DrawableBrush)
        {
            viewModel.SetValue(viewModel.Value.Value, new DrawableBrush());
        }
        else if (e == BrushType.Presenter)
        {
            viewModel.SetValue(viewModel.Value.Value, new BrushPresenter());
        }
    }

    private void OnColorConfirmed(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is not BrushEditorViewModel viewModel) return;
        if (viewModel.Value.Value is not SolidColorBrush) return;
        if (viewModel.IsDisposed) return;

        viewModel.SetColor(e.OldValue.ToBtlColor(), e.NewValue.ToBtlColor());
    }

    private void OnColorChanged(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is not BrushEditorViewModel viewModel) return;
        if (viewModel.Value.Value is not SolidColorBrush solid) return;
        if (viewModel.IsDisposed) return;

        solid.Color.CurrentValue = e.NewValue.ToBtlColor();
        viewModel.InvalidateFrameCache();
    }

    private void OnGradientStopAdded(object? sender, (int Index, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.InsertGradientStop(e.Index, e.Object.ToBtlGradientStop());
    }

    private void OnGradientStopDeleted(object? sender, (int Index, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        viewModel.RemoveGradientStop(e.Index);
    }

    private void OnGradientStopConfirmed(
        object? sender,
        (int OldIndex, int NewIndex,
            Avalonia.Media.GradientStop Object, Avalonia.Media.Immutable.ImmutableGradientStop OldObject) e)
    {
        if (DataContext is not BrushEditorViewModel viewModel) return;
        if (viewModel.Value.Value is not GradientBrush { GradientStops: { } list }) return;
        if (viewModel.IsDisposed) return;

        if (e.NewIndex != e.OldIndex)
            list.Move(e.NewIndex, e.OldIndex);
        GradientStop obj = list[e.OldIndex];
        viewModel.ConfirmeGradientStop(e.OldIndex, e.NewIndex, e.OldObject.ToBtlImmutableGradientStop(), obj);
    }

    private void OnGradientStopChanged(object? sender,
        (int OldIndex, int NewIndex, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is not BrushEditorViewModel viewModel) return;
        if (viewModel.Value.Value is not GradientBrush { GradientStops: { } list }) return;
        if (viewModel.IsDisposed) return;

        GradientStop obj = list[e.OldIndex];
        obj.Offset.CurrentValue = (float)e.Object.Offset;
        obj.Color.CurrentValue = e.Object.Color.ToMedia();
        if (e.NewIndex != e.OldIndex)
            list.Move(e.OldIndex, e.NewIndex);

        viewModel.InvalidateFrameCache();
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextFlyout?.ShowAt(button);
        }
    }

    private void ChangeBrushType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;
        if (sender is not RadioMenuFlyoutItem { Tag: string tag }) return;

        if (tag is "PerlinNoise")
        {
            viewModel.SetValue(viewModel.Value.Value, new PerlinNoiseBrush());
        }
        else
        {
            OnBrushTypeChanged(null, tag switch
            {
                "Solid" => BrushType.SolidColorBrush,
                "LinearGradient" => BrushType.LinearGradientBrush,
                "ConicGradient" => BrushType.ConicGradientBrush,
                "RadialGradient" => BrushType.RadialGradientBrush,
                "Drawable" => BrushType.DrawableBrush,
                "Presenter" => BrushType.Presenter,
                _ => BrushType.Null
            });
        }

        expandToggle.IsChecked = true;
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } vm) return;
        if (_flyoutOpen) return;

        try
        {
            _flyoutOpen = true;
            await TargetSelectionHelper.HandleSelectTargetRequestAsync<BrushEditorViewModel, Brush>(
                this,
                vm,
                vm => vm.GetAvailableTargets(),
                (vm, target) => vm.SetTarget(target));
        }
        finally
        {
            _flyoutOpen = false;
        }
    }
}
