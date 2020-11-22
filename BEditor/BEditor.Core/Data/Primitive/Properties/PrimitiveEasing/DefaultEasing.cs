using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Primitive.Properties.PrimitiveEasing
{

    /// <summary>
    /// 標準のイージング
    /// <para>32種類</para>
    /// <list type="table">
    /// <item>
    /// <description>None</description>
    /// </item>
    /// <item>
    /// <description>Linear</description>
    /// </item>
    /// <item>
    /// <description>SineIn</description>
    /// <description>SineOut</description>
    /// <description>SineInOut</description>
    /// </item>
    /// <item>
    /// <description>QuadIn</description>
    /// <description>QuadOut</description>
    /// <description>QuadInOut</description>
    /// </item>
    /// <item>
    /// <description>CubicIn</description>
    /// <description>CubicOut</description>
    /// <description>CubicInOut</description>
    /// </item>
    /// <item>
    /// <description>QuartIn</description>
    /// <description>QuartOut</description>
    /// <description>QuartInOut</description>
    /// </item>
    /// <item>
    /// <description>QuintIn</description>
    /// <description>QuintOut</description>
    /// <description>QuintInOut</description>
    /// </item>
    /// <item>
    /// <description>ExpIn</description>
    /// <description>ExpOut</description>
    /// <description>ExpInOut</description>
    /// </item>
    /// <item>
    /// <description>CircIn</description>
    /// <description>CircOut</description>
    /// <description>CircInOut</description>
    /// </item>
    /// <item>
    /// <description>BackIn</description>
    /// <description>BackOut</description>
    /// <description>BackInOut</description>
    /// </item>
    /// <item>
    /// <description>ElasticIn</description>
    /// <description>ElasticOut</description>
    /// <description>ElasticInOut</description>
    /// </item>
    /// <item>
    /// <description>BounceIn</description>
    /// <description>BounceOut</description>
    /// <description>BounceInOut</description>
    /// </item>
    /// </list>
    /// </summary>
    [DataContract(Namespace = "")]
    public sealed class DefaultEasing : EasingFunc
    {
        public static readonly SelectorPropertyMetadata propertyMetadata = new SelectorPropertyMetadata("EasingType", new string[32] {
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
            "BounceIn",  "BounceOut",  "BounceInOut"
        });

        #region EasingFunc

        public override IEnumerable<IEasingProperty> Properties => new IEasingProperty[]
        {
            EasingType
        };

        public override float EaseFunc(int frame, int totalframe, float min, float max) => currentfunc?.Invoke(frame, totalframe, min, max) ?? 0;

        private Func<float, float, float, float, float> currentfunc = Easing.None;

        public override void PropertyLoaded()
        {
            EasingType.ExecuteLoaded(propertyMetadata);

            currentfunc = DefaultEase[EasingType.Index];

            EasingType.PropertyChanged += EasingType_PropertyChanged;
        }

        private void EasingType_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectorProperty.Index))
            {
                currentfunc = DefaultEase[EasingType.Index];
            }
        }

        #endregion

        #region コンストラクタ
        public DefaultEasing()
        {
            EasingType = new SelectorProperty(propertyMetadata);
        }
        #endregion


        [DataMember()]
        public SelectorProperty EasingType { get; set; }


        public static readonly Func<float, float, float, float, float>[] DefaultEase = new Func<float, float, float, float, float>[] {
            Easing.None,

            Easing.Linear,

            Easing.SineIn,     Easing.SineOut,    Easing.SineInOut,

            Easing.QuadIn,     Easing.QuadOut,    Easing.QuadInOut,

            Easing.CubicIn,    Easing.CubicOut,   Easing.CubicInOut,

            Easing.QuartIn,    Easing.QuartOut,   Easing.QuartInOut,

            Easing.QuintIn,    Easing.QuintOut,   Easing.QuintInOut,

            Easing.ExpIn,      Easing.ExpOut,     Easing.ExpInOut,

            Easing.CircIn,     Easing.CircOut,    Easing.CircInOut,

            Easing.BackIn,     Easing.BackOut,    Easing.BackInOut,

            Easing.ElasticIn,  Easing.ElasticOut, Easing.ElasticInOut,

            Easing.BounceIn,   Easing.BounceOut,  Easing.BounceInOut,
        };

        class Easing
        {
            public static float QuadIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return max * t * t + min;
            }

            public static float QuadOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return -max * t * (t - 2) + min;
            }

            public static float QuadInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t + min;

                t -= 1;
                return -max / 2 * (t * (t - 2) - 1) + min;
            }

            public static float CubicIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return max * t * t * t + min;
            }

            public static float CubicOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = t / totaltime - 1;
                return max * (t * t * t + 1) + min;
            }

            public static float CubicInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t + min;

                t -= 2;
                return max / 2 * (t * t * t + 2) + min;
            }

            public static float QuartIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return max * t * t * t * t + min;
            }

            public static float QuartOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = t / totaltime - 1;
                return -max * (t * t * t * t - 1) + min;
            }

            public static float QuartInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t * t + min;

                t -= 2;
                return -max / 2 * (t * t * t * t - 2) + min;
            }

            public static float QuintIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return max * t * t * t * t * t + min;
            }

            public static float QuintOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = t / totaltime - 1;
                return max * (t * t * t * t * t + 1) + min;
            }

            public static float QuintInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * t * t * t * t * t + min;

                t -= 2;
                return max / 2 * (t * t * t * t * t + 2) + min;
            }

            public static float SineIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return -max * MathF.Cos(t * (MathF.PI * 90 / 180) / totaltime) + max + min;
            }

            public static float SineOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return max * MathF.Sin(t * (MathF.PI * 90 / 180) / totaltime) + min;
            }

            public static float SineInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return -max / 2 * (MathF.Cos(t * MathF.PI / totaltime) - 1) + min;
            }

            public static float ExpIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return t == 0.0 ? min : max * MathF.Pow(2, 10 * (t / totaltime - 1)) + min;
            }

            public static float ExpOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                return t == totaltime ? max + min : max * (-MathF.Pow(2, -10 * t / totaltime) + 1) + min;
            }

            public static float ExpInOut(float t, float totaltime, float min, float max)
            {
                if (t == 0.0f) return min;
                if (t == totaltime) return max;
                max -= min;
                t /= totaltime / 2;

                if (t < 1) return max / 2 * MathF.Pow(2, 10 * (t - 1)) + min;

                t -= 1;
                return max / 2 * (-MathF.Pow(2, -10 * t) + 2) + min;

            }

            public static float CircIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                return -max * (MathF.Sqrt(1 - t * t) - 1) + min;
            }

            public static float CircOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t = t / totaltime - 1;
                return max * MathF.Sqrt(1 - t * t) + min;
            }

            public static float CircInOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime / 2;
                if (t < 1) return -max / 2 * (MathF.Sqrt(1 - t * t) - 1) + min;

                t -= 2;
                return max / 2 * (MathF.Sqrt(1 - t * t) + 1) + min;
            }

            public static float ElasticIn(float t, float totaltime, float min, float max)
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
                return -(a * MathF.Pow(2, 10 * t) * MathF.Sin((t * totaltime - s) * (2 * MathF.PI) / p)) + min;
            }

            public static float ElasticOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;
                float p = totaltime * 0.3f; ;
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

                return a * MathF.Pow(2, -10 * t) * MathF.Sin((t * totaltime - s) * (2 * MathF.PI) / p) + max + min;
            }

            public static float ElasticInOut(float t, float totaltime, float min, float max)
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
                    return -0.5f * (a * MathF.Pow(2, 10 * (t -= 1)) * MathF.Sin((t * totaltime - s) * (2 * MathF.PI) / p)) + min;
                }

                t -= 1;
                return a * MathF.Pow(2, -10 * t) * MathF.Sin((t * totaltime - s) * (2 * MathF.PI) / p) * 0.5f + max + min;
            }

            public static float BackIn(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.01);

                max -= min;
                t /= totaltime;
                return max * t * t * ((s + 1) * t - s) + min;
            }

            public static float BackOut(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.001);

                max -= min;
                t = t / totaltime - 1;
                return max * (t * t * ((s + 1) * t + s) + 1) + min;
            }

            public static float BackInOut(float t, float totaltime, float min, float max)
            {
                float val = max - min;
                float s = (float)(val * 0.01);

                max -= min;
                s *= 1.525f;
                t /= totaltime / 2;
                if (t < 1) return max / 2 * (t * t * ((s + 1) * t - s)) + min;

                t -= 2;
                return max / 2 * (t * t * ((s + 1) * t + s) + 2) + min;
            }

            public static float BounceIn(float t, float totaltime, float min, float max)
            {
                max -= min;
                return max - BounceOut(totaltime - t, totaltime, 0, max) + min;
            }

            public static float BounceOut(float t, float totaltime, float min, float max)
            {
                max -= min;
                t /= totaltime;

                if (t < 1.0f / 2.75f)
                {
                    return max * (7.5625f * t * t) + min;
                }
                else if (t < 2.0f / 2.75f)
                {
                    t -= 1.5f / 2.75f;
                    return max * (7.5625f * t * t + 0.75f) + min;
                }
                else if (t < 2.5f / 2.75f)
                {
                    t -= 2.25f / 2.75f;
                    return max * (7.5625f * t * t + 0.9375f) + min;
                }
                else
                {
                    t -= 2.625f / 2.75f;
                    return max * (7.5625f * t * t + 0.984375f) + min;
                }
            }

            public static float BounceInOut(float t, float totaltime, float min, float max)
            {
                if (t < totaltime / 2)
                {
                    return BounceIn(t * 2, totaltime, 0, max - min) * 0.5f + min;
                }
                else
                {
                    return BounceOut(t * 2 - totaltime, totaltime, 0, max - min) * 0.5f + min + (max - min) * 0.5f;
                }
            }

            public static float Linear(float t, float totaltime, float min, float max)
            {
                return (max - min) * t / totaltime + min;
            }
            public static float None(float t, float totaltime, float min, float max)
            {
                return min;
            }
        }
    }
}
