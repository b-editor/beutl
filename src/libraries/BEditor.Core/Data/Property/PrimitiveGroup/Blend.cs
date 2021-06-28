// Blend.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Drawing;
using BEditor.Resources;

namespace BEditor.Data.Property.PrimitiveGroup
{
    /// <summary>
    /// Represents a property for setting the blend Option.
    /// </summary>
    public sealed class Blend : ExpandGroup
    {
        /// <summary>
        /// Defines the <see cref="Opacity"/> property.
        /// </summary>
        public static readonly DirectProperty<Blend, EaseProperty> OpacityProperty = EditingProperty.RegisterDirect<EaseProperty, Blend>(
            nameof(Opacity),
            owner => owner.Opacity,
            (owner, obj) => owner.Opacity = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Opacity, 100, 100, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Blend, ColorAnimationProperty> ColorProperty = EditingProperty.RegisterDirect<ColorAnimationProperty, Blend>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorAnimationProperty>.Create(new ColorAnimationPropertyMetadata(Strings.Color, Colors.White, false)).Serialize());

        /// <summary>
        /// Defines the <see cref="BlendType"/> property.
        /// </summary>
        public static readonly DirectProperty<Blend, SelectorProperty> BlendTypeProperty = EditingProperty.RegisterDirect<SelectorProperty, Blend>(
            nameof(BlendType),
            owner => owner.BlendType,
            (owner, obj) => owner.BlendType = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Blend, new[] { Strings.AlphaBlend, Strings.Additive, Strings.Subtract, Strings.Multiplication })).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Blend"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public Blend(BlendMetadata metadata)
            : base(metadata)
        {
        }

        /// <summary>
        /// Gets the opacity.
        /// </summary>
        [AllowNull]
        public EaseProperty Opacity { get; private set; }

        /// <summary>
        /// Gets the color.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Color { get; private set; }

        /// <summary>
        /// Gets the BlendFunc.
        /// </summary>
        [AllowNull]
        public SelectorProperty BlendType { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Opacity;
            yield return Color;
            yield return BlendType;
        }

        /// <summary>
        /// Reset the <see cref="Opacity"/> Optionals.
        /// </summary>
        public void ResetOptional()
        {
            Opacity.Optional = 0;
        }
    }

    /// <summary>
    /// The metadata of <see cref="Blend"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    public record BlendMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<Blend>
    {
        /// <inheritdoc/>
        public Blend Create()
        {
            return new(this);
        }
    }
}