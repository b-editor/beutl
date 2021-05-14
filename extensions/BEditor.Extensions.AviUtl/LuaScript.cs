using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Primitive.Objects;

using Neo.IronLua;

using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using BEditor.Graphics;

namespace BEditor.Extensions.AviUtl
{
    public sealed class LuaScript : ImageEffect
    {
        public static readonly DirectEditingProperty<LuaScript, DocumentProperty> CodeProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, LuaScript>(
            nameof(Code),
            owner => owner.Code,
            (owner, obj) => owner.Code = obj,
            new DocumentPropertyMetadata(string.Empty));

        internal static readonly Lua LuaEngine = new();

        internal static readonly LuaGlobal LuaGlobal = LuaEngine.CreateEnvironment();

        static LuaScript()
        {
            //LuaGlobal.SetValue("obj", ObjectTable);
        }

        public override string Name => "スクリプト制御";

        [AllowNull]
        public DocumentProperty Code { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is ImageObject obj)
            {
                var table = new ObjectTable(args, obj);
                LuaGlobal.SetValue("obj", table);

                var result = LuaGlobal.DoChunk(Code.Value, "main");
            }
            Parent.Parent.GraphicsContext!.MakeCurrentAndBindFbo();
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Code;
        }
    }

    public enum DrawTarget
    {
        TempBuffer,
        FrameBuffer
    }

    public class ObjectTable
    {
        internal static GraphicsContext? _sharedGraphics;
        private readonly ImageObject _imageobj;
        private readonly EffectApplyArgs<Image<BGRA32>> _args;
        private readonly Frame _frame;
        private readonly Image<BGRA32> _img;
        private readonly Project _proj;
        private readonly Scene _scene;
        // クリップからの現在のフレーム
        private readonly Frame rframe;
        private DrawTarget _drawTarget = DrawTarget.FrameBuffer;

        public ObjectTable(EffectApplyArgs<Image<BGRA32>> args, ImageObject image)
        {
            _args = args;
            _frame = args.Frame;
            _img = args.Value;
            _imageobj = image;
            _scene = image.Parent.Parent;
            _proj = image.Parent.Parent.Parent;
            rframe = args.Frame - image.Parent.Start;

            if (_sharedGraphics is null)
            {
                _sharedGraphics = new(args.Value.Width, args.Value.Height);
            }
            else
            {
                _sharedGraphics.SetSize(args.Value.Size);
            }
        }

#pragma warning disable IDE1006, CA1822

        #region Properties
        public float ox
        {
            get => _imageobj.Coordinate.CenterX.Optional;
            set => _imageobj.Coordinate.CenterX.Optional = value;
        }

        public float oy
        {
            get => _imageobj.Coordinate.CenterY.Optional;
            set => _imageobj.Coordinate.CenterY.Optional = value;
        }

        public float oz
        {
            get => _imageobj.Coordinate.CenterZ.Optional;
            set => _imageobj.Coordinate.CenterZ.Optional = value;
        }

        public float rx
        {
            get => _imageobj.Rotate.RotateX[_frame];
            set => _imageobj.Rotate.RotateX.Optional = value - rx;
        }

        public float ry
        {
            get => _imageobj.Rotate.RotateY[_frame];
            set => _imageobj.Rotate.RotateY.Optional = value - ry;
        }

        public float rz
        {
            get => _imageobj.Rotate.RotateZ[_frame];
            set => _imageobj.Rotate.RotateZ.Optional = value - rz;
        }

        public float cx
        {
            get => _imageobj.Coordinate.CenterX[_frame];
            set => _imageobj.Coordinate.CenterX.Optional = value - cx;
        }

        public float cy
        {
            get => _imageobj.Coordinate.CenterY[_frame];
            set => _imageobj.Coordinate.CenterY.Optional = value - cy;
        }

        public float cz
        {
            get => _imageobj.Coordinate.CenterZ[_frame];
            set => _imageobj.Coordinate.CenterZ.Optional = value - cz;
        }

        public float zoom
        {
            get => _imageobj.Scale.Scale1[_frame] / 100;
            set => _imageobj.Scale.Scale1.Optional = (value - zoom) * 100;
        }

        public float alpha
        {
            get => _imageobj.Blend.Opacity[_frame] / 100;
            set => _imageobj.Blend.Opacity.Optional = (value - alpha) * 100;
        }

        public float aspect
        {
            get => ToAspect(zoom_w, zoom_h);
            set
            {
                var size = ToSize(MathF.Max(zoom_w, zoom_h), value);
                zoom_w = size.Width;
                zoom_h = size.Height;
            }
        }

        public float zoom_w
        {
            get => _imageobj.Scale.ScaleX[_frame];
            set => _imageobj.Scale.ScaleX.Optional = value - zoom_w;
        }

        public float zoom_h
        {
            get => _imageobj.Scale.ScaleY[_frame];
            set => _imageobj.Scale.ScaleY.Optional = value - zoom_h;
        }

        public float x => _imageobj.Coordinate.X[_frame];

        public float y => _imageobj.Coordinate.Y[_frame];

        public float z => _imageobj.Coordinate.Z[_frame];

        public int w => _img.Width;

        public int h => _img.Height;

        public int screen_w => _scene.Width;

        public int screen_h => _scene.Height;

        public int framerate => _proj.Framerate;

        public int frame => rframe;

        public double time => rframe.ToSeconds(framerate);

        public int totalframe => _imageobj.Parent.Length;

        public double totaltime => _imageobj.Parent.Length.ToSeconds(framerate);

        public int layer => _imageobj.Parent.Layer;

        public int index => 0;

        public int num => 1;
        #endregion

        #region Methods
        public void mes(string text)
        {
            _imageobj.ServiceProvider?.GetService<IMessage>()?.Snackbar(text);
        }

        // Todo
        public void effect(string name, params object[] param)
        {

        }

        /// <summary>
        /// 現在のオブジェクトを描画します。メディアオブジェクトのみ使用出来ます。
        /// </summary>
        /// <param name="ox">相対座標X</param>
        /// <param name="oy">相対座標Y</param>
        /// <param name="oz">相対座標Z</param>
        /// <param name="zoom">拡大率(1.0=等倍)</param>
        /// <param name="alpha">不透明度(0.0=透明/1.0=不透明)</param>
        /// <param name="rx">X軸回転角度(360.0で一回転)</param>
        /// <param name="ry">Z軸回転角度(360.0で一回転)</param>
        /// <param name="rz">Z軸回転角度(360.0で一回転)</param>
        public void draw(float ox = 0, float oy = 0, float oz = 0, float zoom = 1, float alpha = 1, float rx = 0, float ry = 0, float rz = 0)
        {
            var ctxt = GetContext();
            ctxt.MakeCurrentAndBindFbo();
            using var texture = Texture.FromImage(_img);
            texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                new(new(x, y, z), new(ox, oy, oz), new(rx, ry, rz), new(zoom, zoom, zoom)) :
                new(new(ox, oy, oz), default, new(rx, ry, rz), new(zoom, zoom, zoom));

            texture.Color = Color.FromARGB((byte)(255 * alpha), 1, 1, 1);

            ctxt.DrawTexture(texture);
        }

        /// <summary>
        /// 現在のオブジェクトの任意部分を任意の四角形で描画します。メディアオブジェクトのみ使用出来ます。
        /// </summary>
        /// <param name="x0">四角形の頂点0の座標X</param>
        /// <param name="y0">四角形の頂点0の座標Y</param>
        /// <param name="z0">四角形の頂点0の座標Z</param>
        /// <param name="x1">四角形の頂点1の座標X</param>
        /// <param name="y1">四角形の頂点1の座標Y</param>
        /// <param name="z1">四角形の頂点1の座標Z</param>
        /// <param name="x2">四角形の頂点2の座標X</param>
        /// <param name="y2">四角形の頂点2の座標Y</param>
        /// <param name="z2">四角形の頂点2の座標Z</param>
        /// <param name="x3">四角形の頂点3の座標X</param>
        /// <param name="y3">四角形の頂点3の座標Y</param>
        /// <param name="z3">四角形の頂点3の座標Z</param>
        public void drawpoly(
            // 四角形の頂点0の座標
            float x0, float y0, float z0,
            // 四角形の頂点1の座標
            float x1, float y1, float z1,
            // 四角形の頂点2の座標
            float x2, float y2, float z2,
            // 四角形の頂点3の座標
            float x3, float y3, float z3)
        {
            var ctxt = GetContext();
            ctxt.MakeCurrentAndBindFbo();

            using var texture = Texture.FromImage(_img, new[]
            {
                x0, y0, z0,  0, 0,
                x1, y1, z1,  1, 0,
                x2, y2, z2,  1, 1,
                x3, y3, z3,  0, 1,
            });
            texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                new(default, default, new(rx, ry, rz), new(zoom, zoom, zoom));

            texture.Color = Color.FromARGB((byte)(255 * alpha), 1, 1, 1);

            ctxt.DrawTexture(texture);
        }

        /// <summary>
        /// 現在のオブジェクトの任意部分を任意の四角形で描画します。メディアオブジェクトのみ使用出来ます。
        /// </summary>
        /// <param name="x0">四角形の頂点0の座標X</param>
        /// <param name="y0">四角形の頂点0の座標Y</param>
        /// <param name="z0">四角形の頂点0の座標Z</param>
        /// <param name="x1">四角形の頂点1の座標X</param>
        /// <param name="y1">四角形の頂点1の座標Y</param>
        /// <param name="z1">四角形の頂点1の座標Z</param>
        /// <param name="x2">四角形の頂点2の座標X</param>
        /// <param name="y2">四角形の頂点2の座標Y</param>
        /// <param name="z2">四角形の頂点2の座標Z</param>
        /// <param name="x3">四角形の頂点3の座標X</param>
        /// <param name="y3">四角形の頂点3の座標Y</param>
        /// <param name="z3">四角形の頂点3の座標Z</param>
        /// <param name="u0">頂点0に対応するオブジェクトの画像の座標X</param>
        /// <param name="v0">頂点0に対応するオブジェクトの画像の座標Y</param>
        /// <param name="u1">頂点1に対応するオブジェクトの画像の座標X</param>
        /// <param name="v1">頂点1に対応するオブジェクトの画像の座標Y</param>
        /// <param name="u2">頂点2に対応するオブジェクトの画像の座標X</param>
        /// <param name="v2">頂点2に対応するオブジェクトの画像の座標Y</param>
        /// <param name="u3">頂点3に対応するオブジェクトの画像の座標X</param>
        /// <param name="v3">頂点3に対応するオブジェクトの画像の座標Y</param>
        /// <param name="alpha">不透明度(0.0=透明/1.0=不透明)</param>
        public void drawpoly(
            float x0, float y0, float z0,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3,
            float u0, float v0,
            float u1, float v1,
            float u2, float v2,
            float u3, float v3,
            float alpha)
        {
            var ctxt = GetContext();
            ctxt.MakeCurrentAndBindFbo();
            var w = _img.Width;
            var h = _img.Height;

            using var texture = Texture.FromImage(_img, new[]
            {
                x0, y0, z0,  u0 / w, v0 / h,
                x1, y1, z1,  u1 / w, v1 / h,
                x2, y2, z2,  u2 / w, v2 / h,
                x3, y3, z3,  u3 / w, v3 / h,
            });
            texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                new(default, default, new(rx, ry, rz), new(zoom, zoom, zoom));

            texture.Color = Color.FromARGB((byte)(255 * alpha), 1, 1, 1);

            ctxt.DrawTexture(texture);
        }

        public void load(string type, string file, double time = double.NaN, int flag = 0)
        {

        }
        #endregion

#pragma warning restore IDE1006, CA1822

        private GraphicsContext GetContext()
        {
            return _drawTarget is DrawTarget.FrameBuffer ? _imageobj.Parent.Parent.GraphicsContext! : _sharedGraphics!;
        }

        internal static Size ToSize(float size, float aspect)
        {
            Size result;

            if (aspect > 0)
            {
                result = new((int)(size - (aspect * size / 100f)), (int)size);
            }
            else
            {
                result = new((int)size, (int)(size + (aspect * size / 100)));
            }

            return result;
        }

        internal static float ToAspect(float width, float height)
        {
            var max = MathF.Max(width, height);
            var min = MathF.Min(width, height);

            return (1 - (1 / max) * min) * -100;
        }
    }
}