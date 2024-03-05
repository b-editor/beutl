using System.Collections.Specialized;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using Beutl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public sealed partial class BrushEditor : UserControl
{
    public static readonly StyledProperty<Avalonia.Media.Brush?> BrushProperty =
        AvaloniaProperty.Register<BrushEditor, Avalonia.Media.Brush?>(nameof(Brush));

    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    private BrushEditorFlyout? _flyout;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BrushProperty && _flyout != null)
        {
            _flyout.Brush = Brush;
        }
    }

    private void OpenFlyout_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
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
            }

            _flyout.Brush = Brush;

            _flyout.ShowAt(this, true);
        }
    }

    private void OnBrushTypeChanged(object? sender, BrushType e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
            if (e == BrushType.SolidColorBrush)
            {
                viewModel.SetValue(viewModel.Value.Value, new SolidColorBrush() { Color = Colors.White });
            }
            else if (e == BrushType.Null)
            {
                viewModel.SetValue(viewModel.Value.Value, null);
            }
            else
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
                    BrushType.LinearGradientBrush => new LinearGradientBrush() { GradientStops = gradStops },
                    BrushType.ConicGradientBrush => new ConicGradientBrush() { GradientStops = gradStops },
                    BrushType.RadialGradientBrush => new RadialGradientBrush() { GradientStops = gradStops },
                    _ => null,
                });
            }
        }
    }

    private void OnColorConfirmed(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is BrushEditorViewModel { Value.Value: SolidColorBrush solid } viewModel)
        {
            CommandRecorder recorder = viewModel.GetRequiredService<CommandRecorder>();
            RecordableCommands.Edit(solid, SolidColorBrush.ColorProperty, e.NewValue.ToBtlColor(), e.OldValue.ToBtlColor())
                .DoAndRecord(recorder);
        }
    }

    private void OnColorChanged(object? sender, (Color2 OldValue, Color2 NewValue) e)
    {
        if (DataContext is BrushEditorViewModel { Value.Value: SolidColorBrush solid } viewModel)
        {
            solid.Color = e.NewValue.ToBtlColor();
        }
    }

    private void OnGradientStopAdded(object? sender, (int Index, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
            viewModel.InsertGradientStop(e.Index, e.Object.ToBtlGradientStop());
        }
    }

    private void OnGradientStopDeleted(object? sender, (int Index, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is BrushEditorViewModel viewModel)
        {
            viewModel.RemoveGradientStop(e.Index);
        }
    }

    private void OnGradientStopConfirmed(
        object? sender,
        (int OldIndex, int NewIndex, Avalonia.Media.GradientStop Object, Avalonia.Media.Immutable.ImmutableGradientStop OldObject) e)
    {
        if (DataContext is BrushEditorViewModel { Value.Value: GradientBrush { GradientStops: { } list } } viewModel)
        {
            if (e.NewIndex != e.OldIndex)
                list.Move(e.NewIndex, e.OldIndex);
            GradientStop obj = list[e.OldIndex];
            viewModel.ConfirmeGradientStop(e.OldIndex, e.NewIndex, e.OldObject.ToBtlImmutableGradientStop(), obj);
        }
    }

    private void OnGradientStopChanged(object? sender, (int OldIndex, int NewIndex, Avalonia.Media.GradientStop Object) e)
    {
        if (DataContext is BrushEditorViewModel { Value.Value: GradientBrush { GradientStops: { } list } })
        {
            GradientStop obj = list[e.OldIndex];
            obj.Offset = (float)e.Object.Offset;
            obj.Color = e.Object.Color.ToMedia();
            if (e.NewIndex != e.OldIndex)
                list.Move(e.OldIndex, e.NewIndex);
        }
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
        if (DataContext is BrushEditorViewModel viewModel
            && sender is RadioMenuFlyoutItem { Tag: string tag })
        {
            IBrush? newBrush = tag switch
            {
                "Solid" => new SolidColorBrush(),
                "LinearGradient" => new LinearGradientBrush(),
                "ConicGradient" => new ConicGradientBrush(),
                "RadialGradient" => new RadialGradientBrush(),
                "PerlinNoise" => new PerlinNoiseBrush(),
                _ => null
            };

            viewModel.SetValue(viewModel.Value.Value, newBrush);
            expandToggle.IsChecked = true;
        }
    }
}
