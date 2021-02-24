using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Properties;

using OpenTK.Graphics.OpenGL4;

using static BEditor.Data.Property.PrimitiveGroup.Coordinate;

using GLColor = OpenTK.Mathematics.Color4;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL point light source.
    /// </summary>
    [DataContract]
    public class PointLightSource : EffectElement
    {
        /// <summary>
        /// Represents <see cref="ConstantAttenuation"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ConstantAttenuationMetadata = new("ConstantAttenuation", 100, float.NaN, 1);
        /// <summary>
        /// Represents <see cref="LinearAttenuation"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata LinearAttenuationMetadata = new("LinearAttenuation", 0, 100, 0);
        /// <summary>
        /// Represents <see cref="QuadraticAttenuation"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata QuadraticAttenuationMetadata = new("QuadraticAttenuation", 0, 100, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="PointLightSource"/> class.
        /// </summary>
        public PointLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            ConstantAttenuation = new(ConstantAttenuationMetadata);
            LinearAttenuation = new(LinearAttenuationMetadata);
            QuadraticAttenuation = new(QuadraticAttenuationMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.PointLightSource;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            ConstantAttenuation,
            LinearAttenuation,
            QuadraticAttenuation
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
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the value of GL_CONSTANT_ATTENUATION.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty ConstantAttenuation { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the value of GL_LINEAR_ATTENUATION.
        /// </summary>
        [DataMember(Order = 4)]
        public EaseProperty LinearAttenuation { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the value of GL_QUADRATIC_ATTENUATION.
        /// </summary>
        [DataMember(Order = 5)]
        public EaseProperty QuadraticAttenuation { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;

            Parent!.Parent!.GraphicsContext!.Light = new()
            {
                Color = Color.Light,
                Position = new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame))
            };

            //GL.Enable(EnableCap.Lighting);

            //float[] position = new float[] { X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame), 1f };
            //float[] ambientColor = new float[] { 0.1f, 0.1f, 0.1f, 1.0f };

            //GL.Light(LightName.Light0, LightParameter.Ambient, ambientColor);
            //GL.Light(LightName.Light0, LightParameter.Diffuse, GLColor.White);
            //GL.Light(LightName.Light0, LightParameter.Specular, GLColor.White);
            //GL.Light(LightName.Light0, LightParameter.Position, position);

            //GL.Light(LightName.Light0, LightParameter.ConstantAttenuation, ConstantAttenuation.GetValue(frame) / 100);
            //GL.Light(LightName.Light0, LightParameter.LinearAttenuation, LinearAttenuation.GetValue(frame) / 10000);
            //GL.Light(LightName.Light0, LightParameter.QuadraticAttenuation, QuadraticAttenuation.GetValue(frame) / 100000);

            //GL.Enable(EnableCap.Light0);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            ConstantAttenuation.Load(ConstantAttenuationMetadata);
            LinearAttenuation.Load(LinearAttenuationMetadata);
            QuadraticAttenuation.Load(QuadraticAttenuationMetadata);
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
