// FormattedText.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Drawing.Pixel;

using SkiaSharp;

namespace BEditor.Drawing
{
#pragma warning disable CS1591, SA1600
    public class FormattedText
    {
        private readonly List<KeyValuePair<FBrushRange, Color>> _foregroundBrushes = new();
        private readonly List<FormattedTextLine> _lines = new();
        private readonly SKPaint _paint;
        private RectangleF[]? _rects;
        private float _lineHeight;
        private float _lineOffset;
        private readonly SKFontMetrics _fontMetrics;
        private RectangleF _bounds;
        [AllowNull]
        private List<PrivateFormattedTextLine> _skiaLines;

        public FormattedText(
            string text,
            Font font,
            float fontSize,
            TextAlignment textAlignment,
            IReadOnlyList<FormattedTextStyleSpan> spans)
        {
            Text = text ?? string.Empty;
            Spans = spans;
            TextAlignment = textAlignment;

            // Replace 0 characters with zero-width spaces (200B)
            Text = Text.Replace((char)0, (char)0x200B);

            _paint = new SKPaint
            {
                TextEncoding = SKTextEncoding.Utf16,
                IsStroke = false,
                IsAntialias = true,
                LcdRenderText = true,
                SubpixelText = true,
                IsLinearText = true,
                Typeface = font.GetTypeface(),
                TextSize = fontSize,
            };
            _fontMetrics = _paint.FontMetrics;

            // currently Skia does not measure properly with Utf8 !!!
            // Paint.TextEncoding = SKTextEncoding.Utf8;
            if (spans != null)
            {
                foreach (var span in spans)
                {
                    if (span.ForegroundBrush != default)
                    {
                        SetForegroundBrush(span.ForegroundBrush, span.StartIndex, span.Length);
                    }
                }
            }

            Rebuild();
        }

        public RectangleF Bounds => _bounds;

        public string Text { get; }

        /// <summary>
        /// Gets or sets the alignment of the text.
        /// </summary>
        public TextAlignment TextAlignment { get; }

        /// <summary>
        /// Gets or sets a collection of spans that describe the formatting of subsections of the
        /// text.
        /// </summary>
        public IReadOnlyList<FormattedTextStyleSpan> Spans { get; }

        /// <summary>
        /// Gets the lines in the text.
        /// </summary>
        /// <returns>
        /// A collection of <see cref="FormattedTextLine"/> objects.
        /// </returns>
        public IEnumerable<FormattedTextLine> Lines => _lines;

        public IEnumerable<(Image<BGRA32> Image, RectangleF Rect)> DrawMultiple()
        {
            for (var i = 0; i < _lines.Count; i++)
            {
                var privLine = _skiaLines[i];
                var line = _lines[i];
                var nextTop = privLine.Top + privLine.Height;
                var prevRight = TransformX(0, privLine.Width, TextAlignment);

                if (i + 1 < _skiaLines.Count)
                {
                    nextTop = _skiaLines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var color = GetColor(li);
                    var bounds = default(SKRect);
                    _paint.MeasureText(c.ToString(), ref bounds);
                    _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                    using var bmp = new SKBitmap(new SKImageInfo((int)bounds.Width, (int)(nextTop - privLine.Top), SKColorType.Bgra8888));
                    using var canvas = new SKCanvas(bmp);

                    var resultRect = new RectangleF(
                        prevRight + bounds.Left,
                        privLine.Top,/*privLine.Top + (bounds.Top / 2),*/
                        bounds.Width,
                        nextTop - privLine.Top);
                    canvas.DrawText(c.ToString(), (bounds.Width / 2) - bounds.MidX, -_fontMetrics.Top/*(bounds.Height / 2) - bounds.MidY*/, _paint);

                    prevRight += bounds.Right;

                    yield return (bmp.ToImage32(), resultRect);
                }
            }
        }

        public Image<BGRA32> Draw()
        {
            using var bmp = new SKBitmap(new SKImageInfo((int)Bounds.Width, (int)Bounds.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < _lines.Count; i++)
            {
                var privLine = _skiaLines[i];
                var line = _lines[i];
                var nextTop = privLine.Top + privLine.Height;
                var prevRight = TransformX(0, privLine.Width, TextAlignment);

                if (i + 1 < _skiaLines.Count)
                {
                    nextTop = _skiaLines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var color = GetColor(li);
                    var bounds = default(SKRect);
                    _paint.MeasureText(c.ToString(), ref bounds);
                    _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                    var a = nextTop - privLine.Top;
                    canvas.Translate(prevRight + bounds.Left, privLine.Top);

                    // DrawTextのYはベースライン
                    canvas.DrawText(
                        c.ToString(),
                        (bounds.Width / 2) - bounds.MidX,
                        -_fontMetrics.Top,
                        _paint);

                    canvas.ResetMatrix();
                    prevRight += bounds.Right;
                }
            }

            return bmp.ToImage32();
        }

        public override string ToString()
        {
            return Text;
        }

        private static bool IsBreakChar(char c)
        {
            // white space or zero space whitespace
            return char.IsWhiteSpace(c) || c == '\u200B';
        }

        private static string[] GetLines(string text)
        {
            return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private IEnumerable<RectangleF> GetRectanglesFromLine(string text)
        {
            var bounds = default(SKRect);
            foreach (var item in text)
            {
                _paint.MeasureText(item.ToString(), ref bounds);
                yield return new RectangleF(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            }
        }

        private Color GetColor(int index)
        {
            for (var i = 0; i < Spans.Count; i++)
            {
                var span = Spans[i];
                if (span.StartIndex <= index || index < span.Length)
                {
                    return span.ForegroundBrush;
                }
            }

            return Colors.White;
        }

        private void Rebuild()
        {
            var length = Text.Length;

            _lines.Clear();
            _rects = null;
            _skiaLines = new List<PrivateFormattedTextLine>();

            var curY = 0F;

            var metrics = _paint.FontMetrics;
            var mTop = metrics.Top; // ベースラインからの最大距離 (上) (0以上)
            var mBottom = metrics.Bottom; // ベースラインからの最大距離 (下) (0以下)
            var mLeading = metrics.Leading; // テキストの行間に追加する推奨距離 (0以下)
            var mDescent = metrics.Descent; // ベースラインからの推奨距離 (下) (0以下)
            var mAscent = metrics.Ascent; // ベースラインからの推奨距離 (上) (0以上)
            var lastLineDescent = mBottom - mDescent;

            // 行の高さ
            _lineHeight = mDescent - mAscent;

            // Rendering is relative to baseline
            _lineOffset = -metrics.Ascent;

            var lines = GetLines(Text);
            var count = 0;

            _lines.Clear();
            var maxWidth = 0F;
            var maxHeight = 0F;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineWidth = _paint.MeasureText(line);
                var txtLine = new PrivateFormattedTextLine
                {
                    Start = count,
                    TextLength = line.Length,
                    Width = lineWidth,
                    Height = _lineHeight,
                    Top = curY,
                };

                _skiaLines.Add(txtLine);
                count += line.Length;
                curY += _lineHeight;
                curY += mLeading;

                var item = new FormattedTextLine(line, lineWidth, _lineHeight);
                _lines.Add(item);

                if (maxWidth < item.Width)
                {
                    maxWidth = item.Width;
                }

                maxHeight += item.Height;
            }

            if (_skiaLines.Count == 0)
            {
                _skiaLines.Add(default);
                _lines.Add(new FormattedTextLine(string.Empty, 0, _lineHeight));
                _bounds = new RectangleF(0, 0, 0, _lineHeight);
            }
            else
            {
                var lastLine = _skiaLines[^1];

                _bounds = new RectangleF(0, 0, maxWidth, /*lastLine.Top + lastLine.Height*//*_lineHeight * _skiaLines.Count*/maxHeight);
            }
        }

        private float TransformX(float originX, float lineWidth, TextAlignment align)
        {
            float x = 0;

            if (align == TextAlignment.Left)
            {
                x = originX;
            }
            else
            {
                var width = _bounds.Width;

                switch (align)
                {
                    case TextAlignment.Center: x = originX + ((width - lineWidth) / 2); break;
                    case TextAlignment.Right: x = originX + (width - lineWidth); break;
                }
            }

            return x;
        }

        private void SetForegroundBrush(Color brush, int startIndex, int length)
        {
            var key = new FBrushRange(startIndex, length);
            var index = _foregroundBrushes.FindIndex(v => v.Key.Equals(key));

            if (index > -1)
            {
                _foregroundBrushes.RemoveAt(index);
            }

            if (brush != default)
            {
                _foregroundBrushes.Insert(0, new KeyValuePair<FBrushRange, Color>(key, brush));
            }
        }

        private struct PrivateFormattedTextLine
        {
            public float Height;
            public int Start;
            public int TextLength;
            public float Top;
            public float Width;
            public bool IsEmptyTrailingLine;
        }

        private struct FBrushRange
        {
            public FBrushRange(int startIndex, int length)
            {
                StartIndex = startIndex;
                Length = length;
            }

            public int EndIndex => StartIndex + Length;

            public int Length { get; private set; }

            public int StartIndex { get; private set; }

            public bool Intersects(int index, int len) =>
                (index + len) > StartIndex &&
                (StartIndex + Length) > index;

            public override string ToString()
            {
                return $"{StartIndex}-{EndIndex}";
            }
        }
    }
#pragma warning restore CS1591, SA1600
}
