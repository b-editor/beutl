using Avalonia.Data;
using Avalonia.Data.Converters;
using Beutl.Graphics3D.Gizmo;

namespace Beutl.Converters;

public static class GizmoModeConverters
{
    public static readonly IValueConverter IsTranslate = new GizmoModeConverter(GizmoMode.Translate);
    public static readonly IValueConverter IsRotate = new GizmoModeConverter(GizmoMode.Rotate);
    public static readonly IValueConverter IsScale = new GizmoModeConverter(GizmoMode.Scale);

    private sealed class GizmoModeConverter(GizmoMode targetMode) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is GizmoMode mode)
            {
                return mode == targetMode;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true)
            {
                return targetMode;
            }
            return BindingOperations.DoNothing;
        }
    }
}
