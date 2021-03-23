using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using SkiaSharp;

using Svg.Skia;

namespace BEditor.Extensions.Svg
{
    public class SvgImage : ImageObject
    {
        public static readonly EasePropertyMetadata ScaleMetadata = new("スケール", 100, Min: 0);
        public static readonly EasePropertyMetadata ScaleXMetadata = new("スケール X", 100, Min: 0);
        public static readonly EasePropertyMetadata ScaleYMetadata = new("スケール Y", 100, Min: 0);
        public static readonly FilePropertyMetadata FileMetadata = new("画像ファイル", "", new("画像ファイル", new FileExtension[]
        {
            new("svg")
        }));
        private SKSvg? _source;
        private IDisposable? disposable;

        public SvgImage()
        {
            Scale = new(ScaleMetadata);
            ScaleX = new(ScaleXMetadata);
            ScaleY = new(ScaleYMetadata);
            File = new(FileMetadata);
        }

        public override string Name => "Svg画像";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Scale,
            ScaleX,
            ScaleY,
            File
        };
        [DataMember]
        public EaseProperty Scale { get; private set; }
        [DataMember]
        public EaseProperty ScaleX { get; private set; }
        [DataMember]
        public EaseProperty ScaleY { get; private set; }
        [DataMember]
        public FileProperty File { get; private set; }
        private SKSvg? Source
        {
            get => _source ??= Open(File.Value);
            set
            {
                _source?.Dispose();
                _source = value;
            }
        }

        protected override unsafe Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            if (Source?.Picture is null) return null;

            var picture = Source.Picture;
            var s = Scale[args.Frame] / 100;
            var sx = ScaleX[args.Frame] / 100 * s;
            var sy = ScaleY[args.Frame] / 100 * s;
            using var bmp = new SKBitmap(new SKImageInfo((int)(picture.CullRect.Width * sx), (int)(picture.CullRect.Height * sy), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            picture.Draw(SKColor.Empty, sx, sy, canvas);

            var result = new Image<BGRA32>(bmp.Width, bmp.Height);

            fixed (byte* src = bmp.Bytes)
            fixed (BGRA32* dst = result.Data)
            {
                Buffer.MemoryCopy(src, dst, result.DataSize, result.DataSize);
            }

            return result;
        }
        protected override void OnLoad()
        {
            base.OnLoad();
            Scale.Load(ScaleMetadata);
            ScaleX.Load(ScaleXMetadata);
            ScaleY.Load(ScaleYMetadata);
            File.Load(FileMetadata);
            disposable = File.Subscribe(file =>
            {
                Source = Open(file);
            });
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            Scale.Unload();
            ScaleX.Unload();
            ScaleY.Unload();
            File.Unload();
            disposable?.Dispose();
            _source?.Dispose();
        }
        private static SKSvg? Open(string file)
        {
            if (System.IO.File.Exists(file))
            {
                var svg = new SKSvg();
                svg.Load(file);

                return svg;
            }

            return default;
        }
    }
}