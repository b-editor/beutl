using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Objects;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that cripping the image.
    /// </summary>
    [DataContract]
    public class Clipping : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Top"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TopMetadata = new(Resources.Top, 0, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Bottom"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata BottomMetadata = new(Resources.Bottom, 0, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Left"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata LeftMetadata = new(Resources.Left, 0, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Right"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RightMetadata = new(Resources.Right, 0, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="AdjustCoordinates"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata AdjustCoordinatesMetadata = new(Resources.Adjust_coordinates);

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipping"/> class.
        /// </summary>
        public Clipping()
        {
            Top = new(TopMetadata);
            Bottom = new(BottomMetadata);
            Left = new(LeftMetadata);
            Right = new(RightMetadata);
            AdjustCoordinates = new(AdjustCoordinatesMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Clipping;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Top,
            Bottom,
            Left,
            Right,
            AdjustCoordinates
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the range to be clipped.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Top { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the range to be clipped.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Bottom { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the range to be clipped.
        /// </summary>
        [DataMember(Order = 2)]
        public EaseProperty Left { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the range to be clipped.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty Right { get; private set; }
        /// <summary>
        /// Gets a <see cref="CheckProperty"/> that indicates whether the coordinates should be adjusted or not.
        /// </summary>
        [DataMember(Order = 4)]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var top = (int)Top.GetValue(args.Frame);
            var bottom = (int)Bottom.GetValue(args.Frame);
            var left = (int)Left.GetValue(args.Frame);
            var right = (int)Right.GetValue(args.Frame);
            var img = args.Value;

            if (AdjustCoordinates.IsChecked && Parent!.Effect[0] is ImageObject image)
            {
                image.Coordinate.CenterX.Optional += -(right / 2) + (left / 2);
                image.Coordinate.CenterY.Optional += -(top / 2) + (bottom / 2);
            }

            if (img.Width <= left + right || img.Height <= top + bottom)
            {
                img.Dispose();
                args.Value = new(1, 1, default(BGRA32));
                return;
            }

            int width = img.Width - left - right;
            int height = img.Height - top - bottom;
            int x = left;
            int y = top;

            var img1 = img[new Rectangle(x, y, width, height)];
            img.Dispose();

            args.Value = img1;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Top.Load(TopMetadata);
            Bottom.Load(BottomMetadata);
            Left.Load(LeftMetadata);
            Right.Load(RightMetadata);
            AdjustCoordinates.Load(AdjustCoordinatesMetadata);
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
