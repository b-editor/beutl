// ImageObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Resources;

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
        public static readonly DirectProperty<ImageObject, Coordinate> CoordinateProperty = EditingProperty.RegisterDirect<Coordinate, ImageObject>(
            nameof(Coordinate),
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj,
            EditingPropertyOptions<Coordinate>.Create(new CoordinateMetadata(Strings.Coordinate)).Serialize());

        /// <summary>
        /// Defines the <see cref="Scale"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageObject, Scale> ScaleProperty = EditingProperty.RegisterDirect<Scale, ImageObject>(
            nameof(Scale),
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj,
            EditingPropertyOptions<Scale>.Create(new ScaleMetadata(Strings.Scale)).Serialize());

        /// <summary>
        /// Defines the <see cref="Blend"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageObject, Blend> BlendProperty = EditingProperty.RegisterDirect<Blend, ImageObject>(
            nameof(Blend),
            owner => owner.Blend,
            (owner, obj) => owner.Blend = obj,
            EditingPropertyOptions<Blend>.Create(new BlendMetadata(Strings.Blend)).Serialize());

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageObject, Rotate> RotateProperty = EditingProperty.RegisterDirect<Rotate, ImageObject>(
            nameof(Rotate),
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj,
            EditingPropertyOptions<Rotate>.Create(new RotateMetadata(Strings.Rotate)).Serialize());

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageObject, Property.PrimitiveGroup.Material> MaterialProperty = EditingProperty.RegisterDirect<Property.PrimitiveGroup.Material, ImageObject>(
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
            if (args.Type is ApplyType.Audio) return;

            var imgs_args = new EffectApplyArgs<IEnumerable<Texture>>(args.Frame, Enumerable.Empty<Texture>(), args.Contexts, args.Type);
            OnRender(imgs_args);

            var list = Parent!.Effect.Where(x => x.IsEnabled).ToArray();

            LoadEffect(imgs_args, list);

            // ここで遅延読み込み
            var img_list = imgs_args.Value.ToArray();

            foreach (var img in img_list)
            {
                Draw(img, imgs_args);

                img.Dispose();
            }
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
            var base_img = OnRender(args);

            if (base_img is null)
            {
                image = null;
                return;
            }

            var imageArgs = new EffectApplyArgs<Image<BGRA32>>(args.Frame, base_img, args.Contexts, args.Type);

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
        protected virtual void OnRender(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            using var img = OnRender(args as EffectApplyArgs);

            if (img is null)
            {
                args.Value = Enumerable.Empty<Texture>();
            }
            else
            {
                var texture = Texture.FromImage(img);
                var frame = args.Frame;

                // Materialを設定
                var ambient = Material.Ambient[frame];
                var diffuse = Material.Diffuse[frame];
                var specular = Material.Specular[frame];
                var shininess = Material.Shininess[frame];
                texture.Material = new(ambient, diffuse, specular, shininess);

                // Blend を設定
                var alpha = Blend.Opacity[frame] / 100f;
                var color = Blend.Color[frame];
                color.A = (byte)(color.A * alpha);
                texture.Color = color;
                texture.BlendMode = (BlendMode)Blend.BlendType.Index;

                // Transformを設定
                texture.Transform += GetTransform(frame);

                args.Value = new Texture[]
                {
                    texture,
                };
            }
        }

        private static void LoadEffect(EffectApplyArgs<IEnumerable<Texture>> args, EffectElement[] list)
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
                    return;
                }
            }
        }

        private void Draw(Texture texture, EffectApplyArgs args)
        {
            const float lineWidth = 1.5F;
            static Cube CreateCube(float width, float height, float cx, float cy, Transform trans)
            {
                trans.Center += new Vector3(cx, cy, 0);
                return new Cube(MathF.Max(width, 1), MathF.Max(height, 1), lineWidth)
                {
                    Color = Colors.White,
                    Transform = trans,
                };
            }

            static void DrawLine(GraphicsContext context, float width, float height, Transform trans)
            {
                using var cube1 = CreateCube(lineWidth, height, width / 2, 0, trans);
                using var cube2 = CreateCube(width, lineWidth, 0, -height / 2, trans);
                using var cube3 = CreateCube(lineWidth, height, -width / 2, 0, trans);
                using var cube4 = CreateCube(width, lineWidth, 0, height / 2, trans);

                // 右上～右下
                context.DrawCube(cube1);

                // 右下～左下
                context.DrawCube(cube2);

                // 左下～左上
                context.DrawCube(cube3);

                // 左上～右上
                context.DrawCube(cube4);
            }

            if (texture.IsDisposed || args.Handled)
            {
                return;
            }

            var context = args.Contexts.Graphics;

            if (args.Type is ApplyType.Edit && Parent.Parent.SelectItem == Parent)
            {
                DrawLine(context, texture.Width, texture.Height, texture.Transform);
            }

            context.DrawTexture(texture);
        }
    }
}