using System.Runtime.CompilerServices;

namespace BEditorNext.Graphics;

internal class Helper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Near(int size, float x)
    {
        return Math.Min((int)(x + 0.5), size - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Set255(double value)
    {
        return value switch
        {
            > 255 => 255,
            < 0 => 0,
            _ => value,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Set255(float value)
    {
        return value switch
        {
            > 255 => 255,
            < 0 => 0,
            _ => value,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Set255Round(double value)
    {
        return value switch
        {
            > 255 => 255,
            < 0 => 0,
            _ => Math.Round(value),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Set255Round(float value)
    {
        return value switch
        {
            > 255 => 255,
            < 0 => 0,
            _ => MathF.Round(value),
        };
    }
}
