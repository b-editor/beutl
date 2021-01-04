using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.Property;

using OpenTK.Graphics.OpenGL;

using BEditor.Core.Properties;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

using static BEditor.Core.Data.Primitive.Properties.PrimitiveGroup.Coordinate;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class DirectionalLightSource : EffectElement
    {
        public DirectionalLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
        }

        public override string Name => Resources.DirectionalLightSource;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z
        };
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Z { get; private set; }

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
        public override void Loaded()
        {
            base.Loaded();
            X.ExecuteLoaded(XMetadata);
            Y.ExecuteLoaded(YMetadata);
            Z.ExecuteLoaded(ZMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            foreach (var pr in Children)
            {
                pr.Unloaded();
            }
        }
    }
}
