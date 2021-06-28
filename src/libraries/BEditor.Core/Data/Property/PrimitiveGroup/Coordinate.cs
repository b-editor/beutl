// Coordinate.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        public static readonly DirectProperty<Coordinate, EaseProperty> XProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.X, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectProperty<Coordinate, EaseProperty> YProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Y, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectProperty<Coordinate, EaseProperty> ZProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(Z),
            owner => owner.Z,
            (owner, obj) => owner.Z = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Z, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectProperty<Coordinate, EaseProperty> CenterXProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(CenterX),
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CenterX, 0, float.NaN, float.NaN, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectProperty<Coordinate, EaseProperty> CenterYProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(CenterY),
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CenterY, 0, float.NaN, float.NaN, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="CenterZ"/> property.
        /// </summary>
        public static readonly DirectProperty<Coordinate, EaseProperty> CenterZProperty = EditingProperty.RegisterDirect<EaseProperty, Coordinate>(
            nameof(CenterZ),
            owner => owner.CenterZ,
            (owner, obj) => owner.CenterZ = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CenterZ, 0, float.NaN, float.NaN, true)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Coordinate(CoordinateMetadata metadata)
            : base(metadata)
        {
        }

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the Z coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the X coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterZ { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return CenterX;
            yield return CenterY;
            yield return CenterZ;
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