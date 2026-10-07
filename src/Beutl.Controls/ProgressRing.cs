// https://github.com/ymg2006/FluentAvalonia.ProgressRing/blob/49bf3a2287033decb36ed81f59f5e18cb9333f39/FluentAvalonia.ProgressRing/UI/Controls/ProgressRing/ProgressRing.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Beutl.Controls;

/// <summary>
/// A control used to indicate the progress of an operation.
/// </summary>
[PseudoClasses(":preserveaspect", ":indeterminate")]
public class ProgressRing : RangeBase
{
    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        ProgressBar.IsIndeterminateProperty.AddOwner<ProgressRing>();

    public static readonly StyledProperty<bool> PreserveAspectProperty =
        AvaloniaProperty.Register<ProgressRing, bool>(nameof(PreserveAspect), true);

    public static readonly StyledProperty<double> ValueAngleProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(ValueAngle), 0);

    public static readonly StyledProperty<double> StartAngleProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(StartAngle), 0);

    public static readonly StyledProperty<double> EndAngleProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(EndAngle), 360);

    static ProgressRing()
    {
        MinimumProperty.Changed.AddClassHandler<ProgressRing>(OnMinimumPropertyChanged);
        MaximumProperty.Changed.AddClassHandler<ProgressRing>(OnMaximumPropertyChanged);
        ValueProperty.Changed.AddClassHandler<ProgressRing>(OnValuePropertyChanged);
        StartAngleProperty.Changed.AddClassHandler<ProgressRing>(OnAnglePropertyChanged);
        EndAngleProperty.Changed.AddClassHandler<ProgressRing>(OnAnglePropertyChanged);
    }

    public ProgressRing()
    {
        UpdatePseudoClasses(IsIndeterminate, PreserveAspect);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public bool PreserveAspect
    {
        get => GetValue(PreserveAspectProperty);
        set => SetValue(PreserveAspectProperty, value);
    }

    public double ValueAngle
    {
        get => GetValue(ValueAngleProperty);
        private set => SetValue(ValueAngleProperty, value);
    }

    public double StartAngle
    {
        get => GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public double EndAngle
    {
        get => GetValue(EndAngleProperty);
        set => SetValue(EndAngleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change is not AvaloniaPropertyChangedEventArgs<bool> e) return;

        if (e.Property == IsIndeterminateProperty)
        {
            UpdatePseudoClasses(e.NewValue.GetValueOrDefault(), null);
        }
        else if (e.Property == PreserveAspectProperty)
        {
            UpdatePseudoClasses(null, e.NewValue.GetValueOrDefault());
        }
    }

    private void UpdatePseudoClasses(
        bool? isIndeterminate,
        bool? preserveAspect)
    {
        if (isIndeterminate.HasValue)
        {
            PseudoClasses.Set(":indeterminate", isIndeterminate.Value);
        }

        if (preserveAspect.HasValue)
        {
            PseudoClasses.Set(":preserveaspect", preserveAspect.Value);
        }
    }

    static void OnMinimumPropertyChanged(ProgressRing sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender.Minimum = e.GetNewValue<double>();
    }

    static void OnMaximumPropertyChanged(ProgressRing sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender.Maximum = e.GetNewValue<double>();
    }

    static void OnValuePropertyChanged(ProgressRing sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender.ValueAngle = (e.GetNewValue<double>() - sender.Minimum) * (sender.EndAngle - sender.StartAngle) / (sender.Maximum - sender.Minimum);
    }

    // Redraws the arc for the new angles instead of writing the angle back, which could replace a binding.
    static void OnAnglePropertyChanged(ProgressRing sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender.ValueAngle = (sender.Value - sender.Minimum) * (sender.EndAngle - sender.StartAngle) / (sender.Maximum - sender.Minimum);
    }
}
