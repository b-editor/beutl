using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace BEditor.Data.Primitive
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
        public override async void Render(EffectRenderArgs args)
        {
            var imgs_args = new EffectRenderArgs<IEnumerable<ImageInfo>>(args.Frame, Enumerable.Empty<ImageInfo>(), args.Type);
            OnRender(imgs_args);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();


            LoadEffect(imgs_args, list);

            // ここで遅延読み込み
            var img_list = imgs_args.Value.ToArray();

            foreach (var img in img_list)
            {
                Draw(img, args);

                await img.Source.DisposeAsync();
            }

            Coordinate.ResetOptional();
        }
        private void LoadEffect(EffectRenderArgs<IEnumerable<ImageInfo>> args, EffectElement[] list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                var effect = list[i];

                if (effect is ObjectElement)
                {
                    continue;
                }
                else if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Render(args);
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
        }
        private void Draw(ImageInfo image, EffectRenderArgs args)
        {
            static void DrawLine(GraphicsContext context, float width, float height, Transform trans)
            {
                // 右上～右下
                context.DrawLine(new(width, height, 0), new(width, -height, 0), 1.5f, trans, Color.Light);

                // 右下～左下
                context.DrawLine(new(width, -height, 0), new(-width, -height, 0), 1.5f, trans, Color.Light);

                // 左下～左上
                context.DrawLine(new(-width, -height, 0), new(-width, height, 0), 1.5f, trans, Color.Light);

                // 左上～右上
                context.DrawLine(new(-width, height, 0), new(width, height, 0), 1.5f, trans, Color.Light);
            }

            if (image.Source.IsDisposed) return;

            #region 
            var frame = args.Frame;

            float alpha = (float)(Blend.Alpha.GetValue(frame) / 100);

            Color ambient = Material.Ambient.GetValue(frame);
            Color diffuse = Material.Diffuse.GetValue(frame);
            Color specular = Material.Specular.GetValue(frame);
            float shininess = Material.Shininess.GetValue(frame);
            var c = Blend.Color.GetValue(frame);
            var color = Color.FromARGB((byte)(c.A * alpha), c.R, c.G, c.B);

            #endregion

            var trans = GetTransform(frame) + image.Transform;
            var context = Parent?.Parent.GraphicsContext!;

            if (args.Type is RenderType.Preview)
            {
                var wHalf = (image.Source.Width / 2f) + 10;
                var hHalf = (image.Source.Height / 2f) + 10;
                DrawLine(context, wHalf, hHalf, trans);
            }

            using var texture = Texture.FromImage(image.Source);

            GL.Enable(EnableCap.Blend);

            context.DrawTexture(texture, trans, color, () =>
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
        private Transform GetTransform(Frame frame)
        {
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

            return Transform.Create(coordinate, center, new(nx, ny, nz), new(scalex, scaley, scalez));
        }
        /// <summary>
        /// Render the image.
        /// </summary>
        public void Render(EffectRenderArgs args, out Image<BGRA32>? image)
        {
            //Todo: 多重オブジェクトに対応させる
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
        /// <inheritdoc cref="Render(EffectRenderArgs)"/>
        protected abstract Image<BGRA32>? OnRender(EffectRenderArgs args);
        /// <inheritdoc cref="Render(EffectRenderArgs)"/>
        protected virtual void OnRender(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            var img = OnRender(args as EffectRenderArgs);

            if (img is null)
            {
                args.Value = Enumerable.Empty<ImageInfo>();
            }
            else
            {
                args.Value = new ImageInfo[]
                {
                    new(img, _ => default)
                };
            }
        }
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
