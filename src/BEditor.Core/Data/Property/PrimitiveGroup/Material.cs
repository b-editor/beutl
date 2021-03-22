using System;
using System.Collections.Generic;

using BEditor.Drawing;
using BEditor.Properties;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property that sets the Material.
    /// </summary>
    public sealed class Material : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="Ambient"/> metadata.
        /// </summary>
        public static readonly ColorAnimationPropertyMetadata AmbientMetadata = new(Resources.Ambient, Color.Light, true);
        /// <summary>
        /// Represents <see cref="Diffuse"/> metadata.
        /// </summary>
        public static readonly ColorAnimationPropertyMetadata DiffuseMetadata = new(Resources.Diffuse, Color.Light, true);
        /// <summary>
        /// Represents <see cref="Specular"/> metadata.
        /// </summary>
        public static readonly ColorAnimationPropertyMetadata SpecularMetadata = new(Resources.Specular, Color.Light, true);
        /// <summary>
        /// Represents <see cref="Shininess"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ShininessMetadata = new(Resources.Shininess, 10, float.NaN, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="Material"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Material(PropertyElementMetadata metadata) : base(metadata)
        {
            Ambient = new(AmbientMetadata);
            Diffuse = new(DiffuseMetadata);
            Specular = new(SpecularMetadata);
            Shininess = new(ShininessMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Ambient,
            Diffuse,
            Specular,
            Shininess
        };
        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing the ambient.
        /// </summary>
        [DataMember]
        public ColorAnimationProperty Ambient { get; private set; }
        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing the diffuse.
        /// </summary>
        [DataMember]
        public ColorAnimationProperty Diffuse { get; private set; }
        /// <summary>
        /// Gets the <see cref="ColorAnimationProperty"/> representing the shininess.
        /// </summary>
        [DataMember]
        public ColorAnimationProperty Specular { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the shininess.
        /// </summary>
        [DataMember]
        public EaseProperty Shininess { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Ambient.Load(AmbientMetadata);
            Diffuse.Load(DiffuseMetadata);
            Specular.Load(SpecularMetadata);
            Shininess.Load(ShininessMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Ambient.Unload();
            Diffuse.Unload();
            Specular.Unload();
            Shininess.Unload();
        }
    }
}
