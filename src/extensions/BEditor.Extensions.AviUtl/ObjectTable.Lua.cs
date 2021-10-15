using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

using Microsoft.Extensions.DependencyInjection;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public partial class ObjectTable
    {
#pragma warning disable IDE1006, CA1822, IDE0060
        public float ox
        {
            get => _texture.Transform.Relative.X;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Relative;
                vector.X = value;
                transform.Relative = vector;
                _texture.Transform = transform;
            }
        }

        public float oy
        {
            get => -_texture.Transform.Relative.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Relative;
                vector.Y = -value;
                transform.Relative = vector;
                _texture.Transform = transform;
            }
        }

        public float oz
        {
            get => -_texture.Transform.Relative.Z;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Relative;
                vector.Z = -value;
                transform.Relative = vector;
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
                _texture.Transform = transform;
            }
        }

        public float ry
        {
            get => -_texture.Transform.Rotation.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Rotation;
                vector.Y = -value;
                transform.Rotation = vector;
                _texture.Transform = transform;
            }
        }

        public float rz
        {
            get => -_texture.Transform.Rotation.Z;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Rotation;
                vector.Z = -value;
                transform.Rotation = vector;
                _texture.Transform = transform;
            }
        }

        public float cx
        {
            get => -_texture.Transform.Center.X;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.X = -value;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float cy
        {
            get => _texture.Transform.Center.Y;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Y = value;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float cz
        {
            get => _texture.Transform.Center.Z;
            set
            {
                var transform = _texture.Transform;
                var vector = transform.Center;
                vector.Z = value;
                transform.Center = vector;
                _texture.Transform = transform;
            }
        }

        public float zoom
        {
            get => _zoom;
            set
            {
                _zoom = value;

                var transform = _texture.Transform;
                var vector = transform.Scale;
                vector.X *= value;
                vector.Y *= value;
                transform.Scale = vector;
                _texture.Transform = transform;
            }
        }

        public float alpha
        {
            get => 255F / _texture.Color.A;
            set => _texture.Color = Color.FromArgb((byte)(255 * value), _texture.Color.R, _texture.Color.G, _texture.Color.B);
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
                _texture.Transform = transform;
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
                _texture.Transform = transform;
            }
        }

        public float x => _texture.Transform.Position.X;

        public float y => -_texture.Transform.Position.Y;

        public float z => -_texture.Transform.Position.Z;

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

        public void mes(string text)
        {
            _imageobj.ServiceProvider?.GetService<IMessage>()?.Snackbar(text);
        }

        // Todo: エフェクトを実装
        public void effect(string name, params object[] param)
        {
            this.Apply(ref _img, name, param);
        }

        public void effect()
        {
            _args.Handled = true;
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
            texture.Transform = _texture.Transform;
            texture.Material = _texture.Material;
            texture.BlendMode = _texture.BlendMode;
            texture.RasterizerState = _texture.RasterizerState;

            texture.Transform = _drawTarget is DrawTarget.FrameBuffer ?
                new(
                    new Vector3(x, -y, -z),
                    new Vector3(ox, -oy, -oz),
                    new Vector3(rx, -ry, -rz),
                    new Vector3(zoom, zoom, zoom)) :
                new(
                    new Vector3(ox, -oy, -oz),
                    default,
                    new Vector3(rx, -ry, -rz),
                    new Vector3(zoom, zoom, zoom));

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
                new(
                    new Vector3(x, -y, -z),
                    default,
                    new Vector3(rx, -ry, -rz),
                    new Vector3(zoom, zoom, zoom)) :
                new(
                    default,
                    default,
                    new Vector3(0, 0, 0),
                    new Vector3(1, 1, 1));

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
                new(
                    new Vector3(x, -y, -z),
                    default,
                    new Vector3(rx, -ry, -rz),
                    new Vector3(zoom, zoom, zoom)) :
                new(
                    default,
                    default,
                    new Vector3(0, 0, 0),
                    new(1, 1, 1));

            texture.Color = Color.FromArgb((byte)(255 * alpha), 255, 255, 255);

            ctxt.DrawTexture(texture);
        }

        // - layer
        // - before
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
                    args = new[] { type };
                    goto case "text";
                    //throw new NotSupportedException($"{type} は対応していません");
            }
        }

        public void setfont(string name, int size, int type = 0, int col1 = 0xffffff, int col2 = 0xffffff)
        {
            _font = FontManager.Default.Find(f => f.FamilyName == name) ?? _font;
            _fontsize = size;
            _fontcolor = Color.FromInt32(col1);
            _fontcolor.A = 255;
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

        // - culling
        // - billboard
        // - shadow
        // - antialias
        // - blend
        // - focus_mode
        // - camera_param
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

        // - track_mode
        // - section_num
        // - camera_param
        public dynamic getoption(string name, params dynamic[] value)
        {
            switch (name)
            {
                case "multi_object":
                    return num > 0;
                case "script_name":
                    if (value.Length == 0)
                        return Parent?.Entry.Name ?? string.Empty;

                    var relIndex = value.GetArgValue(0, 0);
                    var skip = value.GetArgValue(1, false);

                    return GetScriptName(relIndex, skip);
                case "gui":
                    return true;
                case "camera_mode":
                    if (Parent?.GetParent<Scene>()?.GraphicsContext is GraphicsContext ctx)
                    {
                        return ctx.Camera is OrthographicCamera ortho
                            && ortho.Position.X == 0
                            && ortho.Position.Y == 0
                            && ortho.Target == Vector3.Zero ? 0 : 1;
                    }
                    else
                    {
                        return -1;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        // scenechange未実装
        public double getvalue(dynamic target, double time = double.NaN, int section = 0)
        {
            var frame = double.IsNaN(time) ? (Frame)this.frame : Frame.FromSeconds(time, framerate);

            if (Parent is AnimationEffect anm && target is int num)
            {
                if (anm.Properties.TryGetValue($"track{num}", out var prop)
                    && prop is EaseProperty track)
                {
                    return GetValue(track, frame, section);
                }
            }
            else if (target is string targetStr)
            {
                if (targetStr == "x")
                {
                    return GetValue(_imageobj.Coordinate.X, frame, section);
                }
                else if (targetStr == "y")
                {
                    return -GetValue(_imageobj.Coordinate.Y, frame, section);
                }
                else if (targetStr == "z")
                {
                    return -GetValue(_imageobj.Coordinate.Z, frame, section);
                }
                else if (targetStr == "rx")
                {
                    return GetValue(_imageobj.Rotate.RotateX, frame, section);
                }
                else if (targetStr == "ry")
                {
                    return -GetValue(_imageobj.Rotate.RotateY, frame, section);
                }
                else if (targetStr == "rz")
                {
                    return -GetValue(_imageobj.Rotate.RotateZ, frame, section);
                }
                else if (targetStr == "zoom")
                {
                    return GetValue(_imageobj.Scale.Scale1, frame, section);
                }
                else if (targetStr == "alpha")
                {
                    return GetValue(_imageobj.Blend.Opacity, frame, section) / 100;
                }
                else if (targetStr == "aspect")
                {
                    var x = GetValue(_imageobj.Scale.ScaleX, frame, section);
                    var y = GetValue(_imageobj.Scale.ScaleY, frame, section);

                    return ToAspect(x, y);
                }
                else if (targetStr == "time")
                {
                    return this.time;
                }
                else if (targetStr.Contains("layer"))
                {
                    var items = targetStr.Split(".");
                    var layer = items[0].Replace("layer", string.Empty);
                    var name = items[^1];
                    var targetLayer = int.Parse(layer);
                    var targetClip = _scene.GetFrame(_frame).FirstOrDefault(i => i.Layer == targetLayer);

                    if (targetClip == null)
                        throw new Exception("対象のレイヤーにオブジェクトが存在しません。");

                    if (targetClip.Effect[0] is ImageObject imageObject)
                    {
                        return GetImageObjectValue(imageObject, _frame - targetClip.Start, framerate, name, time, section);
                    }
                }
            }
            throw new Exception($"'{target}'は不正です。");
        }

        // setanchor
        // getaudio
        // filter

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
                        _buffers.Add(name, img.Clone());
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

        public LuaResult getpixel(params dynamic[] args)
        {
            if (args.Length == 0)
            {
                return new LuaResult(_img.Width, _img.Height);
            }

            var x = args.GetArgValue(0, 0);
            var y = args.GetArgValue(1, 0);
            var type = args.GetArgValue(2, PixelOption.Type.ToStringCore());

            if (type == "col")
            {
                var pixel = (Color)_img[x, y];
                var alpha = pixel.A / 255f;
                return new LuaResult(pixel.Value, alpha);
            }
            else if (type == "rgb")
            {
                var pixel = _img[x, y];
                return new LuaResult(pixel.R, pixel.G, pixel.B, pixel.A);
            }
            else if (type == "yc")
            {
                var pixel = (Color)_img[x, y];
                var alpha = pixel.A / 255f;
                var yc = pixel.ToYCbCr();

                // PixelYCに
                return new LuaResult(
                    Math.Clamp(yc.Y / 255F * 4096, 0, 4096),
                    Math.Clamp((yc.Cb - 128F) / 255F * 2048, -2048, 2048),
                    Math.Clamp((yc.Cr - 128F) / 255F * 2048, -2048, 2048),
                    alpha);
            }

            throw new Exception();
        }

        public LuaResult putpixel(params dynamic[] args)
        {
            if (args.Length == 0)
            {
                return new LuaResult(_img.Width, _img.Height);
            }

            var x = args.GetArgValue(0, 0);
            var y = args.GetArgValue(1, 0);
            var type = PixelOption.Type;

            if (type == PixelType.Color)
            {
                var pixel = Color.FromInt32(args.GetArgValue(2, 0xffffff));
                var alpha = args.GetArgValue<float>(3, 1);

                pixel.A = (byte)MathF.Round(alpha * 255, MidpointRounding.AwayFromZero);
                _img[x, y] = pixel;
            }
            else if (type == PixelType.Rgb)
            {
                var r = args.GetArgValue<byte>(2, 255);
                var g = args.GetArgValue<byte>(3, 255);
                var b = args.GetArgValue<byte>(4, 255);
                var a = args.GetArgValue<byte>(5, 255);
                _img[x, y] = new BGRA32(r, g, b, a);
            }
            else if (type == PixelType.YCbCr)
            {
                var ye = MathF.Round(args.GetArgValue(2, 4096F) / 4096F * 255F, MidpointRounding.AwayFromZero);
                var cb = MathF.Round((args.GetArgValue(3, 2048F) / 2048 * 255F) + 128F, MidpointRounding.AwayFromZero);
                var cr = MathF.Round((args.GetArgValue(3, 2048F) / 2048 * 255F) + 128F, MidpointRounding.AwayFromZero);
                var a = (byte)MathF.Round(args.GetArgValue(5, 1) * 255F, MidpointRounding.AwayFromZero);
                var color = new YCbCr(ye, cb, cr).ToColor();
                color.A = a;

                _img[x, y] = color;
            }

            return new LuaResult();
        }

        public void copypixel(int dst_x, int dst_y, int src_x, int src_y)
        {
            _img[dst_x, dst_y] = _img[src_x, src_y];
        }

        public void pixeloption(string name, dynamic value)
        {
            switch (name)
            {
                case "type":
                    if (value is string type && type is "col" or "rgb" or "yc")
                    {
                        var pixelType = ToPixelType(type);
                        PixelOption = PixelOption with { Type = pixelType };
                    }
                    else
                    {
                        throw new Exception($"'{value}' はピクセルの種類として不正です。");
                    }
                    break;
                case "get":
                    PixelOption = PixelOption with
                    {
                        PixelSource = ToPixelReadWriteOption(value)
                    };
                    break;
                case "put":
                    PixelOption = PixelOption with
                    {
                        PixelDestination = ToPixelReadWriteOption(value)
                    };
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 各種環境情報を取得します。
        /// </summary>
        /// <param name="name">
        /// <list type = "bullet|number|table">
        ///     <listheader>
        ///         <term>取得する情報の名前</term>
        ///         <description>戻り値</description>
        ///     </listheader>
        ///     <item>
        ///         <term>"script_path"</term>
        ///         <description>スクリプトフォルダのパス</description>
        ///     </item>
        ///     <item>
        ///         <term>"saving"</term>
        ///         <description>true=出力中 / false=非出力中</description>
        ///     </item>
        ///     <item>
        ///         <term>"image_max"</term>
        ///         <description>最大画像サイズ(横幅,高さ)</description>
        ///     </item>
        /// </list>
        /// </param>
        /// <returns></returns>
        public LuaResult? getinfo(string name)
        {
            switch (name)
            {
                case "script_path":
                    return new(Path.Combine(Plugin.Default.BaseDirectory, "script"));
                case "saving":
                    return new(_args.Type is ApplyType.Video or ApplyType.Image);
                case "image_max":
                    return new(32768, 32768);
                case "version":
                    var ver = typeof(ImageObject).Assembly.GetName().Version;

                    if (ver == null) return null;
                    return new(ver.ToString(3));
                default:
                    return null;
            }
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

        public LuaResult RGB(params dynamic[] value)
        {
            if (value.Length == 3)
            {
                var r = value.GetArgValue<byte>(0, 0);
                var g = value.GetArgValue<byte>(1, 0);
                var b = value.GetArgValue<byte>(2, 0);

                return new LuaResult(Color.FromArgb(0, r, g, b).Value);
            }
            else if (value.Length == 1)
            {
                var color = Color.FromInt32(value.GetArgValue(0, 0));
                return new LuaResult(color.R, color.G, color.B);
            }
            else if (value.Length == 6)
            {
                var r1 = value.GetArgValue<byte>(0, 0);
                var g1 = value.GetArgValue<byte>(1, 0);
                var b1 = value.GetArgValue<byte>(2, 0);
                var r2 = value.GetArgValue<byte>(3, 0);
                var g2 = value.GetArgValue<byte>(4, 0);
                var b2 = value.GetArgValue<byte>(5, 0);

                var r = (byte)Linear(time, totaltime, r1, r2);
                var g = (byte)Linear(time, totaltime, g1, g2);
                var b = (byte)Linear(time, totaltime, b1, b2);

                return new LuaResult(Color.FromArgb(0, r, g, b).Value);
            }
            else
            {
                return LuaResult.Empty;
            }
        }

        public LuaResult HSV(params dynamic[] value)
        {
            if (value.Length == 3)
            {
                var h = value.GetArgValue<double>(0, 0);
                var s = value.GetArgValue<double>(1, 0);
                var v = value.GetArgValue<double>(2, 0);

                return new LuaResult(new Hsv(h, s, v).ToColor().Value);
            }
            else if (value.Length == 1)
            {
                var color = Color.FromInt32(value.GetArgValue(0, 0)).ToHsv();
                return new LuaResult(color.H, color.S, color.V);
            }
            else if (value.Length == 6)
            {
                var h1 = value.GetArgValue<double>(0, 0);
                var s1 = value.GetArgValue<double>(1, 0);
                var v1 = value.GetArgValue<double>(2, 0);
                var h2 = value.GetArgValue<double>(3, 0);
                var s2 = value.GetArgValue<double>(4, 0);
                var v2 = value.GetArgValue<double>(5, 0);

                var h = Linear(time, totaltime, h1, h2);
                var s = Linear(time, totaltime, s1, s2);
                var v = Linear(time, totaltime, v1, v2);

                return new LuaResult(new Hsv(h, s, v).ToColor().Value);
            }
            else
            {
                return LuaResult.Empty;
            }
        }

#pragma warning restore IDE1006, CA1822, IDE0060
    }
}