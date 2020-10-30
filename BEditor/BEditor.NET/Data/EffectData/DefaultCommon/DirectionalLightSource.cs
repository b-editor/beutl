using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Data.PropertyData.Default;
using BEditor.NET.Media;

using OpenTK.Graphics.OpenGL;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

using static BEditor.NET.Data.PropertyData.Default.Coordinate;

namespace BEditor.NET.Data.EffectData.DefaultCommon {
    public class DirectionalLightSource : EffectElement {

        #region EffectElement

        public override string Name => Properties.Resources.DirectionalLightSource;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            X,
            Y,
            Z
        };

        public override void Load(EffectLoadArgs args) {
            GL.Enable(EnableCap.Lighting);

            float[] position = new float[] { X.GetValue(args.Frame), Y.GetValue(args.Frame), Z.GetValue(args.Frame), 0f };
            float[] ambientColor = new float[] { 0.1f, 0.1f, 0.1f, 1.0f };

            GL.Light(LightName.Light0, LightParameter.Ambient, ambientColor);
            GL.Light(LightName.Light0, LightParameter.Diffuse, GLColor.White);
            GL.Light(LightName.Light0, LightParameter.Specular, GLColor.White);
            GL.Light(LightName.Light0, LightParameter.Position, position);
            GL.Enable(EnableCap.Light0);
        }

        #endregion


        public DirectionalLightSource() {
            X = new EaseProperty(XMetadata);
            Y = new EaseProperty(YMetadata);
            Z = new EaseProperty(ZMetadata);
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
    }
}
