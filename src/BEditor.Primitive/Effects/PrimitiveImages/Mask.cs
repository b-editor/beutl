using System;
using System.Collections.Generic;
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
    /// Represents an <see cref="ImageEffect"/> that masks an image with another <see cref="ImageObject"/>.
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
        public static readonly DirectEditingProperty<Mask, EaseProperty> YProperty = Coordinate.XProperty.WithOwner<Mask>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> RotateProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Mask>(
            nameof(Rotate),
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj,
            new EasePropertyMetadata(Strings.Rotate));

        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> WidthProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Mask>(
            nameof(Width),
            owner => owner.Width,
            (owner, obj) => owner.Width = obj,
            new EasePropertyMetadata(Strings.Width + " (%)", 100, Min: 0));

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, EaseProperty> HeightProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Mask>(
            nameof(Height),
            owner => owner.Height,
            (owner, obj) => owner.Height = obj,
            new EasePropertyMetadata(Strings.Height + " (%)", 100, Min: 0));

        /// <summary>
        /// Defines the <see cref="Image"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, TextProperty> ImageProperty = EditingProperty.RegisterSerializeDirect<TextProperty, Mask>(
            nameof(Image),
            owner => owner.Image,
            (owner, obj) => owner.Image = obj,
            new TextPropertyMetadata(Strings.PathToImageObject));

        /// <summary>
        /// Defines the <see cref="InvertMask"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, CheckProperty> InvertMaskProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Mask>(
            nameof(InvertMask),
            owner => owner.InvertMask,
            (owner, obj) => owner.InvertMask = obj,
            new CheckPropertyMetadata(Strings.InvertMask));

        /// <summary>
        /// Defines the <see cref="FitSize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Mask, CheckProperty> FitSizeProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Mask>(
            nameof(FitSize),
            owner => owner.FitSize,
            (owner, obj) => owner.FitSize = obj,
            new CheckPropertyMetadata(Strings.FitToOriginalSize));

        private ReactiveProperty<ClipElement?>? _clipProperty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mask"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Mask()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Mask;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Rotate;
                yield return Width;
                yield return Height;
                yield return Height;
                yield return Image;
                yield return InvertMask;
                yield return FitSize;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the angle.
        /// </summary>
        public EaseProperty Rotate { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the mask.
        /// </summary>
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the mask.
        /// </summary>
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the <see cref="TextProperty"/> that specifies the image object to be referenced.
        /// </summary>
        public TextProperty Image { get; private set; }

        /// <summary>
        /// Get a <see cref="CheckProperty"/> indicating whether or not to invert the mask.
        /// </summary>
        public CheckProperty InvertMask { get; private set; }

        /// <summary>
        /// Gets a <see cref="CheckProperty"/> indicating whether or not the mask should be fit to the original image size.
        /// </summary>
        public CheckProperty FitSize { get; private set; }

        private ReactiveProperty<ClipElement?> ClipProperty => _clipProperty ??= new();

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            if (ClipProperty.Value is null) return;

            var f = args.Frame;


            var imgobj = (ImageObject)ClipProperty.Value.Effect[0];
            imgobj.Render(
                new EffectRenderArgs((f - Parent?.Start ?? default) + ClipProperty.Value.Start, args.Type),
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

            args.Value.Mask(resizedimg, new PointF(X[f], Y[f]), Rotate[f], InvertMask.Value);
            img.Dispose();
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _clipProperty = Image
                .Select(str => Guid.TryParse(str, out var id) ? (Guid?)id : null)
                .Where(id=>id is not null)
                .Select(id => Parent.Parent.Parent.FindAllChildren<ClipElement>((Guid)id!))
                .ToReactiveProperty();
        }
    }
}