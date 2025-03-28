﻿using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Editors;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed partial class BrushEditor : UserControl
{
    public static readonly StyledProperty<Avalonia.Media.Brush?> BrushProperty =
        AvaloniaProperty.Register<BrushEditor, Avalonia.Media.Brush?>(nameof(Brush));

    public static readonly StyledProperty<Media.IBrush?> OriginalBrushProperty =
        AvaloniaProperty.Register<BrushEditor, Media.IBrush?>(nameof(OriginalBrush));

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

    public Media.IBrush? OriginalBrush
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
            _flyout.DrawableName = GetDrawableName((OriginalBrush as Media.DrawableBrush)?.Drawable);
            _flyout.CanEditDrawable = (OriginalBrush as Media.DrawableBrush)?.Drawable is not null;

            if (change.OldValue is Media.DrawableBrush oldBrush)
            {
                oldBrush.PropertyChanged -= OnDrawableBrushPropertyChanged;
            }

            if (change.NewValue is Media.DrawableBrush newBrush)
            {
                newBrush.PropertyChanged += OnDrawableBrushPropertyChanged;
            }
        }
    }

    private void OnDrawableBrushPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_flyout == null) return;

        if (e is CorePropertyChangedEventArgs<Graphics.Drawable> e2 &&
            e2.Property.Id == Media.DrawableBrush.DrawableProperty.Id)
        {
            _flyout.DrawableName = GetDrawableName(e2.NewValue);
            _flyout.CanEditDrawable = e2.NewValue is not null;
        }
    }

    private static string GetDrawableName(Drawable? drawable)
    {
        if (drawable == null)
        {
            return Strings.CreateNew;
        }

        var type = drawable.GetType();
        return LibraryService.Current.FindItem(type)?.DisplayName ?? type.Name;
    }

    private async Task<Type?> SelectType()
    {
        if (_flyoutOpen) return null;

        try
        {
            _flyoutOpen = true;
            var viewModel = new SelectDrawableTypeViewModel();
            var dialog = new LibraryItemPickerFlyout(viewModel);
            dialog.ShowAt(this);
            var tcs = new TaskCompletionSource<Type?>();
            dialog.Pinned += (_, item) => viewModel.Pin(item);
            dialog.Unpinned += (_, item) => viewModel.Unpin(item);
            dialog.Dismissed += (_, _) => tcs.SetResult(null);
            dialog.Confirmed += (_, _) =>
            {
                switch (viewModel.SelectedItem.Value?.UserData)
                {
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
        _flyout.DrawableName = GetDrawableName((OriginalBrush as Media.DrawableBrush)?.Drawable);
        _flyout.CanEditDrawable = (OriginalBrush as Media.DrawableBrush)?.Drawable is not null;

        _flyout.ShowAt(this);
    }

    private void OnEditDrawableClicked(object? sender, EventArgs e)
    {
        // TODO: DrawablePropertyEditorを開く
        // ObjectPropertyEditorは不要なプロパティも表示されてしまうので
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;
        if (viewModel.Value.Value is not DrawableBrush { Drawable: { } drawable }) return;
        if (viewModel.GetService<EditViewModel>() is not { } editViewModel) return;

        ObjectPropertyEditorViewModel objViewModel
            = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
              ?? new ObjectPropertyEditorViewModel(editViewModel);

        objViewModel.NavigateCore(drawable, false, viewModel);
        editViewModel.OpenToolTab(objViewModel);
    }

    private async void OnChangeDrawableClicked(object? sender, Button e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        Type? type = await SelectType();
        if (type != null)
        {
            viewModel.ChangeDrawableType(type);
        }
    }

    private void OnBrushTypeChanged(object? sender, BrushType e)
    {
        if (DataContext is not BrushEditorViewModel { IsDisposed: false } viewModel) return;

        if (e == BrushType.SolidColorBrush)
        {
            viewModel.SetValue(viewModel.Value.Value, new SolidColorBrush() { Color = Colors.White });
        }
        else if (e == BrushType.Null)
        {
            viewModel.SetValue(viewModel.Value.Value, null);
        }
        else if (e is BrushType.ConicGradientBrush
                 or BrushType.LinearGradientBrush
                 or BrushType.RadialGradientBrush)
        {
            var gradStops = new GradientStops();
            if (viewModel.Value.Value is GradientBrush oldBrush)
            {
                gradStops.AddRange(oldBrush.GradientStops.Select(v => new GradientStop(v.Color, v.Offset)));
            }
            else
            {
                gradStops.Add(new GradientStop(Colors.White, 0));
                gradStops.Add(new GradientStop(Colors.Black, 1));
            }

            viewModel.SetValue(viewModel.Value.Value, e switch
            {
                BrushType.LinearGradientBrush => new LinearGradientBrush { GradientStops = gradStops },
                BrushType.ConicGradientBrush => new ConicGradientBrush { GradientStops = gradStops },
                BrushType.RadialGradientBrush => new RadialGradientBrush { GradientStops = gradStops },
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, null)
            });
        }
        else if (e == BrushType.DrawableBrush)
        {
            viewModel.SetValue(viewModel.Value.Value, new DrawableBrush());
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

        solid.Color = e.NewValue.ToBtlColor();
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
        obj.Offset = (float)e.Object.Offset;
        obj.Color = e.Object.Color.ToMedia();
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
                _ => BrushType.Null
            });
        }

        expandToggle.IsChecked = true;
    }
}
