// Scale.cs
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
    /// Represents a property that sets the scale.
    /// </summary>
    public sealed class Scale : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="Scale1"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleProperty = EditingProperty.RegisterDirect<EaseProperty, Scale>(
            nameof(Scale1),
            owner => owner.Scale1,
            (owner, obj) => owner.Scale1 = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Scale, 100, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="ScaleX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleXProperty = EditingProperty.RegisterDirect<EaseProperty, Scale>(
            nameof(ScaleX),
            owner => owner.ScaleX,
            (owner, obj) => owner.ScaleX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.X, 100, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="ScaleY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleYProperty = EditingProperty.RegisterDirect<EaseProperty, Scale>(
            nameof(ScaleY),
            owner => owner.ScaleY,
            (owner, obj) => owner.ScaleY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Y, 100, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="ScaleZ"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleZProperty = EditingProperty.RegisterDirect<EaseProperty, Scale>(
            nameof(ScaleZ),
            owner => owner.ScaleZ,
            (owner, obj) => owner.ScaleZ = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Z, 100, useOptional: true)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Scale"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Scale(ScaleMetadata metadata)
            : base(metadata)
        {
        }

        /// <summary>
        /// Gets the EaseProperty representing the scale.
        /// </summary>
        [AllowNull]
        public EaseProperty Scale1 { get; private set; }

        /// <summary>
        /// Gets the scale in the Z-axis direction.
        /// </summary>
        [AllowNull]
        public EaseProperty ScaleX { get; private set; }

        /// <summary>
        /// Gets the scale in the Y-axis direction.
        /// </summary>
        [AllowNull]
        public EaseProperty ScaleY { get; private set; }

        /// <summary>
        /// Gets the scale in the Z-axis direction.
        /// </summary>
        [AllowNull]
        public EaseProperty ScaleZ { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Scale1;
            yield return ScaleX;
            yield return ScaleY;
            yield return ScaleZ;
        }

        /// <summary>
        /// Reset the <see cref="Scale1"/>, <see cref="ScaleX"/>, <see cref="ScaleY"/>, and <see cref="ScaleZ"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            Scale1.Optional = 0;
            ScaleX.Optional = 0;
            ScaleY.Optional = 0;
            ScaleZ.Optional = 0;
        }
    }

    /// <summary>
    /// The metadata of <see cref="Scale"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record ScaleMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Scale>
    {
        /// <inheritdoc/>
        public Scale Create()
        {
            return new(this);
        }
    }
}