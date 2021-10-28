// Obsolete.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Linq;

using BEditor.Drawing.Pixel;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Generates an image with an text drawn on it.
        /// </summary>
        /// <param name="text">The string to draw.</param>
        /// <param name="font">The font of the string to be drawn.</param>
        /// <param name="size">The size of the text.</param>
        /// <param name="color">The color of the text.</param>
        /// <param name="hAlign">The horizontal alignment of the text.</param>
        /// <param name="vAlign">The vertical alignment of the text.</param>
        /// <param name="linespace">The line spacing for the text.</param>
        /// <returns>Returns an image with an text drawn on it.</returns>
        [Obsolete("Use FormattedText.")]
        public static Image<BGRA32> Text(
            string text,
            Font font,
            float size,
            Color color,
            HorizontalAlign hAlign,
#pragma warning disable RCS1163
            VerticalAlign vAlign,
#pragma warning restore RCS1163
            float linespace = 0)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));

            return TextHorizontal(text, font, size, color, hAlign, linespace);
        }

        /// <summary>
        /// Generates an image with an text drawn on it.
        /// </summary>
        /// <param name="text">The string to draw.</param>
        /// <param name="font">The font of the string to be drawn.</param>
        /// <param name="size">The size of the text.</param>
        /// <param name="strokewidth">The stroke width of the stroke text.</param>
        /// <param name="color">The color of the text.</param>
        /// <param name="hAlign">The horizontal alignment of the text.</param>
        /// <param name="linespace">The line spacing for the text.</param>
        /// <returns>Returns an image with an text drawn on it.</returns>
        [Obsolete("Use FormattedText.")]
        public static Image<BGRA32> StrokeText(string text, Font font, float size, float strokewidth, Color color, HorizontalAlign hAlign, float linespace = 0)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));
            var lines = text.Replace("\r\n", "\n").Split('\n');

            var face = font.GetTypeface();
            using var fontObj = new SKFont(face, size)
            {
                Edging = SKFontEdging.Antialias,
            };

            using var paint = new SKPaint(fontObj)
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokewidth,
            };

            using var linesBounds = new UnmanagedList<SKRect>();

            for (var i = 0; i < lines.Length; i++)
            {
                var item = lines[i];
                var textBounds = default(SKRect);

                paint.MeasureText(item, ref textBounds);
                linesBounds.Add(textBounds);
            }

            using var bmp = new SKBitmap(new SKImageInfo((int)linesBounds.Max(i => i.Width), (int)(linesBounds.Sum(i => i.Height) + (linespace * (lines.Length - 1))), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < lines.Length; i++)
            {
                var txt = lines[i];
                var bounds = linesBounds[i];
                using var path = paint.GetTextPath(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY);

                if (hAlign is HorizontalAlign.Right)
                {
                    var x = bmp.Width - bounds.Width;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
                else if (hAlign is HorizontalAlign.Left)
                {
                    canvas.DrawPath(path, paint);

                    canvas.Translate(0, bounds.Height + linespace);
                }
                else
                {
                    var x = (bmp.Width - bounds.Width) / 2;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
            }

            canvas.Flush();

            return bmp.ToImage32();
        }

        private static Image<BGRA32> TextHorizontal(string text, Font font, float size, Color color, HorizontalAlign hAlign, float linespace = 0)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');

            var face = font.GetTypeface();
            using var fontObj = new SKFont(face, size)
            {
                Edging = SKFontEdging.Antialias,
            };
            var lineHeight = fontObj.Metrics.Descent - fontObj.Metrics.Ascent;

            using var paint = new SKPaint(fontObj)
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
            };

            using var linesBounds = new UnmanagedList<SKRect>();

            for (var i = 0; i < lines.Length; i++)
            {
                var item = lines[i];
                var textBounds = default(SKRect);

                paint.MeasureText(item, ref textBounds);
                linesBounds.Add(textBounds);
            }

            using var bmp = new SKBitmap(new SKImageInfo((int)linesBounds.Max(i => i.Width), (int)((lineHeight * lines.Length) + linespace), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < lines.Length; i++)
            {
                var txt = lines[i];
                var bounds = linesBounds[i];
                using var path = paint.GetTextPath(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY);

                if (hAlign is HorizontalAlign.Right)
                {
                    var x = bmp.Width - bounds.Width;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, lineHeight + linespace);
                }
                else if (hAlign is HorizontalAlign.Left)
                {
                    canvas.DrawPath(path, paint);

                    canvas.Translate(0, lineHeight + linespace);
                }
                else
                {
                    var x = (bmp.Width - bounds.Width) / 2;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, lineHeight + linespace);
                }
            }

            canvas.Flush();

            return bmp.ToImage32();
        }
    }
}