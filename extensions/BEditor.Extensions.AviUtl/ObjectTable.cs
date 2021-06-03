using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Media.Decoding;

using Microsoft.Extensions.DependencyInjection;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public class ObjectTable
    {
        internal delegate int RandomDelegate(int st_num, int ed_num, int? seed = null, int? frame = null);

        [AllowNull]
        internal static GraphicsContext _sharedGraphics;
        private readonly ImageObject _imageobj;
        private readonly EffectApplyArgs<Image<BGRA32>> _args;
        private readonly Frame _frame;
        private readonly Project _proj;
        private readonly Scene _scene;
        private readonly ClipElement _clip;
        // クリップからの現在のフレーム
        private readonly Frame _rframe;
        private readonly CustomSettings _settings;
        private DrawTarget _drawTarget = DrawTarget.FrameBuffer;
        private Image<BGRA32> _img;
        private Font _font;
        private int _fontsize = 16;
        private Color _fontcolor = Colors.White;

        public ObjectTable(EffectApplyArgs<Image<BGRA32>> args, ImageObject image)
        {
            _args = args;
            _frame = args.Frame;
            _img = args.Value;
            _imageobj = image;
            _clip = image.Parent;
            _scene = image.Parent.Parent;
            _proj = image.Parent.Parent.Parent;
            _font = FontManager.Default.LoadedFonts.First();
            _rframe = args.Frame - image.Parent.Start;
            _settings = (CustomSettings)Plugin.Default.Settings;

            if (_sharedGraphics is null)
            {
                _sharedGraphics = new(args.Value.Width, args.Value.Height);
            }
            else
            {
                _sharedGraphics.SetSize(args.Value.Size);
            }
        }

#pragma warning disable IDE1006, CA1822, IDE0060

        #region Properties
        public float ox
        {
            get => _imageobj.Coordinate.CenterX.Optional;
            set => _imageobj.Coordinate.CenterX.Optional = value;
        }

        public float oy
        {
            get => _settings.ReverseYAsis ? -_imageobj.Coordinate.CenterY.Optional : _imageobj.Coordinate.CenterY.Optional;
            set => _imageobj.Coordinate.CenterY.Optional = _settings.ReverseYAsis ? -value : value;
        }

        public float oz
        {
            get => _settings.ReverseYAsis ? -_imageobj.Coordinate.CenterZ.Optional : _imageobj.Coordinate.CenterZ.Optional;
            set => _imageobj.Coordinate.CenterZ.Optional = _settings.ReverseYAsis ? -value : value;
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
            get => _settings.ReverseYAsis ? -_imageobj.Coordinate.CenterY[_frame] : _imageobj.Coordinate.CenterY[_frame];
            set => _imageobj.Coordinate.CenterY.Optional = _settings.ReverseYAsis ? -(value - cy) : (value - cy);
        }

        public float cz
        {
            get => _settings.ReverseYAsis ? -_imageobj.Coordinate.CenterZ[_frame] : _imageobj.Coordinate.CenterZ[_frame];
            set => _imageobj.Coordinate.CenterZ.Optional = _settings.ReverseYAsis ? -(value - cz) : (value - cz);
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
            get => ToAspect(_imageobj.Scale.ScaleX[_frame], _imageobj.Scale.ScaleY[_frame]);
            set
            {
                var size = ToSize(MathF.Max(_imageobj.Scale.ScaleX[_frame], _imageobj.Scale.ScaleY[_frame]), value);
                zoom_w = size.Width / 100f;
                zoom_h = size.Height / 100f;
            }
        }

        public float zoom_w
        {
            get => _imageobj.Scale.ScaleX[_frame] / 100;
            set => _imageobj.Scale.ScaleX.Optional = (value - zoom_w) * 100;
        }

        public float zoom_h
        {
            get => _imageobj.Scale.ScaleY[_frame] / 100;
            set => _imageobj.Scale.ScaleY.Optional = (value - zoom_h) * 100;
        }

        public float x => _imageobj.Coordinate.X[_frame];

        public float y => _settings.ReverseYAsis ? -_imageobj.Coordinate.Y[_frame] : _imageobj.Coordinate.Y[_frame];

        public float z => _settings.ReverseYAsis ? -_imageobj.Coordinate.Z[_frame] : _imageobj.Coordinate.Z[_frame];

        public int w => _img.Width;

        public int h => _img.Height;

        public int screen_w => _scene.Width;

        public int screen_h => _scene.Height;

        public int framerate => _proj.Framerate;

        public int frame => _rframe;

        public double time => _rframe.ToSeconds(framerate);

        public int totalframe => _imageobj.Parent.Length;

        public double totaltime => _imageobj.Parent.Length.ToSeconds(framerate);

        public int layer => _imageobj.Parent.Layer;

        public int index => 0;

        public int num => 1;

        public float track0 { get; set; }

        public float track1 { get; set; }

        public float track2 { get; set; }

        public float track3 { get; set; }

        public bool check0 { get; set; }

        public int color { get; set; }
        #endregion

        #region Methods
        public void mes(string text)
        {
            _imageobj.ServiceProvider?.GetService<IMessage>()?.Snackbar(text);
        }

        // Todo
        public void effect(string name, params object[] param)
        {
            throw new NotImplementedException();
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
            if (_settings.ReverseYAsis)
            {
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, -y, -z), new(ox, -oy, -oz), new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(new(ox, -oy, -oz), default, new(rx, ry, rz), new(zoom, zoom, zoom));
            }
            else
            {
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, y, z), new(ox, oy, oz), new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(new(ox, oy, oz), default, new(rx, ry, rz), new(zoom, zoom, zoom));
            }

            texture.Color = Color.FromARGB((byte)(255 * alpha), 255, 255, 255);
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
            Texture texture;

            if (_settings.ReverseYAsis)
            {
                texture = Texture.FromImage(_img, new[]
                {
                    x0, -y0, -z0,  0, 0,
                    x1, -y1, -z1,  1, 0,
                    x2, -y2, -z2,  1, 1,
                    x3, -y3, -z3,  0, 1,
                });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, -y, -z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            else
            {
                texture = Texture.FromImage(_img, new[]
                {
                    x0, y0, z0,  0, 0,
                    x1, y1, z1,  1, 0,
                    x2, y2, z2,  1, 1,
                    x3, y3, z3,  0, 1,
                });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            texture.Color = Color.FromARGB((byte)(255 * alpha), 255, 255, 255);

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
            Texture texture;

            if (_settings.ReverseYAsis)
            {
                texture = Texture.FromImage(_img, new[]
                {
                    x0, -y0, -z0,  u0 / w, v0 / h,
                    x1, -y1, -z1,  u1 / w, v1 / h,
                    x2, -y2, -z2,  u2 / w, v2 / h,
                    x3, -y3, -z3,  u3 / w, v3 / h,
                });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, -y, -z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            else
            {
                texture = Texture.FromImage(_img, new[]
                {
                    x0, y0, z0,  u0 / w, v0 / h,
                    x1, y1, z1,  u1 / w, v1 / h,
                    x2, y2, z2,  u2 / w, v2 / h,
                    x3, y3, z3,  u3 / w, v3 / h,
                });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }

            texture.Color = Color.FromARGB((byte)(255 * alpha), 255, 255, 255);

            ctxt.DrawTexture(texture);
        }

        public void load(string type, params dynamic[] args)
        {
            _img.Dispose();
            var context = _imageobj.Parent.Parent.GraphicsContext!;
            switch (type)
            {
                case "movie":
                    _img = LoadMovie(
                        GetArgValue(args, 0, null),
                        GetArgValue(args, 1, time),
                        GetArgValue(args, 2, 0));
                    break;
                case "image":
                    _img = Image.Decode(GetArgValue(args, 0, null));
                    break;
                case "text":
                    _img = Image.Text(
                        GetArgValue(args, 0, ""),
                        _font,
                        _fontsize,
                        _fontcolor,
                        HorizontalAlign.Left,
                        VerticalAlign.Top);
                    break;
                case "figure":
                    var name = GetArgValue(args, 0, "円");
                    var color = Color.FromARGB(GetArgValue(args, 1, 0xffffff));
                    color = Color.FromARGB(255, color.R, color.G, color.B);
                    var size = GetArgValue(args, 2, 100);
                    var line = GetArgValue(args, 3, size);

                    if (name is "円")
                    {
                        _img = Image.Ellipse(size, size, line, color);
                    }
                    else if (name is "四角形")
                    {
                        _img = Image.Rect(size, size, line, color);
                    }
                    else if (name is "三角形")
                    {
                        _img = Image.Polygon(3, size, size, color);
                    }
                    else if (name is "五角形")
                    {
                        _img = Image.Polygon(5, size, size, color);
                    }
                    else if (name is "六角形")
                    {
                        _img = Image.Polygon(6, size, size, color);
                    }
                    break;
                case "tempbuffer":
                    context = _sharedGraphics;
                    goto case "framebuffer";
                case "framebuffer":
                    var x = GetArgValue(args, 0, 0);
                    var y = GetArgValue(args, 1, 0);
                    var h = GetArgValue(args, 2, context.Width);
                    var w = GetArgValue(args, 3, context.Height);
                    var buffer = new Image<BGRA32>(context.Width, context.Height);

                    context.ReadImage(buffer);

                    _img = buffer[new Rectangle(x, y, w, h)];
                    buffer.Dispose();
                    break;
                default:
                    throw new NotSupportedException($"{type} は対応していません");
            }
        }

        public void setfont(string name, int size, int type = 0, int col1 = 0xffffff, int col2 = 0xffffff)
        {
            _font = FontManager.Default.Find(f => f.FamilyName == name) ?? _font;
            _fontsize = size;
            _fontcolor = Color.FromARGB(col1);
        }

        public int rand(int st_num, int ed_num, int? seed = null, int? frame = null)
        {
            seed ??= Math.Abs(_imageobj.Id.GetHashCode());

            if (seed < 0)
            {
                frame ??= _frame;
                var rand = new RandomStruct((int)seed);
                var value = 0;

                for (var i = 0; i < frame; i++)
                {
                    value = rand.Next(st_num, ed_num);
                }

                return value;
            }
            else
            {
                frame ??= this.frame;
                var rand = new RandomStruct((int)seed);
                var value = 0;

                for (var i = 0; i < frame; i++)
                {
                    value = rand.Next(st_num, ed_num);
                }

                return value;
            }
        }

        public void setoption(string name, params dynamic[] value)
        {
            switch (name)
            {
                case "drawtarget":
                    if (value[0] == "framebuffer")
                    {
                        _drawTarget = DrawTarget.FrameBuffer;
                    }
                    else if (value[0] == "tempbuffer")
                    {
                        var w = GetArgValue(value, 1, _sharedGraphics.Width);
                        var h = GetArgValue(value, 2, _sharedGraphics.Height);

                        _drawTarget = DrawTarget.TempBuffer;
                        _sharedGraphics.SetSize(new(w, h));
                    }
                    break;
                case "draw_state":
                    _args.Handled = GetArgValue(value, 0, _args.Handled);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        //public dynamic getoption(string name, params dynamic[] value)
        //{

        //}

        public LuaResult interpolation(
            float time,
            float x0, float y0, float z0,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3)
        {
            var result = InterpolationPrivate(time, x0, y0, z0, x1, y1, z1, x2, y2, z2, x3, y3, z3);
            return new(result.x, result.y, result.z);
        }

        public LuaResult interpolation(
            float time,
            float x0, float y0,
            float x1, float y1,
            float x2, float y2,
            float x3, float y3)
        {
            var result = InterpolationPrivate(time, x0, y0, 0, x1, y1, 0, x2, y2, 0, x3, y3, 0);
            return new(result.x, result.y);
        }

        public LuaResult interpolation(float time, float x0, float x1, float x2, float x3)
        {
            var result = InterpolationPrivate(time, x0, 0, 0, x1, 0, 0, x2, 0, 0, x3, 0, 0);
            return new(result.x);
        }
        #endregion

#pragma warning restore IDE1006, CA1822, IDE0060

        private (float x, float y, float z) InterpolationPrivate(
            float time,
            float x0, float y0, float z0,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3)
        {
            if (_settings.ReverseYAsis)
            {
                var curve = new BezierCurveCubic(
                    new(x0, -y0, -z0),
                    new(x3, -y3, -z3),
                    new(x1, -y1, -z1),
                    new(x2, -y2, -z2));

                var result = curve.CalculatePoint(time);
                return (result.X, result.Y, result.Z);
            }
            else
            {
                var curve = new BezierCurveCubic(
                    new(x0, y0, z0),
                    new(x3, y3, z3),
                    new(x1, y1, z1),
                    new(x2, y2, z2));

                var result = curve.CalculatePoint(time);
                return (result.X, result.Y, result.Z);
            }
        }

        private static dynamic? GetArgValue(dynamic[] args, int index, dynamic? @default)
        {
            if (index < args.Length)
            {
                return args[index];
            }
            else
            {
                return @default;
            }
        }

        private static unsafe Image<BGRA32> LoadMovie(string? file, TimeSpan time, int flag)
        {
            if (file is null) return new(1, 1);

            using var decoder = MediaFile.Open(file, new MediaOptions() { StreamsToLoad = MediaMode.Video });
            if (decoder.Video is null) return new(1, 1);
            var decoded = decoder.Video.GetFrame(time);

            if (flag is 1)
            {
                fixed (BGRA32* data = decoded.Data)
                {
                    Image.PixelOperate(decoded.Width * decoded.Height, new SetAlphaOperation(data, 255));
                }
            }

            return decoded;
        }

        private GraphicsContext GetContext()
        {
            return _drawTarget is DrawTarget.FrameBuffer ? _imageobj.Parent.Parent.GraphicsContext! : _sharedGraphics;
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