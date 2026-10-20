using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels;
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

    private BrushEditorFlyout? _flyout;
    private readonly LiveEditDrag _drag = new();
    private Media.DrawableBrush? _followedBrush;

    public BrushEditor()
    {
        InitializeComponent();
        ExpandTransitionHelper.Attach(expandToggle, content);
        FallbackObjectViewHelper.Attach(this, view => (content.Child as Panel)?.Children.Add(view));

        AddBrushTypeItems((FAMenuFlyout)ExpandMenuButton.ContextFlyout!);
        AddBrushTypeItems((FAMenuFlyout)ReferenceMenuButton.Flyout!);
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(
            this,
            (FAMenuFlyout)ExpandMenuButton.ContextFlyout!,
            (FAMenuFlyout)ReferenceMenuButton.Flyout!);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    // The type menu and the reference editor's menu offer the same brush types.
    private void AddBrushTypeItems(FAMenuFlyout flyout)
    {
        var items = new (string Tag, string IsChecked, string Text)[]
        {
            ("Solid", "IsSolid.Value", GraphicsStrings.SolidColorBrush),
            ("LinearGradient", "IsLinearGradient.Value", GraphicsStrings.LinearGradientBrush),
            ("ConicGradient", "IsConicGradient.Value", GraphicsStrings.ConicGradientBrush),
            ("RadialGradient", "IsRadialGradient.Value", GraphicsStrings.RadialGradientBrush),
            ("PerlinNoise", "IsPerlinNoise.Value", GraphicsStrings.PerlinNoiseBrush),
            ("Drawable", "IsDrawable.Value", GraphicsStrings.Drawable),
            ("Presenter", "IsPresenter.Value", GraphicsStrings.Presenter),
            ("Null", "!Value.Value", Strings.Null)
        };
        foreach ((string tag, string isChecked, string text) in items)
        {
            var item = new FARadioMenuFlyoutItem
            {
                Tag = tag,
                Text = text,
                [!FARadioMenuFlyoutItem.IsCheckedProperty] = new ReflectionBinding(isChecked) { Mode = BindingMode.OneWay }
            };
            item.Click += ChangeBrushType;
            flyout.Items.Add(item);
        }
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        if (EditorDragDropHelper.TryApplyDroppedTemplate(e, viewModel))
        {
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        EditorDragDropHelper.HandleTemplateFileDragOver(e);
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
            if (_flyout.IsOpen)
                FollowDrawable(OriginalBrush as Media.DrawableBrush);
        }
    }

    // Follows the drawable of the brush the open flyout shows, so that its name stays current. Following
    // only while the flyout is open keeps the brush from holding on to an editor that was removed.
    private void FollowDrawable(Media.DrawableBrush? brush)
    {
        if (_followedBrush != null)
            _followedBrush.Drawable.ValueChanged -= OnDrawableBrushPropertyChanged;
        _followedBrush = brush;
        if (brush != null)
            brush.Drawable.ValueChanged += OnDrawableBrushPropertyChanged;
    }

    private void OnDrawableBrushPropertyChanged(object? sender, PropertyValueChangedEventArgs<Drawable?> e)
    {
        if (_flyout == null) return;

        _flyout.DrawableName = GetDrawableName(e.NewValue);
        _flyout.CanEditDrawable = e.NewValue is not null;
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

    private Task<object?> SelectTypeOrReference()
    {
        return PickerGuard.RunAsync(this, () =>
        {
            var selectVm = new SelectLibraryItemDialogViewModel(KnownLibraryItemFormats.Drawable, typeof(Drawable));

            if (DataContext is BrushEditorViewModel { IsDisposed: false } vm)
            {
                var targets = vm.GetAvailableDrawableTargets();
                selectVm.InitializeReferences(targets);
            }

            return LibraryItemPickerHelper.ShowAsync(this, selectVm, KnownLibraryItemFormats.Drawable);
        });
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
            _flyout.Closed += (_, _) => FollowDrawable(null);
        }

        _flyout.Brush = Brush;
        _flyout.OriginalBrush = OriginalBrush;
        _flyout.DrawableName = GetDrawableName((OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue);
        _flyout.CanEditDrawable = (OriginalBrush as Media.DrawableBrush)?.Drawable.CurrentValue is not null;

        _flyout.ShowAt(this, true);
        FollowDrawable(OriginalBrush as Media.DrawableBrush);
    }

    private void OnEditDrawableClicked(object? sender, EventArgs e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.Value.Value is not DrawableBrush { Drawable.CurrentValue: { } drawable }) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyTabViewModel.Open(editViewModel, drawable, viewModel);
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
            case Drawable target:
                viewModel.SetDrawableTarget(target);
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
            GradientBrush brush = CreateGradientBrush(e, viewModel.Value.Value);

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

    // Keeps the stops of the current gradient, or starts from white to black.
    private static GradientBrush CreateGradientBrush(BrushType e, Media.Brush? current)
    {
        var gradStops = new List<GradientStop>();
        if (current is GradientBrush oldBrush)
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
        return brush;
    }

    private void OnColorConfirmed(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is not BrushEditorViewModel viewModel) return;
        if (viewModel.Value.Value is not SolidColorBrush) return;
        if (viewModel.IsDisposed) return;

        // A drag's session holds its writes; SetColor then finds the value in place and commits nothing more.
        _drag.Confirm();
        viewModel.SetColor(e.OldValue.ToBtlColor(), e.NewValue.ToBtlColor());
    }

    private void OnColorChanged(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is not BrushEditorViewModel { IsElementEditable: true } viewModel) return;
        if (viewModel.Value.Value is not SolidColorBrush solid) return;
        if (viewModel.IsDisposed) return;

        _drag.Write(viewModel, DragOwner, StopFlyoutDrag, () => solid.Color.CurrentValue = e.NewValue.ToBtlColor());
        viewModel.InvalidateFrameCache();
    }

    private Control? FlyoutPresenter => _flyout?.Popup.Child;

    // Only a drag in the flyout opens a session; typed values are confirmed as before.
    private Control? DragOwner => FlyoutPresenter is BrushEditorFlyoutPresenter { IsDragging: true } presenter ? presenter : null;

    private void StopFlyoutDrag() => (FlyoutPresenter as BrushEditorFlyoutPresenter)?.StopDrag();

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
        if (DataContext is BrushEditorViewModel { IsElementEditable: true, Value.Value: GradientBrush { GradientStops: { } list } } viewModel
            && !_drag.Confirm())
            GradientStopEdits.Release(viewModel, list, e.OldIndex, e.NewIndex);
    }

    private void OnGradientStopChanged(object? sender,
        (int OldIndex, int NewIndex, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is BrushEditorViewModel { IsElementEditable: true, Value.Value: GradientBrush { GradientStops: { } list } } viewModel)
            _drag.Write(viewModel, DragOwner, StopFlyoutDrag, () => GradientStopEdits.Drag(viewModel, list, e.OldIndex, e.NewIndex, e.Object));
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
        if (sender is not FARadioMenuFlyoutItem { Tag: string tag }) return;

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

        await TargetSelectionHelper.HandleSelectTargetRequestAsync<BrushEditorViewModel, Brush>(
            this,
            vm,
            vm => vm.GetAvailableTargets(),
            (vm, target) => vm.SetTarget(target));
    }
}
