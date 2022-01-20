using System.Runtime.CompilerServices;

namespace BeUtl.Animation.Easings;

internal static class Funcs
{
    public const float HalfPI = MathF.PI / 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackEaseIn(float p)
    {
        return p * (p * p - MathF.Sin(p * MathF.PI));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            var f = 2f * p;
            return 0.5f * f * (f * f - MathF.Sin(f * MathF.PI));
        }
        else
        {
            var f = 1f - (2f * p - 1f);
            return 0.5f * (1f - f * (f * f - MathF.Sin(f * MathF.PI))) + 0.5f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BackEaseOut(float p)
    {
        p = 1f - p;
        return 1 - p * (p * p - MathF.Sin(p * MathF.PI));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceEaseIn(float p)
    {
        return 1 - BounceEaseOut(1 - p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            return 0.5f * (1 - BounceEaseOut(1 - (p * 2)));
        }
        else
        {
            return 0.5f * BounceEaseOut(p * 2 - 1) + 0.5f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BounceEaseOut(float p)
    {
        if (p < 4f / 11.0f)
        {
            return (121f * p * p) / 16.0f;
        }
        else if (p < 8f / 11.0f)
        {
            return (363f / 40.0f * p * p) - (99f / 10.0f * p) + 17f / 5.0f;
        }
        else if (p < 9f / 10.0f)
        {
            return (4356f / 361.0f * p * p) - (35442f / 1805.0f * p) + 16061f / 1805.0f;
        }
        else
        {
            return (54f / 5.0f * p * p) - (513f / 25.0f * p) + 268f / 25.0f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircularEaseIn(float p)
    {
        return 1f - MathF.Sqrt(1f - p * p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircularEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            return 0.5f * (1f - MathF.Sqrt(1f - 4f * p * p));
        }
        else
        {
            var t = 2f * p;
            return 0.5f * (MathF.Sqrt((3f - t) * (t - 1f)) + 1f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CircularEaseOut(float p)
    {
        return MathF.Sqrt((2f - p) * p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicEaseIn(float p)
    {
        return p * p * p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            return 4f * p * p * p;
        }
        else
        {
            var f = 2f * (p - 1);
            return 0.5f * f * f * f + 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CubicEaseOut(float p)
    {
        var f = p - 1f;
        return f * f * f + 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ElasticEaseIn(float p)
    {
        return MathF.Sin(13f * HalfPI * p) * MathF.Pow(2f, 10f * (p - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ElasticEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            var t = 2f * p;
            return 0.5f * MathF.Sin(13f * HalfPI * t) * MathF.Pow(2f, 10f * (t - 1f));
        }
        else
        {
            return 0.5f * (MathF.Sin(-13f * HalfPI * ((2f * p - 1f) + 1f)) * MathF.Pow(2f, -10f * (2f * p - 1f)) + 2f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ElasticEaseOut(float p)
    {
        return MathF.Sin(-13f * HalfPI * (p + 1)) * MathF.Pow(2f, -10f * p) + 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExponentialEaseIn(float p)
    {
        return (p == 0.0f) ? p : MathF.Pow(2f, 10f * (p - 1f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExponentialEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            return 0.5f * MathF.Pow(2f, 20f * p - 10f);
        }
        else
        {
            return -0.5f * MathF.Pow(2f, -20f * p + 10f) + 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ExponentialEaseOut(float p)
    {
        return (p == 1.0f) ? p : 1f - MathF.Pow(2f, -10f * p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LinearEasing(float p)
    {
        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadraticEaseIn(float p)
    {
        return p * p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadraticEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            return 2f * p * p;
        }
        else
        {
            return p * (-2f * p + 4f) - 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuadraticEaseOut(float p)
    {
        return -(p * (p - 2f));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuarticEaseIn(float p)
    {
        var p2 = p * p;
        return p2 * p2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuarticEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            var p2 = p * p;
            return 8f * p2 * p2;
        }
        else
        {
            var f = p - 1f;
            var f2 = f * f;
            return -8f * f2 * f2 + 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuarticEaseOut(float p)
    {
        var f = p - 1f;
        var f2 = f * f;
        return -f2 * f2 + 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuinticEaseIn(float p)
    {
        var p2 = p * p;
        return p2 * p2 * p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuinticEaseInOut(float p)
    {
        if (p < 0.5f)
        {
            var p2 = p * p;
            return 16f * p2 * p2 * p;
        }
        else
        {
            var f = 2f * p - 2f;
            var f2 = f * f;
            return 0.5f * f2 * f2 * f + 1f;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float QuinticEaseOut(float p)
    {
        var f = p - 1f;
        var f2 = f * f;
        return f2 * f2 * f + 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineEaseIn(float p)
    {
        return MathF.Sin((p - 1) * HalfPI) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineEaseInOut(float p)
    {
        return 0.5f * (1f - MathF.Cos(p * MathF.PI));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SineEaseOut(float p)
    {
        return MathF.Sin(p * HalfPI);
    }
}