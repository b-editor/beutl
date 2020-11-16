using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Properties;

using OpenTK.Graphics.OpenGL;

namespace BEditor.ObjectModel.EffectData.DefaultCommon
{
    [DataContract(Namespace = "")]
    public class DepthTest : EffectElement
    {
        public static readonly CheckPropertyMetadata EnabledMetadata = new(Resources.DepthTestEneble, true);
        public static readonly SelectorPropertyMetadata FunctionMetadata = new(Resources.DepthFunction, new string[]
        {
                "Never",
                "Less",
                "Equal",
                "Lequal",
                "Greater",
                "Notequal",
                "Gequal",
                "Always"
        });//初期値はless
        public static readonly CheckPropertyMetadata MaskMetadata = new("Mask", true);
        public static readonly EasePropertyMetadata NearMetadata = new("Near", 0, 100, 0);
        public static readonly EasePropertyMetadata FarMetadata = new("Far", 100, 100, 0);

        #region Function用

        static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new ReadOnlyCollection<DepthFunction>(new DepthFunction[]
        {
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

        public DepthTest()
        {
            Enabled = new(EnabledMetadata);
            Function = new(FunctionMetadata);
            Mask = new(MaskMetadata);
            Near = new(NearMetadata);
            Far = new(FarMetadata);
        }

        #endregion

        #region CommonEffect
        public override string Name => Resources.DepthTest;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Enabled,
            Function,
            Mask,
            Near,
            Far
        };

        public override void Render(EffectRenderArgs args)
        {
            if (Enabled.IsChecked) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.IsChecked);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(EnabledMetadata), typeof(DepthTest))]
        public CheckProperty Enabled { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(FunctionMetadata), typeof(DepthTest))]
        public SelectorProperty Function { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(MaskMetadata), typeof(DepthTest))]
        public CheckProperty Mask { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(NearMetadata), typeof(DepthTest))]
        public EaseProperty Near { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(FarMetadata), typeof(DepthTest))]
        public EaseProperty Far { get; private set; }
    }
}
