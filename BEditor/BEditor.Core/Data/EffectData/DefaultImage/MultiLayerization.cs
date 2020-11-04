using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Data.PropertyData.Default;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Core.Renderer;

using OpenTK.Graphics.OpenGL;

#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
#endif

namespace BEditor.Core.Data.EffectData {
    public sealed class MultiLayerization : ImageEffect {
        public static readonly EasePropertyMetadata ZMetadata = new EasePropertyMetadata(Resources.Z, 50, float.NaN, 0);
        public static readonly ColorAnimationPropertyMetadata ColorMetadata = new ColorAnimationPropertyMetadata(Resources.Color, 255, 255, 255, 255, true);

        #region ImageEffect

        public override string Name => Resources.MultiLayerization;

        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Z,
            Color
        };

        public override void PreviewLoad(EffectLoadArgs args) {
            base.PreviewLoad(args);

            args.Schedules.Remove(this);
            args.Schedules.Add(this);
        }

        public override void Draw(ref Image source, EffectLoadArgs args) {
            var drawObject = (ImageObject)ClipData.Effect[0];
            var frame = args.Frame;

            Point3 coordinate = new Point3(x: drawObject.Coordinate.X.GetValue(frame),
                                             y: drawObject.Coordinate.Y.GetValue(frame),
                                             z: drawObject.Coordinate.Z.GetValue(frame));

            Point3 center = new Point3(x: drawObject.Coordinate.CenterX.GetValue(frame),
                                       y: drawObject.Coordinate.CenterY.GetValue(frame),
                                       z: drawObject.Coordinate.CenterZ.GetValue(frame));


            float nx = drawObject.Angle.AngleX.GetValue(frame);
            float ny = drawObject.Angle.AngleY.GetValue(frame);
            float nz = drawObject.Angle.AngleZ.GetValue(frame);

            //サイズを再設定
            source.AreaExpansion(1, 1, 1, 1);

            //var points = BorderFinder.Find(source);

            ClipData.Scene.RenderingContext.MakeCurrent();
            BEditor.Core.Renderer.Graphics.Paint(coordinate, nx, ny, nz, center, () => {
                GL.Color4((GLColor)Color.GetValue(frame));
                GL.Material(MaterialFace.Front, MaterialParameter.Ambient, (GLColor)Material.Ambient.GetValue(frame));
                GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, (GLColor)Material.Diffuse.GetValue(frame));
                GL.Material(MaterialFace.Front, MaterialParameter.Specular, (GLColor)Material.Specular.GetValue(frame));
                GL.Material(MaterialFace.Front, MaterialParameter.Shininess, Material.Shininess.GetValue(frame));

                float scale = (float)(drawObject.Zoom.Scale.GetValue(frame) / 100);
                float scalex = (float)(drawObject.Zoom.ScaleX.GetValue(frame) / 100) * scale;
                float scaley = (float)(drawObject.Zoom.ScaleY.GetValue(frame) / 100) * scale;
                float scalez = (float)(drawObject.Zoom.ScaleZ.GetValue(frame) / 100) * scale;

                GL.Scale(scalex, scaley, scalez);
                GL.Begin(PrimitiveType.Quads);
                {
                    //foreach(var point points) {
                    //    GL.Vertex3(point.X, point.Y, 0);
                    //}
                }
                GL.End();

                GL.Disable(EnableCap.Blend);
            });
        }

        #endregion

        public MultiLayerization() {
            Z = new EaseProperty(ZMetadata);
            Material = new Material(ImageObject.MaterialMetadata);
            Color = new ColorAnimationProperty(ColorMetadata);
        }


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(ZMetadata), typeof(MultiLayerization))]
        public EaseProperty Z { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ImageObject.MaterialMetadata), typeof(ImageObject))]
        public Material Material { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(MultiLayerization))]
        public ColorAnimationProperty Color { get; set; }
    }
}
