using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

using GLColor = OpenTK.Mathematics.Color4;
using Material = BEditor.Data.Property.PrimitiveGroup.Material;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that draws a Cube, Ball, etc.
    /// </summary>
    public sealed class GL3DObject : ObjectElement
    {
        /// <summary>
        /// Represents <see cref="Type"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Strings.Type, new string[2]
        {
            Strings.Cube,
            Strings.Ball
        });
        /// <summary>
        /// Represents <see cref="Depth"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata DepthMetadata = new(Strings.Depth, 100, float.NaN, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="GL3DObject"/> class.
        /// </summary>
        public GL3DObject()
        {
            Coordinate = new(ImageObject.CoordinateMetadata);
            Zoom = new(ImageObject.ScaleMetadata);
            Blend = new(ImageObject.BlendMetadata);
            Angle = new(ImageObject.RotateMetadata);
            Material = new(ImageObject.MaterialMetadata);
            Type = new(TypeMetadata);
            Width = new(Shape.WidthMetadata);
            Height = new(Shape.HeightMetadata);
            Depth = new(DepthMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.GL3DObject;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Type,
            Width,
            Height,
            Depth
        };
        /// <summary>
        /// Get the coordinates.
        /// </summary>
        [DataMember]
        public Coordinate Coordinate { get; private set; }
        /// <summary>
        /// Get the scale.
        /// </summary>
        [DataMember]
        public Scale Zoom { get; private set; }
        /// <summary>
        /// Get the blend.
        /// </summary>
        [DataMember]
        public Blend Blend { get; private set; }
        /// <summary>
        /// Get the angle.
        /// </summary>
        [DataMember]
        public Rotate Angle { get; private set; }
        /// <summary>
        /// Get the material.
        /// </summary>
        [DataMember]
        public Material Material { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the object type.
        /// </summary>
        [DataMember]
        public SelectorProperty Type { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the width of the object.
        /// </summary>
        [DataMember]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the height of the object.
        /// </summary>
        [DataMember]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the depth of the object.
        /// </summary>
        [DataMember]
        public EaseProperty Depth { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var color = Blend.Color[frame];
            GLColor color4 = new(color.R, color.G, color.B, color.A);
            color4.A *= Blend.Opacity[frame];


            float scale = (float)(Zoom.Scale1[frame] / 100);
            float scalex = (float)(Zoom.ScaleX[frame] / 100) * scale;
            float scaley = (float)(Zoom.ScaleY[frame] / 100) * scale;
            float scalez = (float)(Zoom.ScaleZ[frame] / 100) * scale;

            var material = new Graphics.Material(Material.Ambient[frame], Material.Diffuse[frame], Material.Specular[frame], Material.Shininess[frame]);
            var trans = Transform.Create(
                new(Coordinate.X[frame], Coordinate.Y[frame], Coordinate.Z[frame]),
                new(Coordinate.CenterX[frame], Coordinate.CenterY[frame], Coordinate.CenterZ[frame]),
                new(Angle.RotateX[frame], Angle.RotateY[frame], Angle.RotateZ[frame]),
                new(scalex, scaley, scalez));

            if (Type.Index == 0)
            {
                using var cube = new Cube(
                    Width[frame],
                    Height[frame],
                    Depth[frame],
                    Blend.Color[frame],
                    material,
                    trans);

                Parent.Parent.GraphicsContext!.DrawCube(cube);
            }
            else
            {
                using var ball = new Ball(
                    Width[frame] * 0.5f,
                    Height[frame] * 0.5f,
                    Depth[frame] * 0.5f,
                    Blend.Color[frame],
                    material,
                    trans);

                Parent.Parent.GraphicsContext!.DrawBall(ball);
            }

            Coordinate.ResetOptional();
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Coordinate.Load(ImageObject.CoordinateMetadata);
            Zoom.Load(ImageObject.ScaleMetadata);
            Blend.Load(ImageObject.BlendMetadata);
            Angle.Load(ImageObject.RotateMetadata);
            Material.Load(ImageObject.MaterialMetadata);
            Type.Load(TypeMetadata);
            Width.Load(Shape.WidthMetadata);
            Height.Load(Shape.HeightMetadata);
            Depth.Load(DepthMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Coordinate.Unload();
            Zoom.Unload();
            Blend.Unload();
            Angle.Unload();
            Material.Unload();
            Type.Unload();
            Width.Unload();
            Height.Unload();
            Depth.Unload();
        }
    }
}