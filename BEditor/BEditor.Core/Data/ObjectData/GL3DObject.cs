using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.Default;
using BEditor.Core.Media;
using BEditor.Core.Renderer;

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

namespace BEditor.Core.Data.ObjectData
{
    [DataContract(Namespace = "")]
    public class GL3DObject : ObjectElement
    {
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Resources.Type, new string[2]
        {
            Core.Properties.Resources.Cube,
            Core.Properties.Resources.Ball
        });
        public static readonly EasePropertyMetadata WeightMetadata = new("Weight", 100, float.NaN, 0);

        #region ObjectElement

        public override string Name => Core.Properties.Resources._3DObject;

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
                    BEditor.Core.Renderer.Graphics.DrawCube(Width.GetValue(frame),
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
                    BEditor.Core.Renderer.Graphics.DrawBall(Weight.GetValue(frame),
                                        Material.Ambient.GetValue(frame),
                                        Material.Diffuse.GetValue(frame),
                                        Material.Specular.GetValue(frame),
                                        Material.Shininess.GetValue(frame));
                };
            }

            Parent.Parent.RenderingContext.MakeCurrent();
            BEditor.Core.Renderer.Graphics.Paint(new Point3(Coordinate.X.GetValue(frame),
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
        [PropertyMetadata(nameof(CoordinateMetadata), typeof(ImageObject))]
        public Coordinate Coordinate { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ZoomMetadata), typeof(ImageObject))]
        public Zoom Zoom { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(BlendMetadata), typeof(ImageObject))]
        public Blend Blend { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(AngleMetadata), typeof(ImageObject))]
        public Angle Angle { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(MaterialMetadata), typeof(ImageObject))]
        public Material Material { get; private set; }

        [DataMember(Order = 5)]
        [PropertyMetadata(nameof(TypeMetadata), typeof(GL3DObject))]
        public SelectorProperty Type { get; private set; }

        [DataMember(Order = 6)]
        [PropertyMetadata(nameof(WidthMetadata), typeof(DefaultData.Figure))]
        public EaseProperty Width { get; private set; }

        [DataMember(Order = 7)]
        [PropertyMetadata(nameof(HeightMetadata), typeof(DefaultData.Figure))]
        public EaseProperty Height { get; private set; }

        [DataMember(Order = 8)]
        [PropertyMetadata(nameof(WeightMetadata), typeof(GL3DObject))]
        public EaseProperty Weight { get; private set; }

    }
}
