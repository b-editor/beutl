using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics;

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
    public class ObjectTable : IDisposable
    {
        internal delegate int RandomDelegate(int st_num, int ed_num, int? seed = null, int? frame = null);

        private static readonly Dictionary<string, Image<BGRA32>> _buffers = new();
        [AllowNull]
        private static GraphicsContext _sharedGraphics;
        private readonly ImageObject _imageobj;
        private readonly EffectApplyArgs _args;
        private readonly Frame _frame;
        private readonly Project _proj;
        private readonly Scene _scene;
        private readonly ClipElement _clip;
        // クリップからの現在のフレーム
        private readonly Frame _rframe;
        private readonly CustomSettings _settings;

        private float _ox;
        private float _oy;
        private float _oz;

        private float _zoom = 1;

        private Texture _texture;
        private DrawTarget _drawTarget = DrawTarget.FrameBuffer;
        private Image<BGRA32> _img;
        private Font _font;
        private int _fontsize = 16;
        private Color _fontcolor = Colors.White;

        public ObjectTable(EffectApplyArgs args, Texture texture, ImageObject image)
        {
            _args = args;
            _frame = args.Frame;
            _texture = texture;
            _img = texture.ToImage();
            _imageobj = image;
            _clip = image.Parent;
            _scene = image.Parent.Parent;
            _proj = image.Parent.Parent.Parent;
            _font = FontManager.Default.LoadedFonts.First();
            _rframe = args.Frame - image.Parent.Start;
            _settings = (CustomSettings)Plugin.Default.Settings;

            if (_sharedGraphics is null)
            {
                _sharedGraphics = new(_img.Width, _img.Height);
            }
            else
            {
                _sharedGraphics.SetSize(_img.Size);
            }
        }

#pragma warning disable IDE1006, CA1822, IDE0060

        #region Properties
        public float ox
        {
            get => _ox;
            set
            {
                _ox = value;

                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.X += _ox;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float oy
        {
            get => _oy;
            set
            {
                _oy = _settings.ReverseYAsis ? -value : value;

                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Y += _oy;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float oz
        {
            get => _oz;
            set
            {
                _oz = value;

                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Z += _oz;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float rx
        {
            get => _texture.Transform.Rotation.X;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Rotation;
                vector.X = value;
                transform.Rotation = vector;
            }
        }

        public float ry
        {
            get => _texture.Transform.Rotation.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Rotation;
                vector.Y = value;
                transform.Rotation = vector;
            }
        }

        public float rz
        {
            get => _texture.Transform.Rotation.Z;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Rotation;
                vector.Z = value;
                transform.Rotation = vector;
            }
        }

        public float cx
        {
            get => _texture.Transform.Center.X;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.X = value;
                transform.Center = vector;
            }
        }

        public float cy
        {
            get => _texture.Transform.Center.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Y = _settings.ReverseYAsis ? -value : value;
                transform.Center = vector;
            }
        }

        public float cz
        {
            get => _texture.Transform.Center.Z;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Z = _settings.ReverseYAsis ? -value : value;
                transform.Center = vector;
            }
        }

        public float zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;
                zoom_w = zoom_w;
                zoom_h = zoom_h;
            }
        }

        public float alpha
        {
            get => _imageobj.Blend.Opacity[_frame] / 100;
            set => _imageobj.Blend.Opacity.Optional = (value - alpha) * 100;
        }

        public float aspect
        {
            get => ToAspect(_texture.Transform.Scale.X, _texture.Transform.Scale.Y);
            set
            {
                var size = ToSize(MathF.Max(_texture.Transform.Scale.X, _texture.Transform.Scale.X), value);
                zoom_w = size.Width;
                zoom_h = size.Height;
            }
        }

        public float zoom_w
        {
            get => _texture.Transform.Scale.X;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Scale;
                vector.X = value * zoom;
                transform.Scale = vector;
            }
        }

        public float zoom_h
        {
            get => _texture.Transform.Scale.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Scale;
                vector.Y = value * zoom;
                transform.Scale = vector;
            }
        }

        public float x => _texture.Transform.Position.X;

        public float y => _settings.ReverseYAsis ? -_texture.Transform.Position.Y : _texture.Transform.Position.Y;

        public float z => _settings.ReverseYAsis ? -_texture.Transform.Position.Z : _texture.Transform.Position.Z;

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

        public int index { get; set; }

        public int num { get; set; }

        public float track0 { get; set; }

        public float track1 { get; set; }

        public float track2 { get; set; }

        public float track3 { get; set; }

        public bool check0 { get; set; }

        public int color { get; set; }

        internal string BasePath { get; set; } = string.Empty;
        #endregion

        #region Methods
        public void mes(string text)
        {
            _imageobj.ServiceProvider?.GetService<IMessage>()?.Snackbar(text);
        }

        // Todo: エフェクトを実装
        public void effect(string name, params object[] param)
        {
            this.Apply(ref _img, name, param);
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
            ctxt.PlatformImpl.MakeCurrent();
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

            texture.Color = Color.FromArgb((byte)(255 * alpha), 255, 255, 255);
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
            ctxt.PlatformImpl.MakeCurrent();
            Texture texture;

            if (_settings.ReverseYAsis)
            {
                texture = Texture.FromImage(
                    _img,
                    new VertexPositionTexture[]
                    {
                        new(new(x0, -y0, -z0), new(0, 0)),
                        new(new(x1, -y1, -z1), new(1, 0)),
                        new(new(x2, -y2, -z2), new(1, 1)),
                        new(new(x3, -y3, -z3), new(0, 1)),
                    });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, -y, -z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            else
            {
                texture = Texture.FromImage(
                    _img,
                    new VertexPositionTexture[]
                    {
                        new(new(x0, y0, z0), new(0, 0)),
                        new(new(x1, y1, z1), new(1, 0)),
                        new(new(x2, y2, z2), new(1, 1)),
                        new(new(x3, y3, z3), new(0, 1)),
                    });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            texture.Color = Color.FromArgb((byte)(255 * alpha), 255, 255, 255);

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
            ctxt.PlatformImpl.MakeCurrent();
            var w = _img.Width;
            var h = _img.Height;
            Texture texture;

            if (_settings.ReverseYAsis)
            {
                texture = Texture.FromImage(
                    _img,
                    new VertexPositionTexture[]
                    {
                        new(new(x0, -y0, -z0), new(u0 / w, v0 / h)),
                        new(new(x1, -y1, -z1), new(u1 / w, v1 / h)),
                        new(new(x2, -y2, -z2), new(u2 / w, v2 / h)),
                        new(new(x3, -y3, -z3), new(u3 / w, v3 / h)),
                    });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, -y, -z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }
            else
            {
                texture = Texture.FromImage(
                    _img,
                    new VertexPositionTexture[]
                    {
                        new(new(x0, y0, z0), new(u0 / w, v0 / h)),
                        new(new(x1, y1, z1), new(u1 / w, v1 / h)),
                        new(new(x2, y2, z2), new(u2 / w, v2 / h)),
                        new(new(x3, y3, z3), new(u3 / w, v3 / h)),
                    });
                texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                    new(new(x, y, z), default, new(rx, ry, rz), new(zoom, zoom, zoom)) :
                    new(default, default, new(0, 0, 0), new(1, 1, 1));
            }

            texture.Color = Color.FromArgb((byte)(255 * alpha), 255, 255, 255);

            ctxt.DrawTexture(texture);
        }

        public void load(string type, params object[] args)
        {
            _img.Dispose();
            var context = _imageobj.Parent.Parent.GraphicsContext!;
            switch (type)
            {
                case "movie":
                    _img = LoadMovie(
                        args.GetArgValue<string?>(0, null),
                        TimeSpan.FromSeconds(args.GetArgValue(1, time)),
                        args.GetArgValue(2, 0));
                    break;
                case "image":
                    var file = args.GetArgValue<string?>(0, null);
                    if (File.Exists(file))
                    {
                        _img = new(1, 1);
                    }
                    else
                    {
                        _img = Image<BGRA32>.FromFile(file!);
                    }
                    break;
                case "text":
                    var str = args.GetArgValue(0, "");
                    var lineCount = str.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).Length;

                    var text = new FormattedText(
                        args.GetArgValue(0, ""),
                        _font,
                        _fontsize,
                        TextAlignment.Left,
                        Enumerable.Range(0, lineCount).Select(i => new FormattedTextStyleSpan(i, 0..^1, _fontcolor)).ToArray());

                    _img = text.Draw();
                    text.Dispose();
                    break;
                case "figure":
                    var name = args.GetArgValue(0, "円");
                    var color = Color.FromInt32(args.GetArgValue(1, 0xffffff));
                    color = Color.FromArgb(255, color.R, color.G, color.B);
                    var size = args.GetArgValue(2, 100);
                    var line = args.GetArgValue(3, size);

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
                        _img = Image.Polygon(3, size, size, size, color);
                    }
                    else if (name is "五角形")
                    {
                        _img = Image.Polygon(5, size, size, size, color);
                    }
                    else if (name is "六角形")
                    {
                        _img = Image.Polygon(6, size, size, size, color);
                    }
                    break;
                case "tempbuffer":
                    context = _sharedGraphics;
                    goto case "framebuffer";
                case "framebuffer":
                    var x = args.GetArgValue(0, 0);
                    var y = args.GetArgValue(1, 0);
                    var h = args.GetArgValue(2, context.Width);
                    var w = args.GetArgValue(3, context.Height);
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
            _fontcolor = Color.FromInt32(col1);
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
                        var w = value.GetArgValue(1, _sharedGraphics.Width);
                        var h = value.GetArgValue(2, _sharedGraphics.Height);

                        _drawTarget = DrawTarget.TempBuffer;
                        _sharedGraphics.SetSize(new(w, h));
                    }
                    break;
                case "draw_state":
                    _args.Handled = value.GetArgValue(0, _args.Handled);
                    break;
                default:
                    //throw new NotImplementedException();
                    break;
            }
        }

        public bool copybuffer(string dst, string src)
        {
            Action<Image<BGRA32>>? GetAction(string dst)
            {
                if (dst == "tmp")
                {
                    return img =>
                    {
                        using var texture = Texture.FromImage(img);
                        _sharedGraphics.SetSize(img.Size);
                        _sharedGraphics.DrawTexture(texture);
                    };
                }
                else if (dst == "obj")
                {
                    return img =>
                    {
                        // "obj" <= "obj" の場合のため
                        var cloned = img.Clone();
                        _img.Dispose();
                        _img = cloned;
                    };
                }
                else if (dst.Contains("cache:"))
                {
                    var name = dst.Split(":").LastOrDefault();
                    if (string.IsNullOrWhiteSpace(name))
                        return null;

                    if (_buffers.TryGetValue(name, out var old))
                    {
                        old.Dispose();
                        _buffers.Remove(name);
                    }

                    return img =>
                    {
                        _buffers.Add(name, img);
                    };
                }
                else
                {
                    return null;
                }
            }

            var action = GetAction(dst);
            if (action == null)
                return false;

            if (src == "frm")
            {
                var ctx = _imageobj.Parent.Parent.GraphicsContext!;
                using var buffer = new Image<BGRA32>(ctx.Width, ctx.Height);

                ctx.ReadImage(buffer);

                action.Invoke(buffer);
                return true;
            }
            else if (src == "obj")
            {
                action.Invoke(_img);
                return true;
            }
            else if (src.Contains("cache:"))
            {
                var name = src.Split(":").LastOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                    return false;

                if (_buffers.TryGetValue(name, out var buffer))
                {
                    action.Invoke(buffer);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if (src.Contains("image:"))
            {
                var file = src.Split(":").LastOrDefault();
                if (string.IsNullOrWhiteSpace(file))
                    return false;

                file = Path.GetFullPath(file, BasePath);
                if (!File.Exists(file))
                    return false;

                using var buffer = Image<BGRA32>.FromFile(file);
                action.Invoke(buffer);
                return true;
            }

            return false;
        }

        public object getvalue(object target, double time, int section)
        {
            throw new NotImplementedException();
        }

        public dynamic getoption(string name, params dynamic[] value)
        {
            throw new NotImplementedException();
        }

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

        internal void Update(Texture texture)
        {
            _img.Dispose();

            _texture = texture;
            _img = texture.ToImage();

            // フィールドをリセット
            _ox = 0;
            _oy = 0;
            _oz = 0;
            _zoom = 1;
            _drawTarget = DrawTarget.FrameBuffer;
            _font = FontManager.Default.LoadedFonts.First();
            _fontsize = 16;
            _fontcolor = Colors.White;
        }

        internal Texture ReadTexture()
        {
            _texture.Update(_img);
            return _texture;
        }

        public void Dispose()
        {
            _img.Dispose();
        }

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
    }
}