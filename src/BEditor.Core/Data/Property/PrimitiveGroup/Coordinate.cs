using System;
using System.Collections.Generic;

using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting XYZ coordinates.
    /// </summary>
    public sealed class Coordinate : ExpandGroup
    {
        /// <summary>
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = new(Strings.X, 0);

        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = new(Strings.Y, 0);

        /// <summary>
        /// Represents <see cref="Z"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZMetadata = new(Strings.Z, 0);

        /// <summary>
        /// Represents <see cref="CenterX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterXMetadata = new(Strings.CenterX, 0, float.NaN, float.NaN, true);

        /// <summary>
        /// Represents <see cref="CenterY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterYMetadata = new(Strings.CenterY, 0, float.NaN, float.NaN, true);

        /// <summary>
        /// Represents <see cref="CenterZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata CenterZMetadata = new(Strings.CenterZ, 0, float.NaN, float.NaN, true);

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Coordinate(PropertyElementMetadata metadata)
            : base(metadata)
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
            CenterZ,
        };

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        [DataMember]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        [DataMember]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate.
        /// </summary>
        [DataMember]
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate of the center.
        /// </summary>
        [DataMember]
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the center.
        /// </summary>
        [DataMember]
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the center.
        /// </summary>
        [DataMember]
        public EaseProperty CenterZ { get; private set; }

        /// <summary>
        /// Reset the <see cref="CenterX"/>, <see cref="CenterY"/>, and <see cref="CenterZ"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            CenterX.Optional = 0;
            CenterY.Optional = 0;
            CenterZ.Optional = 0;
        }

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
    }
}