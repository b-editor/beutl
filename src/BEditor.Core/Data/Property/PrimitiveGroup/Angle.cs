using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Properties;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the angle of the XYZ axis.
    /// </summary>
    [DataContract]
    public sealed class Angle : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="AngleX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AngleXMetadata = new(Resources.AngleX);
        /// <summary>
        /// Represents <see cref="AngleZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AngleYMetadata = new(Resources.AngleY);
        /// <summary>
        /// Represents <see cref="AngleZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AngleZMetadata = new(Resources.AngleZ);

        /// <summary>
        /// Initializes a new instance of the <see cref="Angle"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Angle(PropertyElementMetadata metadata) : base(metadata)
        {
            AngleX = new(AngleXMetadata);
            AngleY = new(AngleYMetadata);
            AngleZ = new(AngleZMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            AngleX,
            AngleY,
            AngleZ
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the X-axis angle.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty AngleX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the Y-axis angle.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty AngleY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the Z-axis angle.
        /// </summary>
        [DataMember(Order = 2)]
        public EaseProperty AngleZ { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            AngleX.Load(AngleXMetadata);
            AngleY.Load(AngleYMetadata);
            AngleZ.Load(AngleZMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            AngleX.Unload();
            AngleY.Unload();
            AngleZ.Unload();
        }
    }
}
