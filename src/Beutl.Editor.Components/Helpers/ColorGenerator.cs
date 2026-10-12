using System.Numerics;

using Avalonia.Media;

namespace Beutl.Editor.Components.Helpers;

public static class ColorGenerator
{
    // https://github.com/google/skia/blob/0d39172f35d259b6ab888974177bc4e6d839d44c/src/effects/SkHighContrastFilter.cpp
    // At full contrast the filter's inverted lightness saturates to white or black, so only the luminance byte decides.
    public static Color GetTextColor(Color color)
    {
        float grayscale = Vector3.Dot(
            new(0.2126f, 0.7152f, 0.0722f),
            new Vector3(color.R / 255f, color.G / 255f, color.B / 255f));
        return (byte)(grayscale * 255) < 128 ? Colors.White : Colors.Black;
    }
}
