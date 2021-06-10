// ChromaKey.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that makes the background color of an image transparent.
    /// </summary>
    public sealed class ChromaKey : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="ThresholdValue"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ChromaKey, EaseProperty> TopProperty = EditingProperty.RegisterDirect<EaseProperty, ChromaKey>(
            nameof(ThresholdValue),
            owner => owner.ThresholdValue,
            (owner, obj) => owner.ThresholdValue = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ThresholdValue, 256)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKey"/> class.
        /// </summary>
        public ChromaKey()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ChromaKey;

        /// <summary>
        /// Gets the threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.ChromaKey((int)ThresholdValue[args.Frame], Parent.Parent.DrawingContext);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return ThresholdValue;
        }
    }
}