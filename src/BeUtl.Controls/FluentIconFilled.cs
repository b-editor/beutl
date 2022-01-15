using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BeUtl.Controls;

public sealed class FluentIconFilled : PathIcon, IStyleable
{
    public static readonly StyledProperty<FluentIconsFilled> IconTypeProperty =
        AvaloniaProperty.Register<FluentIconFilled, FluentIconsFilled>("IconType");

    public FluentIconFilled()
    {
        this.GetObservable(IconTypeProperty).Subscribe(type =>
        {
            if (Application.Current.FindResource($"{type}_Filled") is Geometry geometry)
            {
                Data = geometry;
            }
        });
    }

    Type IStyleable.StyleKey => typeof(PathIcon);

    public FluentIconsFilled IconType
    {
        get => GetValue(IconTypeProperty);
        set => SetValue(IconTypeProperty, value);
    }
}
