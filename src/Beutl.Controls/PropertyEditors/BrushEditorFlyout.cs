using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls.Primitives;
using FluentAvalonia.UI.Media;

#nullable enable

namespace Beutl.Controls.PropertyEditors;

public enum BrushType
{
    SolidColorBrush,

    LinearGradientBrush,

    ConicGradientBrush,

    RadialGradientBrush,

    Null,

    DrawableBrush
}

public sealed class BrushEditorFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<Brush?> BrushProperty =
        AvaloniaProperty.Register<BrushEditorFlyout, Brush?>(nameof(Brush));

    public static readonly StyledProperty<Media.IBrush?> OriginalBrushProperty =
        AvaloniaProperty.Register<BrushEditorFlyout, Media.IBrush?>(nameof(OriginalBrush));

    public static readonly StyledProperty<string?> DrawableNameProperty =
        AvaloniaProperty.Register<BrushEditorFlyout, string?>(nameof(DrawableName));

    public static readonly StyledProperty<bool> CanEditDrawableProperty =
        AvaloniaProperty.Register<BrushEditorFlyout, bool>(nameof(CanEditDrawable), false);

    public Brush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public Media.IBrush? OriginalBrush
    {
        get => GetValue(OriginalBrushProperty);
        set => SetValue(OriginalBrushProperty, value);
    }

    public string? DrawableName
    {
        get => GetValue(DrawableNameProperty);
        set => SetValue(DrawableNameProperty, value);
    }

    public bool CanEditDrawable
    {
        get => GetValue(CanEditDrawableProperty);
        set => SetValue(CanEditDrawableProperty, value);
    }

    // ドラッグ操作中または、Colorプロパティの変更
    public event EventHandler<(int OldIndex, int NewIndex, GradientStop Object)>? GradientStopChanged;

    // ドラッグ操作完了または、Colorプロパティの確定
    public event EventHandler<(int OldIndex, int NewIndex, GradientStop Object, ImmutableGradientStop OldObject)>? GradientStopConfirmed;

    public event EventHandler<(int Index, GradientStop Object)>? GradientStopDeleted;

    public event EventHandler<(int Index, GradientStop Object)>? GradientStopAdded;

    public event EventHandler<(Color2 OldValue, Color2 NewValue)>? ColorChanged;

    public event EventHandler<(Color2 OldValue, Color2 NewValue)>? ColorConfirmed;

    public event EventHandler<BrushType>? BrushTypeChanged;

    protected override Control CreatePresenter()
    {
        var pfp = new BrushEditorFlyoutPresenter()
        {
            Content = new SimpleColorPicker(),
            Brush = Brush
            Brush = Brush,
            OriginalBrush = OriginalBrush,
        };
        pfp.CloseClicked += (_, _) => Hide();
        pfp.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                Hide();
            }
        };
        pfp.GradientStopChanged += (_, t) => GradientStopChanged?.Invoke(this, t);
        pfp.GradientStopConfirmed += (_, t) => GradientStopConfirmed?.Invoke(this, t);
        pfp.GradientStopDeleted += (_, t) => GradientStopDeleted?.Invoke(this, t);
        pfp.GradientStopAdded += (_, t) => GradientStopAdded?.Invoke(this, t);
        pfp.ColorChanged += (_, t) => ColorChanged?.Invoke(this, t);
        pfp.ColorConfirmed += (_, t) => ColorConfirmed?.Invoke(this, t);
        pfp.BrushTypeChanged += (_, t) => BrushTypeChanged?.Invoke(this, t);
        pfp.GetObservable(BrushEditorFlyoutPresenter.BrushProperty)
            .Subscribe(b => Brush = b);

        return pfp;
    }

    protected override void OnConfirmed()
    {
        Hide();
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);

        if (Popup.Child is BrushEditorFlyoutPresenter pfp)
        {
            pfp.ShowHideButtons = ShouldShowConfirmationButtons();
            pfp.Brush = Brush;
            pfp.OriginalBrush = OriginalBrush;
            pfp.DrawableName = DrawableName;
            pfp.CanEditDrawable = CanEditDrawable;
        }

        Popup.IsLightDismissEnabled = false;
    }

    protected override bool ShouldShowConfirmationButtons() => false;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Popup.Child is BrushEditorFlyoutPresenter pfp)
        {
            if (change.Property == BrushProperty)
            {
                pfp.Brush = Brush;
            }

            if (change.Property == OriginalBrushProperty)
            {
                pfp.OriginalBrush = OriginalBrush;
            }

            if (change.Property == DrawableNameProperty)
            {
                pfp.DrawableName = DrawableName;
            }

            if (change.Property == CanEditDrawableProperty)
            {
                pfp.CanEditDrawable = CanEditDrawable;
            }
        }
    }
}
