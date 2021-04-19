using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting XYZ coordinates.
    /// </summary>
    public sealed class Coordinate : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> XProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            new EasePropertyMetadata(Strings.X, 0));

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> YProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            new EasePropertyMetadata(Strings.Y, 0));

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> ZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(Z),
            owner => owner.Z,
            (owner, obj) => owner.Z = obj,
            new EasePropertyMetadata(Strings.Z, 0));

        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> CenterXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(CenterX),
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj,
            new EasePropertyMetadata(Strings.CenterX, 0, float.NaN, float.NaN, true));

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> CenterYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(CenterY),
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj,
            new EasePropertyMetadata(Strings.CenterY, 0, float.NaN, float.NaN, true));

        /// <summary>
        /// Defines the <see cref="CenterZ"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Coordinate, EaseProperty> CenterZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Coordinate>(
            nameof(CenterZ),
            owner => owner.CenterZ,
            (owner, obj) => owner.CenterZ = obj,
            new EasePropertyMetadata(Strings.CenterZ, 0, float.NaN, float.NaN, true));

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
#pragma warning disable CS8618
        public Coordinate(CoordinateMetadata metadata) : base(metadata)
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Z;
                yield return CenterX;
                yield return CenterY;
                yield return CenterZ;
            }
        }

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the Z coordinate.
        /// </summary>
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the X coordinate of the center.
        /// </summary>
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the center.
        /// </summary>
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the center.
        /// </summary>
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
    }

    /// <summary>
    /// The metadata of <see cref="Coordinate"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record CoordinateMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Coordinate>
    {
        /// <inheritdoc/>
        public Coordinate Create()
        {
            return new(this);
        }
    }
}