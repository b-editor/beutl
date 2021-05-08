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
        public static new readonly DirectEditingProperty<SvgImage, EaseProperty> ScaleProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, SvgImage>(
            nameof(SvgScale),
            owner => owner.SvgScale,
            (owner, obj) => owner.SvgScale = obj,
            new EasePropertyMetadata("スケール", 100, Min: 0));

        public static readonly DirectEditingProperty<SvgImage, EaseProperty> ScaleXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, SvgImage>(
            nameof(ScaleX),
            owner => owner.ScaleX,
            (owner, obj) => owner.ScaleY = obj,
            new EasePropertyMetadata("スケール X", 100, Min: 0));

        public static readonly DirectEditingProperty<SvgImage, EaseProperty> ScaleYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, SvgImage>(
            nameof(ScaleY),
            owner => owner.ScaleY,
            (owner, obj) => owner.ScaleY = obj,
            new EasePropertyMetadata("スケール Y", 100, Min: 0));

        public static readonly DirectEditingProperty<SvgImage, FileProperty> FileProperty = EditingProperty.RegisterSerializeDirect<FileProperty, SvgImage>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            new FilePropertyMetadata("画像ファイル", "", new("画像ファイル", new FileExtension[]
            {
                new("svg")
            })));

        private SKSvg? _source;

        private IDisposable? disposable;

#pragma warning disable CS8618
        public SvgImage()
#pragma warning restore CS8618
        {
        }

        public override string Name => "Svg画像";

        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return Coordinate;
                yield return Blend;
                yield return Rotate;
                yield return Material;
                yield return SvgScale;
                yield return ScaleX;
                yield return ScaleY;
                yield return File;
            }
        }

        public EaseProperty SvgScale { get; private set; }

        public EaseProperty ScaleX { get; private set; }

        public EaseProperty ScaleY { get; private set; }

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

        protected override unsafe Image<BGRA32>? OnRender(EffectApplyArgs args)
        {
            if (Source?.Picture is null) return null;

            var picture = Source.Picture;
            var s = SvgScale[args.Frame] / 100;
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
            disposable = File.Subscribe(file =>
            {
                Source = Open(file);
            });
        }

        protected override void OnUnload()
        {
            base.OnUnload();
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