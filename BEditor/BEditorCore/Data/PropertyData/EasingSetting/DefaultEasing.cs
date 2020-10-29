using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace BEditorCore.Data.PropertyData.EasingSetting {

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
    public class DefaultEasing : EasingFunc {
        public static readonly SelectorPropertyMetadata propertyMetadata = new SelectorPropertyMetadata("EasingType", 0, new string[32] {
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

        public override IList<IEasingSetting> EasingSettings => new List<IEasingSetting> {
            EasingType
        };

        public override float EaseFunc(int frame, int totalframe, float min, float max) => currentfunc?.Invoke(frame, totalframe, min, max) ?? 0;

        private Func<float, float, float, float, float> currentfunc;

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            currentfunc = DefaultEase[EasingType.Index];

            EasingType.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(SelectorProperty.Index)) {
                    currentfunc = DefaultEase[EasingType.Index];
                }
            };
        }

        #endregion

        #region コンストラクタ
        public DefaultEasing() {
            EasingType = new SelectorProperty(propertyMetadata);
        }
        #endregion


        [DataMember(), PropertyMetadata("propertyMetadata", typeof(DefaultEasing))]
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

        class Easing {
            private const string dll = "BEditorExtern";

            [DllImport(dll)]
            public static extern float QuadIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuadOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuadInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CubicIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CubicOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CubicInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuartIn(float t, float totaltime, float min, float max);

            [DllImport(dll)] 
            public static extern float QuartOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuartInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuintIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuintOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float QuintInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float SineIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float SineOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float SineInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ExpIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ExpOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ExpInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CircIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CircOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float CircInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ElasticIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ElasticOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float ElasticInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BackIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BackOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BackInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BounceIn(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BounceOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float BounceInOut(float t, float totaltime, float min, float max);

            [DllImport(dll)]
            public static extern float Linear(float t, float totaltime, float min, float max);
            
            [DllImport(dll)]
            public static extern float None(float t, float totaltime, float min, float max);
        }
    }
}
