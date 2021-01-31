using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.Property;

using OpenTK.Graphics.OpenGL;

using BEditor.Core.Properties;
using BEditor.Core.Command;

using GLColor = OpenTK.Mathematics.Color4;

using static BEditor.Core.Data.Property.PrimitiveGroup.Coordinate;

namespace BEditor.Core.Data.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL directional light source.
    /// </summary>
    [DataContract]
    public class DirectionalLightSource : EffectElement
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DirectionalLightSource"/> class.
        /// </summary>
        public DirectionalLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.DirectionalLightSource;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
        /// </summary>
        [DataMember(Order = 2)]
        public EaseProperty Z { get; private set; }

        /// <inheritdoc/>
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
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
