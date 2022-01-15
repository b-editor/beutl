using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BeUtl.Controls;

public sealed class FluentIconRegular : PathIcon, IStyleable
{
    public static readonly StyledProperty<FluentIconsRegular> IconTypeProperty =
        AvaloniaProperty.Register<FluentIconRegular, FluentIconsRegular>("IconType");

    public FluentIconRegular()
    {
        this.GetObservable(IconTypeProperty).Subscribe(type =>
        {
            if (Application.Current.FindResource($"{type}_Regular") is Geometry geometry)
            {
                Data = geometry;
            }
        });
    }

    Type IStyleable.StyleKey => typeof(PathIcon);

    public FluentIconsRegular IconType
    {
        get => GetValue(IconTypeProperty);
        set => SetValue(IconTypeProperty, value);
    }
}
