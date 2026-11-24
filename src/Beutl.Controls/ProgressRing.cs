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
public partial class ProgressRing : RangeBase
{
    public static readonly StyledProperty<double> ValueAngleProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(ValueAngle), 0);

    static ProgressRing()
    {
        // Redraws the arc for the new value instead of writing the value back, which could replace a binding.
        Action<ProgressRing, AvaloniaPropertyChangedEventArgs> updateValueAngle = (sender, _) => sender.UpdateValueAngle();
        MinimumProperty.Changed.AddClassHandler<ProgressRing>(updateValueAngle);
        MaximumProperty.Changed.AddClassHandler<ProgressRing>(updateValueAngle);
        ValueProperty.Changed.AddClassHandler<ProgressRing>(updateValueAngle);
        StartAngleProperty.Changed.AddClassHandler<ProgressRing>(updateValueAngle);
        EndAngleProperty.Changed.AddClassHandler<ProgressRing>(updateValueAngle);
    }

    public ProgressRing()
    {
        UpdatePseudoClasses(IsIndeterminate, PreserveAspect);
    }

    [GeneratedStyledProperty(AddOwnerFrom = typeof(ProgressBar))]
    public partial bool IsIndeterminate { get; set; }

    [GeneratedStyledProperty(DefaultValue = true)]
    public partial bool PreserveAspect { get; set; }

    public double ValueAngle
    {
        get => GetValue(ValueAngleProperty);
        private set => SetValue(ValueAngleProperty, value);
    }

    [GeneratedStyledProperty(DefaultValue = 0)]
    public partial double StartAngle { get; set; }

    [GeneratedStyledProperty(DefaultValue = 360)]
    public partial double EndAngle { get; set; }

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

    // An empty range has no progress to show, so it draws no arc instead of a NaN sweep.
    private void UpdateValueAngle()
    {
        double range = Maximum - Minimum;
        ValueAngle = range == 0 ? 0 : (Value - Minimum) * (EndAngle - StartAngle) / range;
    }
}
