using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Data.Property.PrimitiveGroup;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace BEditor.Core.Data.Primitive
{
    /// <summary>
    /// Represents the base class for drawing images.
    /// </summary>
    [DataContract]
    public abstract class ImageObject : ObjectElement
    {
        /// <summary>
        /// Represents <see cref="Coordinate"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata CoordinateMetadata = new(Resources.Coordinate);
        /// <summary>
        /// Represents <see cref="Zoom"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata ZoomMetadata = new(Resources.Zoom);
        /// <summary>
        /// Represents <see cref="Blend"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata BlendMetadata = new(Resources.Blend);
        /// <summary>
        /// Represents <see cref="Angle"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata AngleMetadata = new(Resources.Angle);
        /// <summary>
        /// Represents <see cref="Material"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata MaterialMetadata = new(Resources.Material);

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageObject"/> class.
        /// </summary>
        public ImageObject()
        {
            Coordinate = new(CoordinateMetadata);
            Zoom = new(ZoomMetadata);
            Blend = new(BlendMetadata);
            Angle = new(AngleMetadata);
            Material = new(MaterialMetadata);
        }

        /// <inheritdoc/>
        public override string Name => "";
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
        public Property.PrimitiveGroup.Material Material { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            var base_img = OnRender(args);

            if (base_img is null)
            {
                Coordinate.ResetOptional();
                return;
            }

            var imageArgs = new EffectRenderArgs<Image<BGRA32>>(args.Frame, base_img, args.Type);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();
            for (int i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Render(imageArgs);
                }
                else
                {
                    effect.Render(args);
                }


                if (args.Handled)
                {
                    Coordinate.ResetOptional();
                    return;
                }
            }


            Draw(imageArgs.Value, args);
            base_img?.Dispose();
            imageArgs.Value?.Dispose();

            Coordinate.ResetOptional();
        }
        private void Draw(Image<BGRA32> image, EffectRenderArgs args)
        {
            #region 
            var frame = args.Frame;

            float alpha = (float)(Blend.Alpha.GetValue(frame) / 100);

            var scale = (float)(Zoom.Scale.GetValue(frame) / 100);
            var scalex = (float)(Zoom.ScaleX.GetValue(frame) / 100) * scale;
            var scaley = (float)(Zoom.ScaleY.GetValue(frame) / 100) * scale;
            var scalez = (float)(Zoom.ScaleZ.GetValue(frame) / 100) * scale;

            var coordinate = new System.Numerics.Vector3(
                Coordinate.X.GetValue(frame),
                Coordinate.Y.GetValue(frame),
                Coordinate.Z.GetValue(frame));

            var center = new System.Numerics.Vector3(
                Coordinate.CenterX.GetValue(frame),
                Coordinate.CenterY.GetValue(frame),
                Coordinate.CenterZ.GetValue(frame));


            var nx = Angle.AngleX.GetValue(frame);
            var ny = Angle.AngleY.GetValue(frame);
            var nz = Angle.AngleZ.GetValue(frame);

            Color ambient = Material.Ambient.GetValue(frame);
            Color diffuse = Material.Diffuse.GetValue(frame);
            Color specular = Material.Specular.GetValue(frame);
            float shininess = Material.Shininess.GetValue(frame);
            var c = Blend.Color.GetValue(frame);
            var color = Color.FromARGB((byte)(c.A * alpha), c.R, c.G, c.B);

            #endregion

            var context = Parent?.Parent.GraphicsContext!;

            using var texture = Texture.FromImage(image);

            GL.Enable(EnableCap.Blend);

            //GL.Color4(color.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Ambient, ambient.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, diffuse.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Specular, specular.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            context.DrawTexture(texture, Transform.Create(coordinate, center, new(nx, ny, nz), new(scalex, scaley, scalez)), color, () =>
            {
                var blendFunc = Blend.BlentFunc[Blend.BlendType.Index];

                blendFunc?.Invoke();
                if (blendFunc is null)
                {
                    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                }
            });
        }
        /// <summary>
        /// Render the image.
        /// </summary>
        public void Render(EffectRenderArgs args, out Image<BGRA32>? image)
        {
            var base_img = OnRender(args);

            if (base_img is null)
            {
                Coordinate.ResetOptional();
                image = null;
                return;
            }

            var imageArgs = new EffectRenderArgs<Image<BGRA32>>(args.Frame, base_img, args.Type);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();
            for (int i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Render(imageArgs);
                }
                else
                {
                    effect.Render(args);
                }


                if (args.Handled)
                {
                    Coordinate.ResetOptional();
                    image = imageArgs.Value;
                    return;
                }
            }

            image = imageArgs.Value;

            if (imageArgs.Value != base_img)
            {
                base_img?.Dispose();
            }
        }
        /// <inheritdoc/>
        protected abstract Image<BGRA32>? OnRender(EffectRenderArgs args);
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Coordinate.Load(CoordinateMetadata);
            Zoom.Load(ZoomMetadata);
            Blend.Load(BlendMetadata);
            Angle.Load(AngleMetadata);
            Material.Load(MaterialMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Coordinate.Unload();
            Zoom.Unload();
            Blend.Unload();
            Angle.Unload();
            Material.Unload();
        }
        /// <inheritdoc/>
        public override bool EffectFilter(EffectElement effect)
        {
            return effect is ImageEffect;
        }
    }
}
