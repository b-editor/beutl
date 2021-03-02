using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Objects;
using BEditor.Properties;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    [DataContract]
    public class StrokeText : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Size"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SizeMetadata = Border.SizeMetadata;
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = Border.ColorMetadata;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeText"/> class.
        /// </summary>
        public StrokeText()
        {
            Size = new(SizeMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => $"{Resources.Border} ({Resources.Text})";
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the size of the edge.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the edge color.
        /// </summary>
        [DataMember(Order = 1)]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is Text textObj)
            {
                var stroke = Image.StrokeText(textObj.Document.Value, textObj.Font.Value, textObj.Size[args.Frame], Size[args.Frame], Color.Value);

                stroke.DrawImage(
                    new((stroke.Width - args.Value.Width) / 2, (stroke.Height - args.Value.Height) / 2),
                    args.Value);

                args.Value.Dispose();

                args.Value = stroke;
            }
        }
        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            if (Parent.Effect[0] is Text textObj)
            {
                if (!textObj.EnableMultiple.Value)
                {
                    var imageArgs = new EffectRenderArgs<Image<BGRA32>>(args.Frame, args.Value.First().Source, args.Type);
                    Render(imageArgs);
                    args.Value = new ImageInfo[]
                    {
                        new(imageArgs.Value, _ => default)
                    };

                    return;
                }
                args.Value = args.Value.Select((imageInfo, index) =>
                {
                    var stroke = Image.StrokeText(textObj.Document.Value, textObj.Font.Value, textObj.Size[args.Frame], Size[args.Frame], Color.Value);

                    stroke.DrawImage(
                        new((stroke.Width - imageInfo.Source.Width) / 2, (stroke.Height - imageInfo.Source.Height) / 2),
                        imageInfo.Source);

                    imageInfo.Dispose();

                    return new ImageInfo(stroke, img => Transform.Create(new(img.Source.Width * index, 0, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero));
                });
            }

        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Size.Load(SizeMetadata);
            Color.Load(ColorMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
