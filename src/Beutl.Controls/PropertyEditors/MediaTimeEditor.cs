using Avalonia;
using Avalonia.Data;
using Avalonia.Input;

namespace Beutl.Controls.PropertyEditors;

// Shows and takes a time as TimeSpan text, as the editor of earlier versions did.
public class MediaTimeEditor : ValueStringEditor<MediaTime>
{
    public static readonly DirectProperty<MediaTimeEditor, MediaTime> ValueProperty =
        AvaloniaProperty.RegisterDirect<MediaTimeEditor, MediaTime>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public MediaTimeEditor()
        : base(default, canScrub: false)
    {
        Text = TimeSpan.Zero.ToString();
    }

    private protected override DirectPropertyBase<MediaTime> ValuePropertyCore => ValueProperty;

    private protected override string Format(MediaTime value)
    {
        return value.ToTimeSpan().ToString();
    }

    private protected override bool Differs(MediaTime x, MediaTime y) => x != y;

    private protected override bool TryParse(string? text, out MediaTime value)
    {
        value = default;
        if (!TimeSpan.TryParse(text, out TimeSpan span))
            return false;

        // A TimeSpan reaches past the range of MediaTime (about 414 years).
        try
        {
            value = MediaTime.FromTimeSpan(span);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private protected override MediaTime StepByWheel(MediaTime value, PointerWheelEventArgs e)
    {
        value = e.Delta.Y switch
        {
            < 0 => value - MediaTime.FromSeconds(60),
            > 0 => value + MediaTime.FromSeconds(60),
            _ => value
        };

        return e.Delta.X switch
        {
            < 0 => value - MediaTime.FromSeconds(10),
            > 0 => value + MediaTime.FromSeconds(10),
            _ => value
        };
    }
}
