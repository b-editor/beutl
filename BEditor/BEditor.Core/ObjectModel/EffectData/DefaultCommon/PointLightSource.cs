using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.ObjectModel.PropertyData.Default;

using OpenTK.Graphics.OpenGL;

using static BEditor.ObjectModel.PropertyData.Default.Coordinate;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

namespace BEditor.ObjectModel.EffectData.DefaultCommon
{
    public class PointLightSource : EffectElement
    {
        public static readonly EasePropertyMetadata ConstantAttenuationMetadata = new("ConstantAttenuation", 100, float.NaN, 1);
        public static readonly EasePropertyMetadata LinearAttenuationMetadata = new("LinearAttenuation", 0, 100, 0);
        public static readonly EasePropertyMetadata QuadraticAttenuationMetadata = new("QuadraticAttenuation", 0, 100, 0);


        #region EffectElement

        public override string Name => Core.Properties.Resources.PointLightSource;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            ConstantAttenuation,
            LinearAttenuation,
            QuadraticAttenuation
        };

        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            GL.Enable(EnableCap.Lighting);

            float[] position = new float[] { X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame), 1f };
            float[] ambientColor = new float[] { 0.1f, 0.1f, 0.1f, 1.0f };

            GL.Light(LightName.Light0, LightParameter.Ambient, ambientColor);
            GL.Light(LightName.Light0, LightParameter.Diffuse, GLColor.White);
            GL.Light(LightName.Light0, LightParameter.Specular, GLColor.White);
            GL.Light(LightName.Light0, LightParameter.Position, position);

            GL.Light(LightName.Light0, LightParameter.ConstantAttenuation, ConstantAttenuation.GetValue(frame) / 100);
            GL.Light(LightName.Light0, LightParameter.LinearAttenuation, LinearAttenuation.GetValue(frame) / 10000);
            GL.Light(LightName.Light0, LightParameter.QuadraticAttenuation, QuadraticAttenuation.GetValue(frame) / 100000);

            GL.Enable(EnableCap.Light0);
        }

        #endregion

        public PointLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            ConstantAttenuation = new(ConstantAttenuationMetadata);
            LinearAttenuation = new(LinearAttenuationMetadata);
            QuadraticAttenuation = new(QuadraticAttenuationMetadata);
        }


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(XMetadata), typeof(Coordinate))]
        public EaseProperty X { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(YMetadata), typeof(Coordinate))]
        public EaseProperty Y { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ZMetadata), typeof(Coordinate))]
        public EaseProperty Z { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(ConstantAttenuationMetadata), typeof(PointLightSource))]
        public EaseProperty ConstantAttenuation { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(LinearAttenuationMetadata), typeof(PointLightSource))]
        public EaseProperty LinearAttenuation { get; private set; }

        [DataMember(Order = 5)]
        [PropertyMetadata(nameof(QuadraticAttenuationMetadata), typeof(PointLightSource))]
        public EaseProperty QuadraticAttenuation { get; private set; }
    }
}
