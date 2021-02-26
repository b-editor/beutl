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
using static BEditor.Data.Property.PrimitiveGroup.Material;

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
        /// Initializes a new instance of the <see cref="PointLightSource"/> class.
        /// </summary>
        public PointLightSource()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            Ambient = new(AmbientMetadata);
            Diffuse = new(DiffuseMetadata);
            Specular = new(SpecularMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.PointLightSource;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            Ambient,
            Diffuse,
            Specular,
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
        /// Gets the <see cref="ColorAnimationProperty"/> representing ambient.
        /// </summary>
        [DataMember(Order = 3)]
        public ColorAnimationProperty Ambient { get; private set; }
        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing diffuse.
        /// </summary>
        [DataMember(Order = 4)]
        public ColorAnimationProperty Diffuse { get; private set; }
        /// <summary>
        ///Gets the <see cref="ColorAnimationProperty"/> representing specular.
        /// </summary>
        [DataMember(Order = 5)]
        public ColorAnimationProperty Specular { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            var frame = args.Frame;

            Parent!.Parent!.GraphicsContext!.Light = new(
                new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame)),
                Ambient[frame],
                Diffuse[frame],
                Specular[frame]);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            Ambient.Load(AmbientMetadata);
            Specular.Load(SpecularMetadata);
            Diffuse.Load(DiffuseMetadata);
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
