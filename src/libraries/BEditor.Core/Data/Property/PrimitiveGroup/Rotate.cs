// Rotate.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;

using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the angle of the XYZ axis.
    /// </summary>
    public sealed class Rotate : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="RotateX"/> property.
        /// </summary>
        public static readonly DirectProperty<Rotate, EaseProperty> RotateXProperty = EditingProperty.RegisterDirect<EaseProperty, Rotate>(
            nameof(RotateX),
            owner => owner.RotateX,
            (owner, obj) => owner.RotateX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.RotateX, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="RotateY"/> property.
        /// </summary>
        public static readonly DirectProperty<Rotate, EaseProperty> RotateYProperty = EditingProperty.RegisterDirect<EaseProperty, Rotate>(
            nameof(RotateY),
            owner => owner.RotateY,
            (owner, obj) => owner.RotateY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.RotateY, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="RotateZ"/> property.
        /// </summary>
        public static readonly DirectProperty<Rotate, EaseProperty> RotateZProperty = EditingProperty.RegisterDirect<EaseProperty, Rotate>(
            nameof(RotateZ),
            owner => owner.RotateZ,
            (owner, obj) => owner.RotateZ = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.RotateZ, useOptional: true)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Rotate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Rotate(RotateMetadata metadata)
            : base(metadata)
        {
        }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the X-axis angle.
        /// </summary>
        [AllowNull]
        public EaseProperty RotateX { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the Y-axis angle.
        /// </summary>
        [AllowNull]
        public EaseProperty RotateY { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> of the Z-axis angle.
        /// </summary>
        [AllowNull]
        public EaseProperty RotateZ { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return RotateX;
            yield return RotateY;
            yield return RotateZ;
        }

        /// <summary>
        /// Reset the <see cref="RotateX"/>, <see cref="RotateY"/>, and <see cref="RotateZ"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            RotateX.Optional = 0;
            RotateY.Optional = 0;
            RotateZ.Optional = 0;
        }
    }

    /// <summary>
    /// The metadata of <see cref="Rotate"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record RotateMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Rotate>
    {
        /// <inheritdoc/>
        public Rotate Create()
        {
            return new(this);
        }
    }
}