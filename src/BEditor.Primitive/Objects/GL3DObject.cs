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
        /// Defines the <see cref="Coordinate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GL3DObject, Coordinate> CoordinateProperty = ImageObject.CoordinateProperty.WithOwner<GL3DObject>(
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj);

        /// <summary>
        /// Defines the <see cref="Data.Property.PrimitiveGroup.Scale"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GL3DObject, Scale> ScaleProperty = ImageObject.ScaleProperty.WithOwner<GL3DObject>(
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj);

        /// <summary>
        /// Defines the <see cref="Blend"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GL3DObject, Blend> BlendProperty = ImageObject.BlendProperty.WithOwner<GL3DObject>(
            owner => owner.Blend,
            (owner, obj) => owner.Blend = obj);

        /// <summary>
        /// Defines the <see cref="Data.Property.PrimitiveGroup.Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GL3DObject, Rotate> RotateProperty = ImageObject.RotateProperty.WithOwner<GL3DObject>(
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj);

        /// <summary>
        /// Defines the <see cref="Data.Property.PrimitiveGroup.Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GL3DObject, Material> MaterialProperty = ImageObject.MaterialProperty.WithOwner<GL3DObject>(
            owner => owner.Material,
            (owner, obj) => owner.Material = obj);

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
#pragma warning disable CS8618
        public GL3DObject()
#pragma warning restore CS8618
        {
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
            Scale,
            Blend,
            Rotate,
            Material,
            Type,
            Width,
            Height,
            Depth
        };
        /// <summary>
        /// Get the coordinates.
        /// </summary>
        public Coordinate Coordinate { get; private set; }
        /// <summary>
        /// Get the scale.
        /// </summary>
        public Scale Scale { get; private set; }
        /// <summary>
        /// Get the blend.
        /// </summary>
        public Blend Blend { get; private set; }
        /// <summary>
        /// Get the angle.
        /// </summary>
        public Rotate Rotate { get; private set; }
        /// <summary>
        /// Get the material.
        /// </summary>
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


            float scale = (float)(Scale.Scale1[frame] / 100);
            float scalex = (float)(Scale.ScaleX[frame] / 100) * scale;
            float scaley = (float)(Scale.ScaleY[frame] / 100) * scale;
            float scalez = (float)(Scale.ScaleZ[frame] / 100) * scale;

            var material = new Graphics.Material(Material.Ambient[frame], Material.Diffuse[frame], Material.Specular[frame], Material.Shininess[frame]);
            var trans = Transform.Create(
                new(Coordinate.X[frame], Coordinate.Y[frame], Coordinate.Z[frame]),
                new(Coordinate.CenterX[frame], Coordinate.CenterY[frame], Coordinate.CenterZ[frame]),
                new(Rotate.RotateX[frame], Rotate.RotateY[frame], Rotate.RotateZ[frame]),
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
            Type.Load(TypeMetadata);
            Width.Load(Shape.WidthMetadata);
            Height.Load(Shape.HeightMetadata);
            Depth.Load(DepthMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Coordinate.Unload();
            Scale.Unload();
            Blend.Unload();
            Rotate.Unload();
            Material.Unload();
            Type.Unload();
            Width.Unload();
            Height.Unload();
            Depth.Unload();
        }
    }
}