using System;
using System.Collections.Generic;

using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property that sets the scale.
    /// </summary>
    public sealed class Scale : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="Scale1"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ScaleMetadata = new(Strings.Scale, 100);

        /// <summary>
        /// Represents <see cref="ScaleX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ScaleXMetadata = new(Strings.X, 100);

        /// <summary>
        /// Represents <see cref="ScaleY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ScaleYMetadata = new(Strings.Y, 100);

        /// <summary>
        /// Represents <see cref="ScaleZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ScaleZMetadata = new(Strings.Z, 100);

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Scale(PropertyElementMetadata metadata)
            : base(metadata)
        {
            Scale1 = new(ScaleMetadata);
            ScaleX = new(ScaleXMetadata);
            ScaleY = new(ScaleYMetadata);
            ScaleZ = new(ScaleZMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Scale1,
            ScaleX,
            ScaleY,
            ScaleZ,
        };

        /// <summary>
        /// Gets the EaseProperty representing the scale.
        /// </summary>
        [DataMember]
        public EaseProperty Scale1 { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Z-axis direction.
        /// </summary>
        [DataMember]
        public EaseProperty ScaleX { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Y-axis direction.
        /// </summary>
        [DataMember]
        public EaseProperty ScaleY { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Z-axis direction.
        /// </summary>
        [DataMember]
        public EaseProperty ScaleZ { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Scale1.Load(ScaleMetadata);
            ScaleX.Load(ScaleXMetadata);
            ScaleY.Load(ScaleYMetadata);
            ScaleZ.Load(ScaleZMetadata);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Scale1.Unload();
            ScaleX.Unload();
            ScaleY.Unload();
            ScaleZ.Unload();
        }
    }
}