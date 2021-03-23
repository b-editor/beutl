using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that masks an image with another <see cref="ImageObject"/>.
    /// </summary>
    public sealed class Mask : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = Coordinate.XMetadata;
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = Coordinate.YMetadata;
        /// <summary>
        /// Represents <see cref="Rotate"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RotateMetadata = new(Resources.Rotate);
        /// <summary>
        /// Represents <see cref="Width"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata WidthMetadata = new(Resources.Width + " (%)", 100, Min: 0);
        /// <summary>
        /// Represents <see cref="Height"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata HeightMetadata = new(Resources.Height + " (%)", 100, Min: 0);
        /// <summary>
        /// Represents <see cref="Image"/> metadata.
        /// </summary>
        public static readonly TextPropertyMetadata ImageMetadata = new(Resources.PathToImageObject);
        /// <summary>
        /// Represents <see cref="InvertMask"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata InvertMaskMetadata = new(Resources.InvertMask);
        /// <summary>
        /// Represents <see cref="FitSize"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata FitSizeMetadata = new(Resources.FitToOriginalSize);
        private ReactiveProperty<ClipElement?>? _clipProperty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mask"/> class.
        /// </summary>
        public Mask()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Rotate = new(RotateMetadata);
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Image = new(ImageMetadata);
            InvertMask = new(InvertMaskMetadata);
            FitSize = new(FitSizeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Mask;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Rotate,
            Width,
            Height,
            Image,
            InvertMask,
            FitSize
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        [DataMember]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        [DataMember]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the angle.
        /// </summary>
        [DataMember]
        public EaseProperty Rotate { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the width of the mask.
        /// </summary>
        [DataMember]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the height of the mask.
        /// </summary>
        [DataMember]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Gets the <see cref="TextProperty"/> that specifies the image object to be referenced.
        /// </summary>
        [DataMember]
        public TextProperty Image { get; private set; }
        /// <summary>
        /// Get a <see cref="CheckProperty"/> indicating whether or not to invert the mask.
        /// </summary>
        [DataMember]
        public CheckProperty InvertMask { get; private set; }
        /// <summary>
        /// Gets a <see cref="CheckProperty"/> indicating whether or not the mask should be fit to the original image size.
        /// </summary>
        [DataMember]
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
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Rotate.Load(RotateMetadata);
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Image.Load(ImageMetadata);
            InvertMask.Load(InvertMaskMetadata);
            FitSize.Load(FitSizeMetadata);

            _clipProperty = Image
                .Select(str => ClipElement.FromFullName(str, Parent?.Parent?.Parent))
                .ToReactiveProperty();

            ClipProperty.Value = ClipElement.FromFullName(Image.Value, Parent?.Parent?.Parent);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var p in Children)
            {
                p.Unload();
            }
        }
    }
}
