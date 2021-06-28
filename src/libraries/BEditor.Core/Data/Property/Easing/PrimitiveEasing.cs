// PrimitiveEasing.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;

using BEditor.Media;

namespace BEditor.Data.Property.Easing
{
    /// <summary>
    /// Represents a standard <see cref="EasingFunc"/>.
    /// </summary>
    public sealed class PrimitiveEasing : EasingFunc
    {
        /// <summary>
        /// Defines the <see cref="EasingType"/> property.
        /// </summary>
        public static readonly DirectProperty<PrimitiveEasing, SelectorProperty> EasingTypeProperty;

        private static readonly SelectorPropertyMetadata _easingTypeMetadata = new("EasingType", new[]
        {
            "None",
            "Linear",
            "SineIn",    "SineOut",    "SineInOut",
            "QuadIn",    "QuadOut",    "QuadInOut",
            "CubicIn",   "CubicOut",   "CubicInOut",
            "QuartIn",   "QuartOut",   "QuartInOut",
            "QuintIn",   "QuintOut",   "QuintInOut",
            "ExpIn",     "ExpOut",     "ExpInOut",
            "CircIn",    "CircOut",    "CircInOut",
            "BackIn",    "BackOut",    "BackInOut",
            "ElasticIn", "ElasticOut", "ElasticInOut",
            "BounceIn",  "BounceOut",  "BounceInOut",
        });

        private static readonly Func<float, float, float, float, float>[] _defaultEase =
        {
            Easing.Instance.None,

            Easing.Instance.Linear,

            Easing.Instance.SineIn,     Easing.Instance.SineOut,    Easing.Instance.SineInOut,

            Easing.Instance.QuadIn,     Easing.Instance.QuadOut,    Easing.Instance.QuadInOut,

            Easing.Instance.CubicIn,    Easing.Instance.CubicOut,   Easing.Instance.CubicInOut,

            Easing.Instance.QuartIn,    Easing.Instance.QuartOut,   Easing.Instance.QuartInOut,

            Easing.Instance.QuintIn,    Easing.Instance.QuintOut,   Easing.Instance.QuintInOut,

            Easing.Instance.ExpIn,      Easing.Instance.ExpOut,     Easing.Instance.ExpInOut,

            Easing.Instance.CircIn,     Easing.Instance.CircOut,    Easing.Instance.CircInOut,

            Easing.Instance.BackIn,     Easing.Instance.BackOut,    Easing.Instance.BackInOut,

            Easing.Instance.ElasticIn,  Easing.Instance.ElasticOut, Easing.Instance.ElasticInOut,

            Easing.Instance.BounceIn,   Easing.Instance.BounceOut,  Easing.Instance.BounceInOut,
        };

        private Func<float, float, float, float, float> _currentFunc = Easing.Instance.None;

        private IDisposable? _disposable;

        static PrimitiveEasing()
        {
            EasingTypeProperty = EditingProperty.RegisterDirect<SelectorProperty, PrimitiveEasing>(
                nameof(EasingType),
                owner => owner.EasingType,
                (owner, obj) => owner.EasingType = obj,
                EditingPropertyOptions<SelectorProperty>.Create(_easingTypeMetadata).Serialize());
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimitiveEasing"/> class.
        /// </summary>
        public PrimitiveEasing()
        {
        }

        /// <summary>
        /// Gets the <see cref="SelectorProperty"/> to select the easing function.
        /// </summary>
        [AllowNull]
        public SelectorProperty EasingType { get; private set; }

        /// <inheritdoc/>
        public override float EaseFunc(Frame frame, Frame totalframe, float min, float max) =>
            _currentFunc?.Invoke(frame, totalframe, min, max) ?? 0;

        /// <inheritdoc/>
        public override IEnumerable<IEasingProperty> GetProperties()
        {
            yield return EasingType;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _currentFunc = _defaultEase[EasingType.Index];

            _disposable = EasingType.Subscribe(index => _currentFunc = _defaultEase[index]);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _disposable?.Dispose();
        }

#pragma warning disable RCS1176, RCS1010, RCS0056, RCS1163, RCS1089, CA1822, IDE0060
        private class Easing
        {
            public static Easing Instance = new();

            public float QuadIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (max * t * t) + min;
            }

            public float QuadOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (-max * t * (t - 2)) + min;
            }

            public float QuadInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return (max / 2 * t * t) + min;

                t -= 1;
                return (-max / 2 * ((t * (t - 2)) - 1)) + min;
            }

            public float CubicIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (max * t * t * t) + min;
            }

            public float CubicOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = (t / totaltime) - 1;
                return (max * ((t * t * t) + 1)) + min;
            }

            public float CubicInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return (max / 2 * t * t * t) + min;

                t -= 2;
                return (max / 2 * ((t * t * t) + 2)) + min;
            }

            public float QuartIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (max * t * t * t * t) + min;
            }

            public float QuartOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = (t / totaltime) - 1;
                return (-max * ((t * t * t * t) - 1)) + min;
            }

            public float QuartInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return (max / 2 * t * t * t * t) + min;

                t -= 2;
                return (-max / 2 * ((t * t * t * t) - 2)) + min;
            }

            public float QuintIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (max * t * t * t * t * t) + min;
            }

            public float QuintOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = (t / totaltime) - 1;
                return (max * ((t * t * t * t * t) + 1)) + min;
            }

            public float QuintInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return (max / 2 * t * t * t * t * t) + min;

                t -= 2;
                return (max / 2 * ((t * t * t * t * t) + 2)) + min;
            }

            public float SineIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return (-max * MathF.Cos(t * (MathF.PI * 90 / 180) / totaltime)) + max + min;
            }

            public float SineOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return (max * MathF.Sin(t * (MathF.PI * 90 / 180) / totaltime)) + min;
            }

            public float SineInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return (-max / 2 * (MathF.Cos(t * MathF.PI / totaltime) - 1)) + min;
            }

            public float ExpIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return t == 0.0 ? min : (max * MathF.Pow(2, 10 * ((t / totaltime) - 1))) + min;
            }

            public float ExpOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return t == totaltime ? max + min : (max * (-MathF.Pow(2, -10 * t / totaltime) + 1)) + min;
            }

            public float ExpInOut(float t, float totaltime, float min, float max)
            {
                if (t == 0.0f) return min;
                if (t == totaltime) return max;
                max -= min;
                t /= totaltime / 2;

                if (t < 1) return (max / 2 * MathF.Pow(2, 10 * (t - 1))) + min;

                t -= 1;
                return (max / 2 * (-MathF.Pow(2, -10 * t) + 2)) + min;
            }

            public float CircIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return (-max * (MathF.Sqrt(1 - (t * t)) - 1)) + min;
            }

            public float CircOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = (t / totaltime) - 1;
                return (max * MathF.Sqrt(1 - (t * t))) + min;
            }

            public float CircInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return (-max / 2 * (MathF.Sqrt(1 - (t * t)) - 1)) + min;

                t -= 2;
                return (max / 2 * (MathF.Sqrt(1 - (t * t)) + 1)) + min;
            }

            public float ElasticIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                float p = totaltime * 0.3f;
                float a = max;

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

                t -= 1;
                return -(a * MathF.Pow(2, 10 * t) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p)) + min;
            }

            public float ElasticOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                float p = totaltime * 0.3f;
                float a = max;

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

            public float ElasticInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                float p = totaltime * (0.3f * 1.5f);
                float a = max;

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
                    return (-0.5f * (a * MathF.Pow(2, 10 * (t -= 1)) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p))) + min;
                }

                t -= 1;
                return (a * MathF.Pow(2, -10 * t) * MathF.Sin(((t * totaltime) - s) * (2 * MathF.PI) / p) * 0.5f) + max + min;
            }

            public float BackIn(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.01);

                max -= min;
                t /= totaltime;
                return (max * t * t * (((s + 1) * t) - s)) + min;
            }

            public float BackOut(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.001);

                max -= min;
                t = (t / totaltime) - 1;
                return (max * ((t * t * (((s + 1) * t) + s)) + 1)) + min;
            }

            public float BackInOut(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.01);

                max -= min;
                s *= 1.525f;
                t /= totaltime / 2;
                if (t < 1) return (max / 2 * (t * t * (((s + 1) * t) - s))) + min;

                t -= 2;
                return (max / 2 * ((t * t * (((s + 1) * t) + s)) + 2)) + min;
            }

            public float BounceIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return max - BounceOut(totaltime - t, totaltime, 0, max) + min;
            }

            public float BounceOut(float t, float totaltime, float min, float max)
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

            public float BounceInOut(float t, float totaltime, float min, float max)
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

            public float Linear(float t, float totaltime, float min, float max)
            {
                return ((max - min) * t / totaltime) + min;
            }

            public float None(float t, float totaltime, float min, float max)
            {
                return min;
            }
        }
#pragma warning restore RCS1176, RCS1010, RCS0056, RCS1163, RCS1089, CA1822, IDE0060
    }
}