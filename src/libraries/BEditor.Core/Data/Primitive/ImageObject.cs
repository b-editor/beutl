// ImageObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Resources;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Data.Primitive
{
    /// <summary>
    /// Represents the base class for drawing images.
    /// </summary>
    public abstract class ImageObject : ObjectElement
    {
        /// <summary>
        /// Defines the <see cref="Coordinate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ImageObject, Coordinate> CoordinateProperty = EditingProperty.RegisterDirect<Coordinate, ImageObject>(
            nameof(Coordinate),
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj,
            EditingPropertyOptions<Coordinate>.Create(new CoordinateMetadata(Strings.Coordinate)).Serialize());

        /// <summary>
        /// Defines the <see cref="Scale"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ImageObject, Scale> ScaleProperty = EditingProperty.RegisterDirect<Scale, ImageObject>(
            nameof(Scale),
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj,
            EditingPropertyOptions<Scale>.Create(new ScaleMetadata(Strings.Scale)).Serialize());

        /// <summary>
        /// Defines the <see cref="Blend"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ImageObject, Blend> BlendProperty = EditingProperty.RegisterDirect<Blend, ImageObject>(
            nameof(Blend),
            owner => owner.Blend,
            (owner, obj) => owner.Blend = obj,
            EditingPropertyOptions<Blend>.Create(new BlendMetadata(Strings.Blend)).Serialize());

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ImageObject, Rotate> RotateProperty = EditingProperty.RegisterDirect<Rotate, ImageObject>(
            nameof(Rotate),
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj,
            EditingPropertyOptions<Rotate>.Create(new RotateMetadata(Strings.Rotate)).Serialize());

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ImageObject, Property.PrimitiveGroup.Material> MaterialProperty = EditingProperty.RegisterDirect<Property.PrimitiveGroup.Material, ImageObject>(
            nameof(Material),
            owner => owner.Material,
            (owner, obj) => owner.Material = obj,
            EditingPropertyOptions<Property.PrimitiveGroup.Material>.Create(new MaterialMetadata(Strings.Material)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageObject"/> class.
        /// </summary>
        protected ImageObject()
        {
        }

        /// <inheritdoc/>
        public override string Name => string.Empty;

        /// <summary>
        /// Gets the coordinates.
        /// </summary>
        [AllowNull]
        public Coordinate Coordinate { get; private set; }

        /// <summary>
        /// Gets the scale.
        /// </summary>
        [AllowNull]
        public Scale Scale { get; private set; }

        /// <summary>
        /// Gets the blend.
        /// </summary>
        [AllowNull]
        public Blend Blend { get; private set; }

        /// <summary>
        /// Gets the angle.
        /// </summary>
        [AllowNull]
        public Rotate Rotate { get; private set; }

        /// <summary>
        /// Gets the material.
        /// </summary>
        [AllowNull]
        public Property.PrimitiveGroup.Material Material { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            var imgs_args = new EffectApplyArgs<IEnumerable<ImageInfo>>(args.Frame, Enumerable.Empty<ImageInfo>(), args.Type);
            OnRender(imgs_args);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();

            LoadEffect(imgs_args, list);

            // ここで遅延読み込み
            var img_list = imgs_args.Value.ToArray();

            foreach (var img in img_list)
            {
                Draw(img, imgs_args);

                img.Source.Dispose();
            }

            ResetOptional();
        }

        /// <summary>
        /// <see cref="Coordinate"/>, <see cref="Scale"/>, <see cref="Rotate"/> から <see cref="Transform"/> を取得します.
        /// </summary>
        /// <param name="frame">取得するフレームです.</param>
        /// <returns><see cref="Coordinate"/>, <see cref="Scale"/>, <see cref="Rotate"/> の値から作成された <see cref="Transform"/>.</returns>
        public Transform GetTransform(Frame frame)
        {
            var scale = (float)(Scale.Scale1.GetValue(frame) / 100);
            var scalex = (float)(Scale.ScaleX.GetValue(frame) / 100) * scale;
            var scaley = (float)(Scale.ScaleY.GetValue(frame) / 100) * scale;
            var scalez = (float)(Scale.ScaleZ.GetValue(frame) / 100) * scale;

            var coordinate = new System.Numerics.Vector3(
                Coordinate.X.GetValue(frame),
                Coordinate.Y.GetValue(frame),
                Coordinate.Z.GetValue(frame));

            var center = new System.Numerics.Vector3(
                Coordinate.CenterX.GetValue(frame),
                Coordinate.CenterY.GetValue(frame),
                Coordinate.CenterZ.GetValue(frame));

            var nx = Rotate.RotateX.GetValue(frame);
            var ny = Rotate.RotateY.GetValue(frame);
            var nz = Rotate.RotateZ.GetValue(frame);

            return new Transform(coordinate, center, new(nx, ny, nz), new(scalex, scaley, scalez));
        }

        /// <summary>
        /// Render the image.
        /// </summary>
        /// <param name="args">The data used to apply the effect.</param>
        /// <param name="image">Returns the rendered image.</param>
        public void Render(EffectApplyArgs args, out Image<BGRA32>? image)
        {
            // Todo: 多重オブジェクトに対応させる
            var base_img = OnRender(args);

            if (base_img is null)
            {
                ResetOptional();
                image = null;
                return;
            }

            var imageArgs = new EffectApplyArgs<Image<BGRA32>>(args.Frame, base_img, args.Type);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();
            for (var i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Apply(imageArgs);
                }
                else
                {
                    effect.Apply(args);
                }

                if (args.Handled)
                {
                    ResetOptional();
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
        public override bool EffectFilter(EffectElement effect)
        {
            return effect is ImageEffect;
        }

        /// <inheritdoc cref="Apply(EffectApplyArgs)"/>
        protected abstract Image<BGRA32>? OnRender(EffectApplyArgs args);

        /// <inheritdoc cref="Apply(EffectApplyArgs)"/>
        protected virtual void OnRender(EffectApplyArgs<IEnumerable<ImageInfo>> args)
        {
            var img = OnRender(args as EffectApplyArgs);

            if (img is null)
            {
                args.Value = Enumerable.Empty<ImageInfo>();
            }
            else
            {
                args.Value = new ImageInfo[]
                {
                    new(img, _ => default),
                };
            }
        }

        private void ResetOptional()
        {
            Coordinate.ResetOptional();
            Rotate.ResetOptional();
            Scale.ResetOptional();
            Blend.ResetOptional();
        }

        private void LoadEffect(EffectApplyArgs<IEnumerable<ImageInfo>> args, EffectElement[] list)
        {
            for (var i = 0; i < list.Length; i++)
            {
                var effect = list[i];

                if (effect is ObjectElement)
                {
                    continue;
                }
                else if (effect is ImageEffect imageEffect)
                {
                    imageEffect.Apply(args);
                }
                else
                {
                    effect.Apply(args);
                }

                if (args.Handled)
                {
                    ResetOptional();
                    return;
                }
            }
        }

        private void Draw(ImageInfo image, EffectApplyArgs args)
        {
            static void DrawLine(GraphicsContext context, float width, float height, Transform trans)
            {
                // 右上～右下
                context.DrawLine(new(width, height, 0), new(width, -height, 0), 1.5f, trans, Colors.White);

                // 右下～左下
                context.DrawLine(new(width, -height, 0), new(-width, -height, 0), 1.5f, trans, Colors.White);

                // 左下～左上
                context.DrawLine(new(-width, -height, 0), new(-width, height, 0), 1.5f, trans, Colors.White);

                // 左上～右上
                context.DrawLine(new(-width, height, 0), new(width, height, 0), 1.5f, trans, Colors.White);
            }

            if (image.Source.IsDisposed || args.Handled)
            {
                return;
            }

            var frame = args.Frame;

            var alpha = (float)(Blend.Opacity.GetValue(frame) / 100);

            var ambient = Material.Ambient[frame];
            var diffuse = Material.Diffuse[frame];
            var specular = Material.Specular[frame];
            var shininess = Material.Shininess[frame];
            var color = Blend.Color[frame];
            color.A = (byte)(color.A * alpha);

            var trans = GetTransform(frame) + image.Transform;
            var context = Parent!.Parent.GraphicsContext!;

            if (args.Type is RenderType.Preview && Parent.Parent.SelectItem == Parent)
            {
                var wHalf = (image.Source.Width / 2f) + 10;
                var hHalf = (image.Source.Height / 2f) + 10;
                DrawLine(context, wHalf, hHalf, trans);
            }

            using var texture = Texture.FromImage(image.Source);
            texture.Material = new(ambient, diffuse, specular, shininess);
            texture.Transform = trans;
            texture.Color = color;

            GL.Enable(EnableCap.Blend);

            context.DrawTexture(texture, () =>
            {
                var blendFunc = Blend.BlentFunc[Blend.BlendType.Index];

                blendFunc?.Invoke();
                if (blendFunc is null)
                {
                    GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                }
            });
        }
    }
}