using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.PrimitiveGroup;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive.Effects
{
    /// <summary>
    /// Represents a <see cref="MultipleImageEffect"/> that provides the ability to edit multiple objects by specifying their indices.
    /// </summary>
    [DataContract]
    public class MultipleControls : MultipleImageEffect
    {
        /// <summary>
        /// Represents <see cref="Coordinate"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata CoordinateMetadata = ImageObject.CoordinateMetadata;
        /// <summary>
        /// Represents <see cref="Zoom"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata ZoomMetadata = ImageObject.ZoomMetadata;
        /// <summary>
        /// Represents <see cref="Angle"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata AngleMetadata = ImageObject.AngleMetadata;
        /// <summary>
        /// Represents <see cref="Index"/> metadata.
        /// </summary>
        public static readonly ValuePropertyMetadata IndexMetadata = new("index", 0, Min: 0);

        /// <summary>
        /// INitializes a new instance of the <see cref="MultipleControls"/> class.
        /// </summary>
        public MultipleControls()
        {
            Coordinate = new(CoordinateMetadata);
            Zoom = new(ZoomMetadata);
            Angle = new(AngleMetadata);
            Index = new(IndexMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.MultipleImageControls;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Angle,
            Index
        };
        /// <summary>
        /// Get the coordinates.
        /// </summary>
        [DataMember(Order = 0)]
        public Coordinate Coordinate { get; private set; }
        /// <summary>
        /// Get the scale.
        /// </summary>
        [DataMember(Order = 1)]
        public Zoom Zoom { get; private set; }
        /// <summary>
        /// Get the angle.
        /// </summary>
        [DataMember(Order = 2)]
        public Angle Angle { get; private set; }
        /// <summary>
        /// Gets the <see cref="ValueProperty"/> representing the index of the image to be controlled.
        /// </summary>
        [DataMember(Order = 3)]
        public ValueProperty Index { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<Image<BGRA32>> args)
        {
            return new ImageInfo[] { new(args.Value, _ => default, 0) };
        }
        /// <inheritdoc/>
        public override IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<ImageInfo> args)
        {
            var trans = args.Value.Transform;
            var i = args.Value.Index;

            if (i == (int)Index.Value)
            {
                return new ImageInfo[]
                {
                    new(
                        args.Value.Source,
                        _ =>
                        {
                            var f = args.Frame;
                            var s = Zoom.Scale[f] / 100;
                            var sx = Zoom.ScaleX[f] / 100 * s - 1;
                            var sy = Zoom.ScaleY[f] / 100 * s - 1;
                            var sz = Zoom.ScaleZ[f] / 100 * s - 1;

                            return args.Value.Transform + Transform.Create(
                                new(Coordinate.X[f], Coordinate.Y[f], Coordinate.Z[f]),
                                new(Coordinate.CenterX[f], Coordinate.CenterY[f], Coordinate.CenterZ[f]),
                                new(Angle.AngleX[f], Angle.AngleY[f], Angle.AngleZ[f]),
                                new(sx, sy, sz));
                        },
                        i)
                };
            }

            return new ImageInfo[] { args.Value };
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Coordinate.Load(CoordinateMetadata);
            Zoom.Load(ZoomMetadata);
            Angle.Load(AngleMetadata);
            Index.Load(IndexMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Coordinate.Unload();
            Zoom.Unload();
            Angle.Unload();
            Index.Unload();
        }
    }
}
