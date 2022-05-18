using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BeUtl.Controls;

public static class IconHelper
{
    public static Geometry GetGeometry(this FluentIconsFilled icon)
    {
        return Application.Current.FindResource($"{icon}_Filled") as Geometry;
    }

    public static Geometry GetGeometry(this FluentIconsRegular icon)
    {
        return Application.Current.FindResource($"{icon}_Regular") as Geometry;
    }
}
