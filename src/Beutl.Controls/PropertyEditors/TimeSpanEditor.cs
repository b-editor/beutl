using Avalonia;
using Avalonia.Data;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

internal class TimeSpanEditor : ValueStringEditor<TimeSpan>
{
    public static readonly DirectProperty<TimeSpanEditor, TimeSpan> ValueProperty =
        AvaloniaProperty.RegisterDirect<TimeSpanEditor, TimeSpan>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public TimeSpanEditor()
        : base(default, canScrub: false)
    {
        Text = TimeSpan.Zero.ToString();
    }

    private protected override DirectPropertyBase<TimeSpan> ValuePropertyCore => ValueProperty;

    private protected override string Format(TimeSpan value)
    {
        return value.ToString();
    }

    private protected override bool Differs(TimeSpan x, TimeSpan y) => x != y;

    private protected override bool TryParse(string? text, out TimeSpan value)
    {
        return TimeSpan.TryParse(text, out value);
    }

    private protected override TimeSpan StepByWheel(TimeSpan value, PointerWheelEventArgs e)
    {
        value = e.Delta.Y switch
        {
            < 0 => value.Subtract(TimeSpan.FromMinutes(1)),
            > 0 => value.Add(TimeSpan.FromMinutes(1)),
            _ => value
        };

        return e.Delta.X switch
        {
            < 0 => value.Subtract(TimeSpan.FromSeconds(10)),
            > 0 => value.Add(TimeSpan.FromSeconds(10)),
            _ => value
        };
    }
}
