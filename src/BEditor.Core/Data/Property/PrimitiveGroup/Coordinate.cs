using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting XYZ coordinates.
    /// </summary>
    [DataContract]
    public sealed class Coordinate : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 0);
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 0);
        /// <summary>
        /// Represents <see cref="Z"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 0);
        /// <summary>
        /// Represents <see cref="CenterX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterXMetadata = new(Resources.CenterX, 0, float.NaN, float.NaN, true);
        /// <summary>
        /// Represents <see cref="CenterY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterYMetadata = new(Resources.CenterY, 0, float.NaN, float.NaN, true);
        /// <summary>
        /// Represents <see cref="CenterZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterZMetadata = new(Resources.CenterZ, 0, float.NaN, float.NaN, true);

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Coordinate(PropertyElementMetadata metadata) : base(metadata)
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            CenterX = new(CenterXMetadata);
            CenterY = new(CenterYMetadata);
            CenterZ = new(CenterZMetadata);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            CenterX,
            CenterY,
            CenterZ
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
        /// Get the <see cref="EaseProperty"/> representing the X coordinate of the center.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty CenterX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate of the center.
        /// </summary>
        [DataMember(Order = 4)]
        public EaseProperty CenterY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Z coordinate of the center.
        /// </summary>
        [DataMember(Order = 5)]
        public EaseProperty CenterZ { get; private set; }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            CenterX.Load(CenterXMetadata);
            CenterY.Load(CenterYMetadata);
            CenterZ.Load(CenterZMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            X.Unload();
            Y.Unload();
            Z.Unload();
            CenterX.Unload();
            CenterY.Unload();
            CenterZ.Unload();
        }
        /// <summary>
        /// Reset the <see cref="CenterX"/>, <see cref="CenterY"/>, and <see cref="CenterZ"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            CenterX.Optional = 0;
            CenterY.Optional = 0;
            CenterZ.Optional = 0;
        }
    }
}
