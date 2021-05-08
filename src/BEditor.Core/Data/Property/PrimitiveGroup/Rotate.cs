using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
        public static readonly DirectEditingProperty<Rotate, EaseProperty> RotateXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Rotate>(
            nameof(RotateX),
            owner => owner.RotateX,
            (owner, obj) => owner.RotateX = obj,
            new EasePropertyMetadata(Strings.RotateX));

        /// <summary>
        /// Defines the <see cref="RotateY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Rotate, EaseProperty> RotateYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Rotate>(
            nameof(RotateY),
            owner => owner.RotateY,
            (owner, obj) => owner.RotateY = obj,
            new EasePropertyMetadata(Strings.RotateY));

        /// <summary>
        /// Defines the <see cref="RotateZ"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Rotate, EaseProperty> RotateZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Rotate>(
            nameof(RotateZ),
            owner => owner.RotateZ,
            (owner, obj) => owner.RotateZ = obj,
            new EasePropertyMetadata(Strings.RotateZ));

        /// <summary>
        /// Initializes a new instance of the <see cref="Rotate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Rotate(RotateMetadata metadata) : base(metadata)
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