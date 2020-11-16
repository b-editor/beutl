using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.PropertyData;
using BEditor.Core.Properties;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BEditor.Core.Data.PropertyData.Default
{
    [DataContract(Namespace = "")]
    public sealed class Blend : ExpandGroup
    {
        public static readonly EasePropertyMetadata AlphaMetadata = new(Resources.Alpha, 100, 100, 0);
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new(Resources.Color, 255, 255, 255, 255, false);
        public static readonly SelectorPropertyMetadata BlendTypeMetadata = new(Resources.Blend, new string[4] { "通常", "加算", "減算", "乗算" });

        public static readonly Action[] BlentFunc = new Action[] {
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

        public Blend(PropertyElementMetadata constant) : base(constant)
        {
            Alpha = new(AlphaMetadata);
            BlendType = new(BlendTypeMetadata);
            Color = new(ColorMetadata);
        }


        #region ExpandGroup

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Alpha,
            Color,
            BlendType
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(AlphaMetadata), typeof(Blend))]
        public EaseProperty Alpha { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(Blend))]
        public ColorAnimationProperty Color { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(BlendTypeMetadata), typeof(Blend))]
        public SelectorProperty BlendType { get; private set; }
    }
}
