using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.Default;
using BEditor.Core.Media;
using BEditor.Core.Renderings;

using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

using static BEditor.Core.Data.ObjectData.DefaultData.Figure;
using static BEditor.Core.Data.ObjectData.ImageObject;
using BEditor.Core.Properties;
using BEditor.Core.Graphics;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.ObjectData
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

        #region ObjectElement

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
                    GLTK.DrawCube(Width.GetValue(frame),
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
                    GLTK.DrawBall(Weight.GetValue(frame),
                                        Material.Ambient.GetValue(frame),
                                        Material.Diffuse.GetValue(frame),
                                        Material.Specular.GetValue(frame),
                                        Material.Shininess.GetValue(frame));
                };
            }

            Parent.Parent.GraphicsContext.MakeCurrent();
            GLTK.Paint(new Point3(Coordinate.X.GetValue(frame),
                                                           Coordinate.Y.GetValue(frame),
                                                           Coordinate.Z.GetValue(frame)),
                                        Angle.AngleX.GetValue(frame),
                                        Angle.AngleY.GetValue(frame),
                                        Angle.AngleZ.GetValue(frame),
                                        new Point3(Coordinate.CenterX.GetValue(frame),
                                                           Coordinate.CenterY.GetValue(frame),
                                                           Coordinate.CenterZ.GetValue(frame)),
                                        action,
                                        Blend.BlentFunc[Blend.BlendType.Index]
                                        );

            Coordinate.ResetOptional();
        }

        public override void PropertyLoaded()
        {
            Coordinate.ExecuteLoaded(CoordinateMetadata);
            Zoom.ExecuteLoaded(ZoomMetadata);
            Blend.ExecuteLoaded(BlendMetadata);
            Angle.ExecuteLoaded(AngleMetadata);
            Material.ExecuteLoaded(MaterialMetadata);
            Type.ExecuteLoaded(TypeMetadata);
            Width.ExecuteLoaded(WidthMetadata);
            Height.ExecuteLoaded(HeightMetadata);
            Weight.ExecuteLoaded(WeightMetadata);
        }

        #endregion

        public GL3DObject()
        {
            Coordinate = new(CoordinateMetadata);
            Zoom = new(ZoomMetadata);
            Blend = new(BlendMetadata);
            Angle = new(AngleMetadata);
            Material = new(MaterialMetadata);
            Type = new(TypeMetadata);
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Weight = new(WeightMetadata);
        }

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

    }
}
