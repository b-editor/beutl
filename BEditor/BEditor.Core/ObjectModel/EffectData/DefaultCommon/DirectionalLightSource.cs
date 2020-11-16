using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.ObjectModel.PropertyData.Default;
using BEditor.Media;

using OpenTK.Graphics.OpenGL;
using BEditor.Properties;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

using static BEditor.ObjectModel.PropertyData.Default.Coordinate;

namespace BEditor.ObjectModel.EffectData.DefaultCommon
{
    public class DirectionalLightSource : EffectElement
    {

        #region EffectElement

        public override string Name => Resources.DirectionalLightSource;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z
        };

        public override void Render(EffectRenderArgs args)
        {
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


        public DirectionalLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
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
    }
}
