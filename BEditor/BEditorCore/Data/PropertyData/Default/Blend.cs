using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditorCore.Data.PropertyData;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BEditorCore.Data.PropertyData.Default {
    [DataContract(Namespace = "")]
    public class Blend : ExpandGroup {
        public static readonly EasePropertyMetadata AlphaMetadata = new EasePropertyMetadata(Properties.Resources.Alpha, 100, 100, 0);
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new ColorAnimationPropertyMetadata(Properties.Resources.Color, 255, 255, 255, 255, false);
        public static readonly SelectorPropertyMetadata BlendTypeMetadata = new SelectorPropertyMetadata(Properties.Resources.Blend, 0, new string[4] { "通常", "加算", "減算", "乗算" });

        public static readonly List<Action> BlentFunc = new List<Action> {
                () => {
                    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                },
                () => {
                    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                },
                () => {
                    GL.BlendEquationSeparate(BlendEquationMode.FuncReverseSubtract, BlendEquationMode.FuncReverseSubtract);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                },
                () => {
                    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                    GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.SrcColor);
                }
            };

        public Blend(PropertyElementMetadata constant) : base(constant) {
            Alpha = new EaseProperty(AlphaMetadata);
            BlendType = new(BlendTypeMetadata);
            Color = new ColorAnimationProperty(ColorMetadata);
        }


        #region ExpandGroup

        public override IList<PropertyElement> GroupItems => new List<PropertyElement>() {
            Alpha,
            Color,
            BlendType
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata("AlphaMetadata", typeof(Blend))]
        public EaseProperty Alpha { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata("ColorMetadata", typeof(Blend))]
        public ColorAnimationProperty Color { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata("BlendTypeMetadata", typeof(Blend))]
        public SelectorProperty BlendType { get; set; }
    }
}
