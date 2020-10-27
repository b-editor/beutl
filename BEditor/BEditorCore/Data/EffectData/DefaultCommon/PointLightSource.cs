using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditorCore.Data.ProjectData;
using BEditorCore.Data.PropertyData;
using BEditorCore.Data.PropertyData.Default;

using OpenTK.Graphics.OpenGL;

using static BEditorCore.Data.PropertyData.Default.Coordinate;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

namespace BEditorCore.Data.EffectData.DefaultCommon {
    public class PointLightSource : EffectElement {
        public static readonly EasePropertyMetadata ConstantAttenuationMetadata = new EasePropertyMetadata("ConstantAttenuation", 100, float.NaN, 1);
        public static readonly EasePropertyMetadata LinearAttenuationMetadata = new EasePropertyMetadata("LinearAttenuation", 0, 100, 0);
        public static readonly EasePropertyMetadata QuadraticAttenuationMetadata = new EasePropertyMetadata("QuadraticAttenuation", 0, 100, 0);


        #region EffectElement

        public override string Name => Properties.Resources.PointLightSource;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            X,
            Y,
            Z,
            ConstantAttenuation,
            LinearAttenuation,
            QuadraticAttenuation
        };

        public override void Load(EffectLoadArgs args) {
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

        public PointLightSource() {
            X = new EaseProperty(XMetadata);
            Y = new EaseProperty(YMetadata);
            Z = new EaseProperty(ZMetadata);
            ConstantAttenuation = new EaseProperty(ConstantAttenuationMetadata);
            LinearAttenuation = new EaseProperty(LinearAttenuationMetadata);
            QuadraticAttenuation = new EaseProperty(QuadraticAttenuationMetadata);
        }


        [DataMember(Order = 0)]
        [PropertyMetadata("XMetadata", typeof(Coordinate))]
        public EaseProperty X { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata("YMetadata", typeof(Coordinate))]
        public EaseProperty Y { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata("ZMetadata", typeof(Coordinate))]
        public EaseProperty Z { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata("ConstantAttenuationMetadata", typeof(PointLightSource))]
        public EaseProperty ConstantAttenuation { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata("LinearAttenuationMetadata", typeof(PointLightSource))]
        public EaseProperty LinearAttenuation { get; set; }

        [DataMember(Order = 5)]
        [PropertyMetadata("QuadraticAttenuationMetadata", typeof(PointLightSource))]
        public EaseProperty QuadraticAttenuation { get; set; }
    }
}
