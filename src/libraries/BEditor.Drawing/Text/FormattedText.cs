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
        private const float MAXLINEWIDTH = 10000;

        private readonly List<KeyValuePair<FBrushRange, Color>> _foregroundBrushes = new();
        private readonly List<FormattedTextLine> _lines = new();
        private readonly SKPaint _paint;
        private readonly List<RectangleF> _rects = new();
        private SizeF _constraint = new(float.NaN, float.NaN);
        private float _lineHeight;
        private float _lineOffset;
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
                TextAlign = (SKTextAlign)textAlignment,
            };

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

        public IReadOnlyList<RectangleF> Rectangles => GetRectangles();

        /// <summary>
        /// Gets the lines in the text.
        /// </summary>
        /// <returns>
        /// A collection of <see cref="FormattedTextLine"/> objects.
        /// </returns>
        public IEnumerable<FormattedTextLine> GetLines()
        {
            return _lines;
        }

        public IEnumerable<(Image<BGRA32> Image, RectangleF Rect)> DrawMultiple()
        {
            for (var i = 0; i < Text.Length; i++)
            {
                var chara = Text[i];
                if (chara is '\n' or '\r') continue;
                var rect = Rectangles[i];
                var color = GetColor(i);

                using var bmp = new SKBitmap(new SKImageInfo((int)rect.Width, (int)rect.Height, SKColorType.Bgra8888));
                using var canvas = new SKCanvas(bmp);

                _paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                var bounds = default(SKRect);
                var txt = chara.ToString();
                _paint.MeasureText(txt, ref bounds);
                canvas.DrawText(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY, _paint);

                yield return (bmp.ToImage32(), rect);
            }
        }

        public Image<BGRA32> Draw()
        {
            using var bmp = new SKBitmap(new SKImageInfo((int)Bounds.Width, (int)Bounds.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < Text.Length; i++)
            {
                var chara = Text[i];
                if (chara is '\n' or '\r') continue;
                var rect = Rectangles[i];
                var color = GetColor(i);

                _paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                var bounds = default(SKRect);
                var txt = chara.ToString();
                _paint.MeasureText(txt, ref bounds);
                canvas.Translate(rect.X, rect.Y);
                canvas.DrawText(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY, _paint);
                canvas.ResetMatrix();
            }

            return bmp.ToImage32();
        }

        /// <summary>
        /// Gets the bounds rectangle that the specified character occupies.
        /// </summary>
        /// <param name="index">The index of the character.</param>
        /// <returns>The character bounds.</returns>
        public RectangleF HitTestTextPosition(int index)
        {
            if (string.IsNullOrEmpty(Text))
            {
                var alignmentOffset = TransformX(0, 0, _paint.TextAlign);
                return new RectangleF(alignmentOffset, 0, 0, _lineHeight);
            }

            var rects = GetRectangles();
            if (index >= Text.Length || index < 0)
            {
                var r = rects.LastOrDefault();

                return Text[^1] switch
                {
                    '\n' or '\r' => new RectangleF(r.X, r.Y, 0, _lineHeight),
                    _ => new RectangleF(r.X + r.Width, r.Y, 0, _lineHeight),
                };
            }

            return rects[index];
        }

        /// <summary>
        /// Gets the bounds rectangles that the specified text range occupies.
        /// </summary>
        /// <param name="index">The index of the first character.</param>
        /// <param name="length">The number of characters in the text range.</param>
        /// <returns>The character bounds.</returns>
        public IEnumerable<RectangleF> HitTestTextRange(int index, int length)
        {
            var result = new List<RectangleF>();

            var rects = GetRectangles();

            var lastIndex = index + length - 1;

            foreach (var line in _skiaLines.Where(l =>
                (l.Start + l.Length) > index
                && lastIndex >= l.Start
                && !l.IsEmptyTrailingLine))
            {
                var lineEndIndex = line.Start + (line.Length > 0 ? line.Length - 1 : 0);

                var left = rects[line.Start > index ? line.Start : index].X;
                var right = rects[lineEndIndex > lastIndex ? lastIndex : lineEndIndex].Right;

                result.Add(new RectangleF(left, line.Top, right - left, line.Height));
            }

            return result;
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

        private static int LineBreak(
            string textInput, int textIndex, int stop,
            out int trailingCount)
        {
            var lengthBreak = stop - textIndex;

            // Check for white space or line breakers before the lengthBreak
            var startIndex = textIndex;
            var index = textIndex;
            var word_start = textIndex;
            var prevBreak = true;

            trailingCount = 0;

            while (index < stop)
            {
                var prevText = index;
                var currChar = textInput[index++];
                var currBreak = IsBreakChar(currChar);

                if (!currBreak && prevBreak)
                {
                    word_start = prevText;
                }

                prevBreak = currBreak;

                if (index > startIndex + lengthBreak)
                {
                    if (currBreak)
                    {
                        // eat the rest of the whitespace
                        while (index < stop && IsBreakChar(textInput[index]))
                        {
                            index++;
                        }

                        trailingCount = index - prevText;
                    }
                    else
                    {
                        // backup until a whitespace (or 1 char)
                        if (word_start == startIndex)
                        {
                            if (prevText > startIndex)
                            {
                                index = prevText;
                            }
                        }
                        else
                        {
                            index = word_start;
                        }
                    }

                    break;
                }

                if (currChar == '\n')
                {
                    var ret = index - startIndex;
                    var lineBreakSizeF = 1;
                    if (index < stop)
                    {
                        currChar = textInput[index++];
                        if (currChar == '\r')
                        {
                            ret = index - startIndex;
                            ++lineBreakSizeF;
                        }
                    }

                    trailingCount = lineBreakSizeF;

                    return ret;
                }

                if (currChar == '\r')
                {
                    var ret = index - startIndex;
                    var lineBreakSizeF = 1;
                    if (index < stop)
                    {
                        currChar = textInput[index++];
                        if (currChar == '\n')
                        {
                            ret = index - startIndex;
                            ++lineBreakSizeF;
                        }
                    }

                    trailingCount = lineBreakSizeF;

                    return ret;
                }
            }

            return index - startIndex;
        }

        private void BuildRectangleFs()
        {
            // Build character rects
            var align = _paint.TextAlign;

            for (var li = 0; li < _skiaLines.Count; li++)
            {
                var line = _skiaLines[li];
                var prevRight = TransformX(0, line.Width, align);
                var nextTop = line.Top + line.Height;

                if (li + 1 < _skiaLines.Count)
                {
                    nextTop = _skiaLines[li + 1].Top;
                }

                for (var i = line.Start; i < line.Start + line.TextLength; i++)
                {
                    var bounds = default(SKRect);
                    var c = Text[i];
                    var w = line.IsEmptyTrailingLine ? 0 : _paint.MeasureText(c.ToString(), ref bounds);

                    _rects.Add(new RectangleF(
                        prevRight + bounds.Location.X,
                        line.Top + bounds.Top + (_lineOffset / 2),
                        w,
                        bounds.Height));

                    prevRight += w;
                }
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

        private Color GetNextForegroundBrush(ref PrivateFormattedTextLine line, int index, out int length)
        {
            Color result = default;
            var len = length = line.Start + line.Length - index;

            if (_foregroundBrushes.Count > 0)
            {
                var bi = _foregroundBrushes.FindIndex(b => b.Key.StartIndex <= index && b.Key.EndIndex > index);

                if (bi > -1)
                {
                    var match = _foregroundBrushes[bi];

                    len = match.Key.EndIndex - index;
                    result = match.Value;

                    if (len > 0 && len < length)
                    {
                        length = len;
                    }
                }

                var endIndex = index + length;
                var max = bi == -1 ? _foregroundBrushes.Count : bi;
                var next = _foregroundBrushes.Take(max)
                    .Where(b => b.Key.StartIndex < endIndex && b.Key.StartIndex > index)
                    .OrderBy(b => b.Key.StartIndex)
                    .FirstOrDefault();

                if (next.Value != default)
                {
                    length = next.Key.StartIndex - index;
                }
            }

            return result;
        }

        private List<RectangleF> GetRectangles()
        {
            if (Text.Length > _rects.Count)
            {
                BuildRectangleFs();
            }

            return _rects;
        }

        private void Rebuild()
        {
            var length = Text.Length;

            _lines.Clear();
            _rects.Clear();
            _skiaLines = new List<PrivateFormattedTextLine>();

            var curOff = 0;
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

            string subString;

            var widthConstraint = float.IsNaN(_constraint.Width) ? -1 : _constraint.Width;

            while (curOff < length)
            {
                var measured = LineBreak(Text, curOff, length, out var trailingnumber);
                subString = Text.Substring(curOff, measured);
                var lineWidth = _paint.MeasureText(subString.Replace("\n", string.Empty).Replace("\r", string.Empty));
                var line = new PrivateFormattedTextLine
                {
                    Start = curOff,
                    TextLength = measured,
                    Length = measured - trailingnumber,
                    Width = lineWidth,
                    Height = _lineHeight,
                    Top = curY,
                };

                _skiaLines.Add(line);

                curY += _lineHeight;
                curY += mLeading;
                curOff += measured;

                // if this is the last line and there are trailing newline characters then
                // insert a additional line
                if (curOff >= length)
                {
                    var subStringMinusNewlines = subString.TrimEnd('\n', '\r');
                    var lengthDiff = subString.Length - subStringMinusNewlines.Length;
                    if (lengthDiff > 0)
                    {
                        var lastLineSubString = Text.Substring(line.Start, line.TextLength);
                        var lastLineWidth = _paint.MeasureText(lastLineSubString);
                        var lastLine = new PrivateFormattedTextLine
                        {
                            TextLength = lengthDiff,
                            Start = curOff - lengthDiff,
                            Length = 0,
                            Width = lastLineWidth,
                            Height = _lineHeight,
                            Top = curY,
                            IsEmptyTrailingLine = true,
                        };

                        _skiaLines.Add(lastLine);

                        curY += _lineHeight;
                        curY += mLeading;
                    }
                }
            }

            // Now convert to Avalonia data formats
            _lines.Clear();
            float maxX = 0;

            for (var c = 0; c < _skiaLines.Count; c++)
            {
                var w = _skiaLines[c].Width;
                if (maxX < w)
                    maxX = w;

                _lines.Add(new FormattedTextLine(_skiaLines[c].Start, _skiaLines[c].TextLength, _skiaLines[c].Height));
            }

            if (_skiaLines.Count == 0)
            {
                _lines.Add(new FormattedTextLine(0, 0, _lineHeight));
                _bounds = new RectangleF(0, 0, 0, _lineHeight);
            }
            else
            {
                var lastLine = _skiaLines[^1];
                
                _bounds = new RectangleF(0, 0, maxX, /*lastLine.Top + lastLine.Height*/_lineHeight * _skiaLines.Count);
            }
        }

        private float TransformX(float originX, float lineWidth, SKTextAlign align)
        {
            float x = 0;

            if (align == SKTextAlign.Left)
            {
                x = originX;
            }
            else
            {
                var width = _bounds.Width;

                switch (align)
                {
                    case SKTextAlign.Center: x = originX + ((width - lineWidth) / 2); break;
                    case SKTextAlign.Right: x = originX + (width - lineWidth); break;
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
            public int Length;
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
