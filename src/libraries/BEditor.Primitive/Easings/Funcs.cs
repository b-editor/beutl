// Funcs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.CompilerServices;

namespace BEditor.Primitive.Easings
{
    internal static class Funcs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuadIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (max * t * t) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuadOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (-max * t * (t - 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuadInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            if (t < 1) return (max / 2 * t * t) + min;

            t--;
            return (-max / 2 * ((t * (t - 2)) - 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubicIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (max * t * t * t) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubicOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t = (t / totaltime) - 1;
            return (max * ((t * t * t) + 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubicInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            if (t < 1) return (max / 2 * t * t * t) + min;

            t -= 2;
            return (max / 2 * ((t * t * t) + 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuartIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (max * t * t * t * t) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuartOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t = (t / totaltime) - 1;
            return (-max * ((t * t * t * t) - 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuartInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            if (t < 1) return (max / 2 * t * t * t * t) + min;

            t -= 2;
            return (-max / 2 * ((t * t * t * t) - 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuintIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (max * t * t * t * t * t) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuintOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t = (t / totaltime) - 1;
            return (max * ((t * t * t * t * t) + 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuintInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            if (t < 1) return (max / 2 * t * t * t * t * t) + min;

            t -= 2;
            return (max / 2 * ((t * t * t * t * t) + 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SineIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            return (-max * MathF.Cos(t * (MathF.PI * 90 / 180) / totaltime)) + max + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SineOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            return (max * MathF.Sin(t * (MathF.PI * 90 / 180) / totaltime)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SineInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            return (-max / 2 * (MathF.Cos(t * MathF.PI / totaltime) - 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExpIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            return t == 0.0 ? min : (max * MathF.Pow(2, 10 * ((t / totaltime) - 1))) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExpOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            return t == totaltime ? max + min : (max * (-MathF.Pow(2, -10 * t / totaltime) + 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ExpInOut(float t, float totaltime, float min, float max)
        {
            if (t == 0.0f) return min;
            if (t == totaltime) return max;
            max -= min;
            t /= totaltime / 2;

            if (t < 1) return (max / 2 * MathF.Pow(2, 10 * (t - 1))) + min;

            t--;
            return (max / 2 * (-MathF.Pow(2, -10 * t) + 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CircIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            return (-max * (MathF.Sqrt(1 - (t * t)) - 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CircOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t = (t / totaltime) - 1;
            return (max * MathF.Sqrt(1 - (t * t))) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CircInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            if (t < 1) return (-max / 2 * (MathF.Sqrt(1 - (t * t)) - 1)) + min;

            t -= 2;
            return (max / 2 * (MathF.Sqrt(1 - (t * t)) + 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ElasticIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            var p = totaltime * 0.3f;
            var a = max;

            if (t == 0) return min;
            if (t == 1) return min + max;

            float s;
            if (a < MathF.Abs(max))
            {
                a = max;
                s = p / 4;
            }
            else
            {
                s = p / (2 * MathF.PI) * MathF.Asin(max / a);
            }

            t--;
            return -(a * MathF.Pow(2, 10 * t) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ElasticOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;
            var p = totaltime * 0.3f;
            var a = max;

            if (t == 0) return min;
            if (t == 1) return min + max;

            float s;
            if (a < MathF.Abs(max))
            {
                a = max;
                s = p / 4;
            }
            else
            {
                s = p / (2 * MathF.PI) * MathF.Asin(max / a);
            }

            return (a * MathF.Pow(2, -10 * t) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p)) + max + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ElasticInOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime / 2;
            var p = totaltime * (0.3f * 1.5f);
            var a = max;

            if (t == 0) return min;
            if (t == 2) return min + max;

            float s;
            if (a < MathF.Abs(max))
            {
                a = max;
                s = p / 4;
            }
            else
            {
                s = p / (2 * MathF.PI) * MathF.Asin(max / a);
            }

            if (t < 1)
            {
                return (-0.5f * (a * MathF.Pow(2, 10 * (--t)) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p))) + min;
            }

            t--;
            return (a * MathF.Pow(2, -10 * t) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p) * 0.5f) + max + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BackIn(float t, float totaltime, float min, float max)
        {
            var val = max - min;
            var s = (float)(val * 0.01);

            max -= min;
            t /= totaltime;
            return (max * t * t * (((s + 1) * t) - s)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BackOut(float t, float totaltime, float min, float max)
        {
            var val = max - min;
            var s = (float)(val * 0.001);

            max -= min;
            t = (t / totaltime) - 1;
            return (max * ((t * t * (((s + 1) * t) + s)) + 1)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BackInOut(float t, float totaltime, float min, float max)
        {
            var val = max - min;
            var s = (float)(val * 0.01);

            max -= min;
            s *= 1.525f;
            t /= totaltime / 2;
            if (t < 1) return (max / 2 * (t * t * (((s + 1) * t) - s))) + min;

            t -= 2;
            return (max / 2 * ((t * t * (((s + 1) * t) + s)) + 2)) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BounceIn(float t, float totaltime, float min, float max)
        {
            max -= min;
            return max - BounceOut(totaltime - t, totaltime, 0, max) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BounceOut(float t, float totaltime, float min, float max)
        {
            max -= min;
            t /= totaltime;

            if (t < 1.0f / 2.75f)
            {
                return (max * (7.5625f * t * t)) + min;
            }
            else if (t < 2.0f / 2.75f)
            {
                t -= 1.5f / 2.75f;
                return (max * ((7.5625f * t * t) + 0.75f)) + min;
            }
            else if (t < 2.5f / 2.75f)
            {
                t -= 2.25f / 2.75f;
                return (max * ((7.5625f * t * t) + 0.9375f)) + min;
            }
            else
            {
                t -= 2.625f / 2.75f;
                return (max * ((7.5625f * t * t) + 0.984375f)) + min;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BounceInOut(float t, float totaltime, float min, float max)
        {
            if (t < totaltime / 2)
            {
                return (BounceIn(t * 2, totaltime, 0, max - min) * 0.5f) + min;
            }
            else
            {
                return (BounceOut((t * 2) - totaltime, totaltime, 0, max - min) * 0.5f) + min + ((max - min) * 0.5f);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Linear(float t, float totaltime, float min, float max)
        {
            return ((max - min) * t / totaltime) + min;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float None(float min)
        {
            return min;
        }
    }
}
