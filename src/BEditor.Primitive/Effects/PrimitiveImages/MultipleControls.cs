using System.Collections.Generic;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents a <see cref="ImageEffect"/> that provides the ability to edit multiple objects by specifying their indices.
    /// </summary>
    public sealed class MultipleControls : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Coordinate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<MultipleControls, Coordinate> CoordinateProperty = ImageObject.CoordinateProperty.WithOwner<MultipleControls>(
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj);

        /// <summary>
        /// Defines the <see cref="Scale"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<MultipleControls, Scale> ScaleProperty = ImageObject.ScaleProperty.WithOwner<MultipleControls>(
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj);
        
        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<MultipleControls, Rotate> RotateProperty = ImageObject.RotateProperty.WithOwner<MultipleControls>(
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj);

        /// <summary>
        /// Defines the <see cref="Index"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<MultipleControls, ValueProperty> IndexProperty = EditingProperty.RegisterSerializeDirect<ValueProperty, MultipleControls>(
            nameof(Index),
            owner => owner.Index,
            (owner, obj) => owner.Index = obj,
            new ValuePropertyMetadata("index", 0, Min: 0));

        /// <summary>
        /// INitializes a new instance of the <see cref="MultipleControls"/> class.
        /// </summary>
#pragma warning disable CS8618
        public MultipleControls()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.MultipleImageControls;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Coordinate;
                yield return Scale;
                yield return Rotate;
                yield return Index;
            }
        }

        /// <summary>
        /// Get the coordinates.
        /// </summary>
        public Coordinate Coordinate { get; private set; }

        /// <summary>
        /// Get the scale.
        /// </summary>
        public Scale Scale { get; private set; }

        /// <summary>
        /// Get the angle.
        /// </summary>
        public Rotate Rotate { get; private set; }

        /// <summary>
        /// Gets the <see cref="ValueProperty"/> representing the index of the image to be controlled.
        /// </summary>
        public ValueProperty Index { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            args.Value = args.Value.Select((img, i) =>
            {
                var trans = img.Transform;

                if (i == (int)Index.Value)
                {
                    return new(
                        img.Source,
                        _ =>
                        {
                            var f = args.Frame;
                            var s = Scale.Scale1[f] / 100;
                            var sx = Scale.ScaleX[f] / 100 * s - 1;
                            var sy = Scale.ScaleY[f] / 100 * s - 1;
                            var sz = Scale.ScaleZ[f] / 100 * s - 1;

                            return img.Transform + Transform.Create(
                                new(Coordinate.X[f], Coordinate.Y[f], Coordinate.Z[f]),
                                new(Coordinate.CenterX[f], Coordinate.CenterY[f], Coordinate.CenterZ[f]),
                                new(Rotate.RotateX[f], Rotate.RotateY[f], Rotate.RotateZ[f]),
                                new(sx, sy, sz));
                        });
                }

                return img;
            });
        }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {

        }
    }
}