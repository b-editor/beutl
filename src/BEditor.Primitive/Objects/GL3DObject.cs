using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Properties;
using BEditor.Graphics;

using OpenTK.Graphics.OpenGL4;

using GLColor = OpenTK.Mathematics.Color4;
using Material = BEditor.Data.Property.PrimitiveGroup.Material;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that draws a Cube, Ball, etc.
    /// </summary>
    [DataContract]
    public class GL3DObject : ObjectElement
    {
        /// <summary>
        /// Represents <see cref="Type"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Resources.Type, new string[2]
        {
            Resources.Cube,
            Resources.Ball
        });
        /// <summary>
        /// Represents <see cref="Depth"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata DepthMetadata = new("Depth", 100, float.NaN, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="GL3DObject"/> class.
        /// </summary>
        public GL3DObject()
        {
            Coordinate = new(ImageObject.CoordinateMetadata);
            Zoom = new(ImageObject.ZoomMetadata);
            Blend = new(ImageObject.BlendMetadata);
            Angle = new(ImageObject.AngleMetadata);
            Material = new(ImageObject.MaterialMetadata);
            Type = new(TypeMetadata);
            Width = new(Figure.WidthMetadata);
            Height = new(Figure.HeightMetadata);
            Depth = new(DepthMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources._3DObject;
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
        [DataMember(Order = 0)]
        public Coordinate Coordinate { get; private set; }
        /// <summary>
        /// Get the scale.
        /// </summary>
        [DataMember(Order = 1)]
        public Zoom Zoom { get; private set; }
        /// <summary>
        /// Get the blend.
        /// </summary>
        [DataMember(Order = 2)]
        public Blend Blend { get; private set; }
        /// <summary>
        /// Get the angle.
        /// </summary>
        [DataMember(Order = 3)]
        public Angle Angle { get; private set; }
        /// <summary>
        /// Get the material.
        /// </summary>
        [DataMember(Order = 4)]
        public Material Material { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> to select the object type.
        /// </summary>
        [DataMember(Order = 5)]
        public SelectorProperty Type { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the width of the object.
        /// </summary>
        [DataMember(Order = 6)]
        public EaseProperty Width { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the height of the object.
        /// </summary>
        [DataMember(Order = 7)]
        public EaseProperty Height { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the depth of the object.
        /// </summary>
        [DataMember(Order = 8)]
        public EaseProperty Depth { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var color = Blend.Color[frame];
            GLColor color4 = new(color.R, color.G, color.B, color.A);
            color4.A *= Blend.Alpha[frame];


            float scale = (float)(Zoom.Scale[frame] / 100);
            float scalex = (float)(Zoom.ScaleX[frame] / 100) * scale;
            float scaley = (float)(Zoom.ScaleY[frame] / 100) * scale;
            float scalez = (float)(Zoom.ScaleZ[frame] / 100) * scale;


            Parent!.Parent!.GraphicsContext!.MakeCurrent();

            if (Type.Index == 0)
            {
                using var cube = new Cube(
                    Width[frame],
                    Height[frame],
                    Depth[frame],
                    Blend.Color[frame])
                {
                    Material= new(Material.Ambient[frame], Material.Diffuse[frame], Material.Specular[frame], Material.Shininess[frame])
                };

                var trans = Transform.Create(
                    new(Coordinate.X[frame], Coordinate.Y[frame], Coordinate.Z[frame]),
                    new(Coordinate.CenterX[frame], Coordinate.CenterY[frame], Coordinate.CenterZ[frame]),
                    new(Angle.AngleX[frame], Angle.AngleY[frame], Angle.AngleZ[frame]),
                    new(scalex, scaley, scalez));

                Parent.Parent.GraphicsContext.DrawCube(cube, trans);
            }
            else
            {
                using var ball = new Ball(
                    Width[frame] * 0.5f,
                    Height[frame] * 0.5f,
                    Depth[frame] * 0.5f,
                    Blend.Color[frame]);

                var trans = Transform.Create(
                    new(Coordinate.X[frame], Coordinate.Y[frame], Coordinate.Z[frame]),
                    new(Coordinate.CenterX[frame], Coordinate.CenterY[frame], Coordinate.CenterZ[frame]),
                    new(Angle.AngleX[frame], Angle.AngleY[frame], Angle.AngleZ[frame]),
                    new(scalex, scaley, scalez));

                Parent.Parent.GraphicsContext.DrawBall(ball, trans);
            }

            Coordinate.ResetOptional();
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Coordinate.Load(ImageObject.CoordinateMetadata);
            Zoom.Load(ImageObject.ZoomMetadata);
            Blend.Load(ImageObject.BlendMetadata);
            Angle.Load(ImageObject.AngleMetadata);
            Material.Load(ImageObject.MaterialMetadata);
            Type.Load(TypeMetadata);
            Width.Load(Figure.WidthMetadata);
            Height.Load(Figure.HeightMetadata);
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
