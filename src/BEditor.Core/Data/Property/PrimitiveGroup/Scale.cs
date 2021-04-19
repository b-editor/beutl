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
        /// Defines the <see cref="Scale1"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Scale>(
            nameof(Scale1),
            owner => owner.Scale1,
            (owner, obj) => owner.Scale1 = obj,
            new EasePropertyMetadata(Strings.Scale, 100));

        /// <summary>
        /// Defines the <see cref="ScaleX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Scale>(
            nameof(ScaleX),
            owner => owner.ScaleX,
            (owner, obj) => owner.ScaleX = obj,
            new EasePropertyMetadata(Strings.X, 100));

        /// <summary>
        /// Defines the <see cref="ScaleY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Scale>(
            nameof(ScaleY),
            owner => owner.ScaleY,
            (owner, obj) => owner.ScaleY = obj,
            new EasePropertyMetadata(Strings.Y, 100));

        /// <summary>
        /// Defines the <see cref="ScaleZ"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Scale, EaseProperty> ScaleZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Scale>(
            nameof(ScaleZ),
            owner => owner.ScaleZ,
            (owner, obj) => owner.ScaleZ = obj,
            new EasePropertyMetadata(Strings.Z, 100));

        /// <summary>
        /// Initializes a new instance of the <see cref="Coordinate"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
#pragma warning disable CS8618
        public Scale(ScaleMetadata metadata): base(metadata)
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Scale1;
                yield return ScaleX;
                yield return ScaleY;
                yield return ScaleZ;
            }
        }

        /// <summary>
        /// Gets the EaseProperty representing the scale.
        /// </summary>
        public EaseProperty Scale1 { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Z-axis direction.
        /// </summary>
        public EaseProperty ScaleX { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Y-axis direction.
        /// </summary>
        public EaseProperty ScaleY { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the scale in the Z-axis direction.
        /// </summary>
        public EaseProperty ScaleZ { get; private set; }
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