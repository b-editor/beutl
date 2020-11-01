using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;

using OpenTK.Graphics.OpenGL;

namespace BEditor.Core.Data.EffectData.DefaultCommon {
    [DataContract(Namespace = "")]
    public class DepthTest : EffectElement {
        public static readonly CheckPropertyMetadata EnabledMetadata = new CheckPropertyMetadata(Properties.Resources.DepthTestEneble, true);
        public static readonly SelectorPropertyMetadata FunctionMetadata = new SelectorPropertyMetadata(Properties.Resources.DepthFunction, 1, new string[] {
                "Never",
                "Less",
                "Equal",
                "Lequal",
                "Greater",
                "Notequal",
                "Gequal",
                "Always"
            });//初期値はless
        public static readonly CheckPropertyMetadata MaskMetadata = new CheckPropertyMetadata("Mask", true);
        public static readonly EasePropertyMetadata NearMetadata = new EasePropertyMetadata("Near", 0, 100, 0);
        public static readonly EasePropertyMetadata FarMetadata = new EasePropertyMetadata("Far", 100, 100, 0);

        #region Function用

        static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new ReadOnlyCollection<DepthFunction>(new DepthFunction[] {
            DepthFunction.Never,
            DepthFunction.Less,
            DepthFunction.Equal,
            DepthFunction.Lequal,
            DepthFunction.Greater,
            DepthFunction.Notequal,
            DepthFunction.Gequal,
            DepthFunction.Always
        });

        #endregion

        #region コンストラクタ

        public DepthTest() {
            Enabled = new CheckProperty(EnabledMetadata);
            Function = new SelectorProperty(FunctionMetadata);
            Mask = new CheckProperty(MaskMetadata);
            Near = new EaseProperty(NearMetadata);
            Far = new EaseProperty(FarMetadata);
        }

        #endregion

        #region CommonEffect
        public override string Name => Properties.Resources.DepthTest;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Enabled,
            Function,
            Mask,
            Near,
            Far
        };

        public override void Load(EffectLoadArgs args) {
            if (Enabled.IsChecked) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.IsChecked);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata("EnabledMetadata", typeof(DepthTest))]
        public CheckProperty Enabled { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata("FunctionMetadata", typeof(DepthTest))]
        public SelectorProperty Function { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata("MaskMetadata", typeof(DepthTest))]
        public CheckProperty Mask { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata("NearMetadata", typeof(DepthTest))]
        public EaseProperty Near { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata("FarMetadata", typeof(DepthTest))]
        public EaseProperty Far { get; set; }
    }
}
