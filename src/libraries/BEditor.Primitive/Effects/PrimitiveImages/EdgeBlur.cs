// EdgeBlur.cs
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
using BEditor.Primitive.Effects.OpenCv;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Blurs the edges of the image.
    /// </summary>
    public sealed class EdgeBlur : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="KernelWidth"/> property.
        /// </summary>
        public static readonly DirectProperty<EdgeBlur, EaseProperty> KernelWidthProperty = GaussianBlur.KernelWidthProperty.WithOwner<EdgeBlur>(
            owner => owner.KernelWidth,
            (owner, obj) => owner.KernelWidth = obj);

        /// <summary>
        /// Defines the <see cref="KernelHeight"/> property.
        /// </summary>
        public static readonly DirectProperty<EdgeBlur, EaseProperty> KernelHeightProperty = GaussianBlur.KernelHeightProperty.WithOwner<EdgeBlur>(
            owner => owner.KernelHeight,
            (owner, obj) => owner.KernelHeight = obj);

        /// <summary>
        /// Defines the <see cref="AlphaEdge"/> property.
        /// </summary>
        public static readonly DirectProperty<EdgeBlur, CheckProperty> AlphaEdgeProperty = EditingProperty.RegisterDirect<CheckProperty, EdgeBlur>(
            nameof(AlphaEdge),
            owner => owner.AlphaEdge,
            (owner, obj) => owner.AlphaEdge = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.BlurBorderOfTransparency)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="EdgeBlur"/> class.
        /// </summary>
        public EdgeBlur()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.EdgeBlur;

        /// <summary>
        /// Gets the width of the kernel.
        /// </summary>
        [AllowNull]
        public EaseProperty KernelWidth { get; private set; }

        /// <summary>
        /// Gets the height of the kernel.
        /// </summary>
        [AllowNull]
        public EaseProperty KernelHeight { get; private set; }

        /// <summary>
        /// Gets whether to blur the of borders transparency.
        /// </summary>
        [AllowNull]
        public CheckProperty AlphaEdge { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.EdgeBlur(new((int)KernelWidth[args.Frame], (int)KernelHeight[args.Frame]), AlphaEdge.Value);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return KernelWidth;
            yield return KernelHeight;
            yield return AlphaEdge;
        }
    }
}