using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.Property;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Core.Graphics;
using BEditor.Core.Extensions;
using BEditor.Core.Data.Primitive.Objects.PrimitiveImages;

using OpenTK.Graphics.OpenGL;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract(Namespace = "")]
    public class GL3DObject : ObjectElement
    {
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Resources.Type, new string[2]
        {
            Resources.Cube,
            Resources.Ball
        });
        public static readonly EasePropertyMetadata WeightMetadata = new("Weight", 100, float.NaN, 0);

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
            Weight = new(WeightMetadata);
        }

        #region Properties

        public override string Name => Resources._3DObject;

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
            Weight
        };


        [DataMember(Order = 0)]
        public Coordinate Coordinate { get; private set; }

        [DataMember(Order = 1)]
        public Zoom Zoom { get; private set; }

        [DataMember(Order = 2)]
        public Blend Blend { get; private set; }

        [DataMember(Order = 3)]
        public Angle Angle { get; private set; }

        [DataMember(Order = 4)]
        public Material Material { get; private set; }

        [DataMember(Order = 5)]
        public SelectorProperty Type { get; private set; }

        [DataMember(Order = 6)]
        public EaseProperty Width { get; private set; }

        [DataMember(Order = 7)]
        public EaseProperty Height { get; private set; }

        [DataMember(Order = 8)]
        public EaseProperty Weight { get; private set; }

        #endregion

        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            Action action;
            GLColor color4 = Blend.Color.GetValue(frame);
            color4.A *= Blend.Alpha.GetValue(frame);


            float scale = (float)(Zoom.Scale.GetValue(frame) / 100);
            float scalex = (float)(Zoom.ScaleX.GetValue(frame) / 100) * scale;
            float scaley = (float)(Zoom.ScaleY.GetValue(frame) / 100) * scale;
            float scalez = (float)(Zoom.ScaleZ.GetValue(frame) / 100) * scale;


            if (Type.Index == 0)
            {
                action = () =>
                {
                    GL.Color4(color4);
                    GL.Scale(scalex, scaley, scalez);
                    GLTK.DrawCube(
                        Width.GetValue(frame),
                        Height.GetValue(frame),
                        Weight.GetValue(frame),
                        Material.Ambient.GetValue(frame),
                        Material.Diffuse.GetValue(frame),
                        Material.Specular.GetValue(frame),
                        Material.Shininess.GetValue(frame));
                };
            }
            else
            {
                action = () =>
                {
                    GL.Color4(color4);
                    GL.Scale(scalex, scaley, scalez);
                    GLTK.DrawBall(
                        Weight.GetValue(frame),
                        Material.Ambient.GetValue(frame),
                        Material.Diffuse.GetValue(frame),
                        Material.Specular.GetValue(frame),
                        Material.Shininess.GetValue(frame));
                };
            }

            Parent.Parent.GraphicsContext.MakeCurrent();
            GLTK.Paint(
                new System.Numerics.Vector3(
                    Coordinate.X.GetValue(frame),
                    Coordinate.Y.GetValue(frame),
                    Coordinate.Z.GetValue(frame)),
                Angle.AngleX.GetValue(frame),
                Angle.AngleY.GetValue(frame),
                Angle.AngleZ.GetValue(frame),
                new System.Numerics.Vector3(
                    Coordinate.CenterX.GetValue(frame),
                    Coordinate.CenterY.GetValue(frame),
                    Coordinate.CenterZ.GetValue(frame)),
                action,
                Blend.BlentFunc[Blend.BlendType.Index]);

            Coordinate.ResetOptional();
        }
        public override void PropertyLoaded()
        {
            Coordinate.ExecuteLoaded(ImageObject.CoordinateMetadata);
            Zoom.ExecuteLoaded(ImageObject.ZoomMetadata);
            Blend.ExecuteLoaded(ImageObject.BlendMetadata);
            Angle.ExecuteLoaded(ImageObject.AngleMetadata);
            Material.ExecuteLoaded(ImageObject.MaterialMetadata);
            Type.ExecuteLoaded(TypeMetadata);
            Width.ExecuteLoaded(Figure.WidthMetadata);
            Height.ExecuteLoaded(Figure.HeightMetadata);
            Weight.ExecuteLoaded(WeightMetadata);
        }
    }
}
