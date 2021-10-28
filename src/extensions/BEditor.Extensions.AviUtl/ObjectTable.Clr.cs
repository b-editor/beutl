using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Media.Decoding;

namespace BEditor.Extensions.AviUtl
{
    public partial class ObjectTable : IDisposable
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
            _zoom = image.Scale.Scale1[args.Frame] / 100;

            if (_sharedGraphics is null)
            {
                _sharedGraphics = new(Math.Max(_img.Width, 1), Math.Max(_img.Height, 1));
            }
            else
            {
                _sharedGraphics.SetSize(new(Math.Max(_img.Width, 1), Math.Max(_img.Height, 1)));
            }
        }

        internal string BasePath { get; set; } = string.Empty;

        internal AnimationEffect? Parent { get; set; }

        internal PixelOption PixelOption { get; set; } = new();

        internal static SizeF ToSize(float size, float aspect)
        {
            SizeF result;

            if (aspect > 0)
            {
                result = new(size - (aspect * size / 100f), size);
            }
            else
            {
                result = new(size, size + (aspect * size / 100));
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
            _zoom = _imageobj.Scale.Scale1[_args.Frame] / 100;
            _drawTarget = DrawTarget.FrameBuffer;
            _font = FontManager.Default.LoadedFonts.First();
            _fontsize = 16;
            _fontcolor = Colors.White;

            PixelOption = new();
        }

        internal Texture ReadTexture()
        {
            _texture.Update(_img);
            return _texture;
        }

        public void Dispose()
        {
            _img.Dispose();
            GC.SuppressFinalize(this);
        }

        private static PixelType ToPixelType(string type)
        {
            return type switch
            {
                "col" => PixelType.Color,
                "rgb" => PixelType.Rgb,
                "yc" => PixelType.YCbCr,
                _ => throw new Exception($"'{type}' はピクセルの種類として不正です。"),
            };
        }

        private static PixelReadWriteOption ToPixelReadWriteOption(string str)
        {
            return str switch
            {
                "obj" => PixelReadWriteOption.Object,
                "frm" => PixelReadWriteOption.Framebuffer,
                _ => PixelReadWriteOption.Object,
            };
        }

        private static double Linear(double t, double totaltime, double min, double max)
        {
            return ((max - min) * t / totaltime) + min;
        }

        private static float GetValue(EaseProperty property, Frame frame, int section)
        {
            var clip = property.GetRequiredParent<ClipElement>();
            if (section < 0 || section > property.Pairs.Count)
                throw new Exception("'section'が範囲外です。");

            if (section == -1)
            {
                section = property.Pairs.Count - 1;
            }

            var pair = property.Pairs[section];
            var baseFrame = (Frame)pair.Position.GetAbsolutePosition(clip.Length.Value) + clip.Start;

            var value = property[frame + baseFrame];
            return value;
        }

        private static float GetImageObjectValue(ImageObject imageObject, Frame cur, double framerate, string target, double time, int section)
        {
            var frame = double.IsNaN(time) ? cur : Frame.FromSeconds(time, framerate);

            if (target == "x")
            {
                return GetValue(imageObject.Coordinate.X, frame, section);
            }
            else if (target == "y")
            {
                return -GetValue(imageObject.Coordinate.Y, frame, section);
            }
            else if (target == "z")
            {
                return -GetValue(imageObject.Coordinate.Z, frame, section);
            }
            else if (target == "rx")
            {
                return GetValue(imageObject.Rotate.RotateX, frame, section);
            }
            else if (target == "ry")
            {
                return -GetValue(imageObject.Rotate.RotateY, frame, section);
            }
            else if (target == "rz")
            {
                return -GetValue(imageObject.Rotate.RotateZ, frame, section);
            }
            else if (target == "zoom")
            {
                return GetValue(imageObject.Scale.Scale1, frame, section);
            }
            else if (target == "alpha")
            {
                return GetValue(imageObject.Blend.Opacity, frame, section) / 100;
            }
            else if (target == "aspect")
            {
                var x = GetValue(imageObject.Scale.ScaleX, frame, section);
                var y = GetValue(imageObject.Scale.ScaleY, frame, section);

                return ToAspect(x, y);
            }
            else
            {
                throw new Exception($"'{target}'は不正です。");
            }
        }

        private static (float x, float y, float z) InterpolationPrivate(
            float time,
            float x0, float y0, float z0,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float x3, float y3, float z3)
        {
            // https://scrapbox.io/aviutl-script/obj.interpolation
            var (xd0, xd1, xd2) = (x1 - x0, x2 - x1, x3 - x2);
            var (yd0, yd1, yd2) = (y1 - y0, y2 - y1, y3 - y2);
            var (zd0, zd1, zd2) = (z1 - z0, z2 - z1, z3 - z2);
            var d0 = MathF.Sqrt(xd0 * xd0 + yd0 * yd0 + zd0 * zd0);
            var d1 = MathF.Sqrt(xd1 * xd1 + yd1 * yd1 + zd1 * zd1);
            var d2 = MathF.Sqrt(xd2 * xd2 + yd2 * yd2 + zd2 * zd2);
            if (d1 <= 0)
            {
                return (x0, y0, z0);
            }
            var dd0 = d0 + d0 + d1 + d1;
            var dd1 = d1 + d1 + d2 + d2;
            var s = 1 - time;

            var resultX = ((x1 + (d1 * xd0 + d0 * xd1) / dd0) * s * s * 3 + time * time * x2) * time +
                ((x2 - (d2 * xd1 + d1 * xd2) / dd1) * time * time * 3 + s * s * x1) * s;

            var resultY = ((y1 + (d1 * yd0 + d0 * yd1) / dd0) * s * s * 3 + time * time * y2) * time +
                ((y2 - (d2 * yd1 + d1 * yd2) / dd1) * time * time * 3 + s * s * y1) * s;

            var resultZ = ((z1 + (d1 * zd0 + d0 * zd1) / dd0) * s * s * 3 + time * time * z2) * time +
             ((z2 - (d2 * zd1 + d1 * zd2) / dd1) * time * time * 3 + s * s * z1) * s;

            return (resultX, resultY, resultZ);
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

        private string GetScriptName(int relIndex, bool skip)
        {
            if (Parent?.Parent != null)
            {
                var list = (IList<EffectElement>)(skip ? Parent.Parent.Effect.Where(i => i.IsEnabled).ToArray() : Parent.Parent.Effect);
                var index = list.IndexOf(Parent) + relIndex;
                if (list.ElementAtOrDefault(index) is AnimationEffect anm)
                {
                    return anm.Entry.Name;
                }
            }

            return string.Empty;
        }

        private GraphicsContext GetContext()
        {
            return _drawTarget is DrawTarget.FrameBuffer ? _imageobj.Parent.Parent.GraphicsContext! : _sharedGraphics;
        }
    }
}