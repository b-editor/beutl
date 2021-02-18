using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that adds a shadow to an image.
    /// </summary>
    [DataContract]
    public class Shadow : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 10);
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 10);
        /// <summary>
        /// Represents <see cref="Blur"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata BlurMetadata = new(Resources.Blur, 10, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Alpha"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AlphaMetadata = new(Resources.Alpha, 75, 100, 0);
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Dark);

        /// <summary>
        /// Initializes a new instance of the <see cref="Shadow"/> class.
        /// </summary>
        public Shadow()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Blur = new(BlurMetadata);
            Alpha = new(AlphaMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.DropShadow;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Blur,
            Alpha,
            Color
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the X coordinate.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the blur sigma.
        /// </summary>
        [DataMember(Order = 2)]
        public EaseProperty Blur { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> that represents the transparency.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty Alpha { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the shadow color.
        /// </summary>
        [DataMember(Order = 4)]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Shadow(X.GetValue(args.Frame), Y.GetValue(args.Frame), Blur.GetValue(args.Frame), Alpha.GetValue(args.Frame) / 100, Color.Color);
            args.Value.Dispose();

            args.Value = img;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Blur.Load(BlurMetadata);
            Alpha.Load(AlphaMetadata);
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
