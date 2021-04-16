using System;
using System.Collections.Generic;

using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the angle of the XYZ axis.
    /// </summary>
    public sealed class Rotate : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="RotateX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RotateXMetadata = new(Strings.RotateX);

        /// <summary>
        /// Represents <see cref="RotateY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RotateYMetadata = new(Strings.RotateY);

        /// <summary>
        /// Represents <see cref="RotateZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RotateZMetadata = new(Strings.RotateZ);

        /// <summary>
        /// Initializes a new instance of the <see cref="Rotate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Rotate(PropertyElementMetadata metadata)
            : base(metadata)
        {
            RotateX = new(RotateXMetadata);
            RotateY = new(RotateYMetadata);
            RotateZ = new(RotateZMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            RotateX,
            RotateY,
            RotateZ,
        };

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the X-axis angle.
        /// </summary>
        [DataMember]
        public EaseProperty RotateX { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the Y-axis angle.
        /// </summary>
        [DataMember]
        public EaseProperty RotateY { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the Z-axis angle.
        /// </summary>
        [DataMember]
        public EaseProperty RotateZ { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            RotateX.Load(RotateXMetadata);
            RotateY.Load(RotateYMetadata);
            RotateZ.Load(RotateZMetadata);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            RotateX.Unload();
            RotateY.Unload();
            RotateZ.Unload();
        }
    }
}