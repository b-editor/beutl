// Mask.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

using Reactive.Bindings;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that masks an image with another <see cref="ImageObject"/>.
    /// </summary>
    public sealed class Mask : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> XProperty = Coordinate.XProperty.WithOwner<Mask>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> YProperty = Coordinate.YProperty.WithOwner<Mask>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="MaskRotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> MaskRotateProperty = EditingProperty.RegisterDirect<EaseProperty, Mask>(
            nameof(MaskRotate),
            owner => owner.MaskRotate,
            (owner, obj) => owner.MaskRotate = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Rotate)).Serialize());

        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> WidthProperty = EditingProperty.RegisterDirect<EaseProperty, Mask>(
            nameof(Width),
            owner => owner.Width,
            (owner, obj) => owner.Width = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Width + " (%)", 100, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> HeightProperty = EditingProperty.RegisterDirect<EaseProperty, Mask>(
            nameof(Height),
            owner => owner.Height,
            (owner, obj) => owner.Height = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Height + " (%)", 100, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Image"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, TextProperty> ImageProperty = EditingProperty.RegisterDirect<TextProperty, Mask>(
            nameof(Image),
            owner => owner.Image,
            (owner, obj) => owner.Image = obj,
            EditingPropertyOptions<TextProperty>.Create(new TextPropertyMetadata(Strings.PathToImageObject)).Serialize());

        /// <summary>
        /// Defines the <see cref="InvertMask"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, CheckProperty> InvertMaskProperty = EditingProperty.RegisterDirect<CheckProperty, Mask>(
            nameof(InvertMask),
            owner => owner.InvertMask,
            (owner, obj) => owner.InvertMask = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.InvertMask)).Serialize());

        /// <summary>
        /// Defines the <see cref="FitSize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, CheckProperty> FitSizeProperty = EditingProperty.RegisterDirect<CheckProperty, Mask>(
            nameof(FitSize),
            owner => owner.FitSize,
            (owner, obj) => owner.FitSize = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.FitToOriginalSize)).Serialize());

        private ReactiveProperty<ClipElement?>? _clipProperty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mask"/> class.
        /// </summary>
        public Mask()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Mask;

        /// <summary>
        /// Get the X coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the angle.
        /// </summary>
        [AllowNull]
        public EaseProperty MaskRotate { get; private set; }

        /// <summary>
        /// Gets the width of the mask.
        /// </summary>
        [AllowNull]
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Gets the height of the mask.
        /// </summary>
        [AllowNull]
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the image object to be referenced.
        /// </summary>
        [AllowNull]
        public TextProperty Image { get; private set; }

        /// <summary>
        /// Gets whether or not to invert the mask.
        /// </summary>
        [AllowNull]
        public CheckProperty InvertMask { get; private set; }

        /// <summary>
        /// Gets whether or not the mask should be fit to the original image size.
        /// </summary>
        [AllowNull]
        public CheckProperty FitSize { get; private set; }

        private ReactiveProperty<ClipElement?> ClipProperty => _clipProperty ??= new();

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (ClipProperty.Value is null) return;

            var f = args.Frame;

            var imgobj = (ImageObject)ClipProperty.Value.Effect[0];
            imgobj.Render(
                new EffectApplyArgs(f - Parent.Start + ClipProperty.Value.Start, args.Type),
                out var img);
            imgobj.Coordinate.ResetOptional();

            if (img is null) return;

            int w = (int)(Width[f] * 0.01 * img.Width);
            int h = (int)(Height[f] * 0.01 * img.Height);

            if (FitSize.Value)
            {
                w = args.Value.Width;
                h = args.Value.Height;
            }

            if (w is 0 || h is 0) return;
            using var resizedimg = img.Resize(w, h, Quality.Medium);

            args.Value.Mask(resizedimg, new PointF(X[f], Y[f]), MaskRotate[f], InvertMask.Value, Parent.Parent.DrawingContext);
            img.Dispose();
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return MaskRotate;
            yield return Width;
            yield return Height;
            yield return Image;
            yield return InvertMask;
            yield return FitSize;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _clipProperty = Image
                .Select(str => Guid.TryParse(str, out var id) ? (Guid?)id : null)
                .Where(id => id is not null)
                .Select(id => Parent.Parent.Parent.FindAllChildren<ClipElement>((Guid)id!))
                .ToReactiveProperty();
        }
    }
}