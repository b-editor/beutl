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
    /// <summary>
    /// Represents a piece of text with formatting.
    /// </summary>
    public class FormattedText : IDisposable
    {
        private readonly List<FormattedTextLine> _lines = new();
        private readonly SKPaint _paint;
        private SKFontMetrics _fontMetrics;
        private float _lineHeight;
        private RectangleF _bounds;
        private bool _propertyChanged;
        private string _text;
        private Font _font;
        private float _fontSize;
        private float _lineSpacing;
        private float _characterSpacing;
        private bool _stroke;
        private float _strokeWidth;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormattedText"/> class.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="font">The font.</param>
        /// <param name="fontSize">The font size.</param>
        /// <param name="textAlignment">The alignment of the text.</param>
        /// <param name="spans">A collection of spans that describe the formatting of subsections of the text.</param>
        public FormattedText(
            string text,
            Font font,
            float fontSize,
            TextAlignment textAlignment,
            FormattedTextStyleSpan[] spans)
        {
            _text = text ?? string.Empty;
            _font = font ?? throw new ArgumentNullException(nameof(font));
            _fontSize = fontSize;

            Spans = spans ?? throw new ArgumentNullException(nameof(spans));
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
            Rebuild();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="FormattedText"/> class.
        /// </summary>
        ~FormattedText()
        {
            Dispose();
        }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets the bounds of the text.
        /// </summary>
        public RectangleF Bounds => _bounds;

        /// <summary>
        /// Gets or sets the font.
        /// </summary>
        public Font Font
        {
            get => _font;
            set => Set(ref _font, value);
        }

        /// <summary>
        /// Gets or sets the font size.
        /// </summary>
        public float FontSize
        {
            get => _fontSize;
            set => Set(ref _fontSize, value);
        }

        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        public string Text
        {
            get => _text;
            set => Set(ref _text, value);
        }

        /// <summary>
        /// Gets or sets the alignment of the text.
        /// </summary>
        public TextAlignment TextAlignment { get; set; }

        /// <summary>
        /// Gets or sets the align to the baseline.
        /// </summary>
        public bool AlignBaseline { get; set; }

        /// <summary>
        /// Gets or sets the line spacing of the text.
        /// </summary>
        public float LineSpacing
        {
            get => _lineSpacing;
            set => Set(ref _lineSpacing, value);
        }

        /// <summary>
        /// Gets or sets the character spacing of the text.
        /// </summary>
        public float CharacterSpacing
        {
            get => _characterSpacing;
            set => Set(ref _characterSpacing, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether to paint a stroke or the fill.
        /// </summary>
        public bool IsStroke
        {
            get => _stroke;
            set => Set(ref _stroke, value);
        }

        /// <summary>
        /// Gets or sets the stroke width.
        /// </summary>
        public float StrokeWidth
        {
            get => _strokeWidth;
            set => Set(ref _strokeWidth, value);
        }

        /// <summary>
        /// Gets or sets the stroke join.
        /// </summary>
        public SKStrokeJoin StrokeJoin { get; set; }

        /// <summary>
        /// Gets or sets the stroke miter.
        /// </summary>
        public float StrokeMiter { get; set; }

        /// <summary>
        /// Gets or sets a collection of spans that describe the formatting of subsections of the text.
        /// </summary>
        public FormattedTextStyleSpan[] Spans { get; set; }

        /// <summary>
        /// Gets the lines in the text.
        /// </summary>
        /// <returns>
        /// A collection of <see cref="FormattedTextLine"/> objects.
        /// </returns>
        public IEnumerable<FormattedTextLine> Lines => _lines;

        /// <summary>
        /// Draws this <see cref="FormattedText"/>.
        /// </summary>
        /// <returns>Returns the drawn image.</returns>
        public IEnumerable<FormattedTextCharacter> DrawMultiple()
        {
            if (IsStroke)
            {
                return DrawMultipleStroke();
            }
            else
            {
                return DrawMultipleCore();
            }
        }

        /// <summary>
        /// Draws this <see cref="FormattedText"/>.
        /// </summary>
        /// <returns>Returns the drawn image.</returns>
        public Image<BGRA32> Draw()
        {
            if (IsStroke)
            {
                return DrawStroke();
            }
            else
            {
                return DrawCore();
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Text;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                _paint.Dispose();
                GC.SuppressFinalize(this);
                IsDisposed = true;
            }
        }

        private static string[] GetLines(string text)
        {
            return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private (Color Foreground, Color Stroke) GetColor(int line, int index, int length)
        {
            for (var i = 0; i < Spans.Length; i++)
            {
                var span = Spans[i];
                var (offset, len) = span.Range.GetOffsetAndLength(length);
                if ((span.LineNumber == line || span.LineNumber < 0) && (offset <= index || index < len))
                {
                    return (span.ForegroundBrush, span.StrokeColor);
                }
            }

            return (Colors.White, Colors.White);
        }

        private void Rebuild()
        {
            _lines.Clear();
            _paint.TextSize = FontSize;
            _paint.Typeface = Font.GetTypeface();

            if (IsStroke)
            {
                _paint.StrokeWidth = StrokeWidth;
                _paint.Style = SKPaintStyle.StrokeAndFill;
            }
            else
            {
                _paint.StrokeWidth = 0;
                _paint.Style = SKPaintStyle.Fill;
            }

            _fontMetrics = _paint.FontMetrics;

            var curY = _paint.StrokeWidth;

            var metrics = _paint.FontMetrics;

            // var mTop = metrics.Top; // ベースラインからの最大距離 (上) (0以上)
            // var mBottom = metrics.Bottom; // ベースラインからの最大距離 (下) (0以下)
            var mLeading = metrics.Leading; // テキストの行間に追加する推奨距離 (0以下)
            var mDescent = metrics.Descent; // ベースラインからの推奨距離 (下) (0以下)
            var mAscent = metrics.Ascent; // ベースラインからの推奨距離 (上) (0以上)

            // 行の高さ
            _lineHeight = mDescent - mAscent;

            var lines = GetLines(Text);

            _lines.Clear();
            var maxWidth = 0F;
            var maxHeight = 0F;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineWidth = _paint.MeasureText(line);
                FormattedTextLine? item;
                if (i is 0)
                {
                    item = new FormattedTextLine(
                        line,
                        curY,
                        lineWidth + (CharacterSpacing * (line.Length - 1)),
                        _lineHeight);
                }
                else
                {
                    item = new FormattedTextLine(
                        line,
                        curY,
                        lineWidth + (CharacterSpacing * (line.Length - 1)),
                        _lineHeight + LineSpacing);
                }

                _lines.Add(item);

                if (maxWidth < item.Width)
                {
                    maxWidth = item.Width;
                }

                maxHeight += item.Height;
                curY += _lineHeight;
                curY += mLeading;
                curY += LineSpacing;
            }

            if (_lines.Count == 0)
            {
                _lines.Add(new FormattedTextLine(string.Empty, 0, 0, _lineHeight));
                _bounds = new RectangleF(0, 0, 0, _lineHeight);
            }
            else
            {
                _bounds = new RectangleF(0, 0, maxWidth + (_paint.StrokeWidth * 2), maxHeight + (_paint.StrokeWidth * 2));
            }

            _propertyChanged = false;
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

        private void Set<T>(ref T field, T value)
        {
            if (field != null && field.Equals(value))
            {
                return;
            }

            field = value;

            _propertyChanged = true;
        }

        private Image<BGRA32> DrawCore()
        {
            if (_propertyChanged) Rebuild();

            using var bmp = new SKBitmap(new SKImageInfo((int)Bounds.Width, (int)Bounds.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var nextTop = line.Top + line.Height;
                var prevRight = TransformX(0, line.Width, TextAlignment);

                if (i + 1 < _lines.Count)
                {
                    nextTop = _lines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var (color, stroke) = GetColor(i, li, line.Text.Length);
                    var bounds = default(SKRect);
                    var w = _paint.MeasureText(c.ToString(), ref bounds);
                    _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                    canvas.Translate(prevRight + bounds.Left, line.Top);

                    // DrawTextのYはベースライン
                    canvas.DrawText(
                        c.ToString(),
                        (bounds.Width / 2) - bounds.MidX,
                        -_fontMetrics.Ascent,
                        _paint);

                    canvas.ResetMatrix();
                    prevRight += w;
                    prevRight += CharacterSpacing;
                }
            }

            return bmp.ToImage32();
        }

        private Image<BGRA32> DrawStroke()
        {
            if (_propertyChanged) Rebuild();
            _paint.StrokeMiter = StrokeMiter;
            _paint.StrokeJoin = StrokeJoin;

            using var bmp1 = new SKBitmap(new SKImageInfo((int)Bounds.Width, (int)Bounds.Height, SKColorType.Bgra8888));
            using var canvas1 = new SKCanvas(bmp1);

            using var bmp2 = new SKBitmap(bmp1.Info);
            using var canvas2 = new SKCanvas(bmp2);

            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var nextTop = line.Top + line.Height;
                var prevRight = TransformX(StrokeWidth, line.Width, TextAlignment);

                if (i + 1 < _lines.Count)
                {
                    nextTop = _lines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var (color, stroke) = GetColor(i, li, line.Text.Length);
                    var bounds = default(SKRect);
                    var strokeBounds = default(SKRect);

                    // ストロークなしのBoundsを図る
                    _paint.Style = SKPaintStyle.Fill;
                    _paint.MeasureText(c.ToString(), ref bounds);

                    // ストロークありのBoundsを図る
                    _paint.Style = SKPaintStyle.StrokeAndFill;
                    var w = _paint.MeasureText(c.ToString(), ref strokeBounds);

                    // ストロークを描画
                    {
                        _paint.Style = SKPaintStyle.StrokeAndFill;
                        _paint.Color = new SKColor(stroke.R, stroke.G, stroke.B, stroke.A);

                        canvas2.Translate(prevRight + strokeBounds.Left, line.Top);

                        // DrawTextのYはベースライン
                        canvas2.DrawText(
                            c.ToString(),
                            (strokeBounds.Width / 2) - strokeBounds.MidX,
                            -_fontMetrics.Ascent,
                            _paint);

                        canvas2.ResetMatrix();
                    }

                    // 文字本体を描画
                    {
                        _paint.Style = SKPaintStyle.Fill;
                        _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                        canvas1.Translate(prevRight + bounds.Left, line.Top);

                        // DrawTextのYはベースライン
                        canvas1.DrawText(
                            c.ToString(),
                            (bounds.Width / 2) - bounds.MidX,
                            -_fontMetrics.Ascent,
                            _paint);

                        canvas1.ResetMatrix();
                    }

                    prevRight += w;
                    prevRight += CharacterSpacing;
                }
            }

            canvas2.DrawBitmap(bmp1, 0, 0);

            return bmp2.ToImage32();
        }

        private IEnumerable<FormattedTextCharacter> DrawMultipleCore()
        {
            if (_propertyChanged) Rebuild();

            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var nextTop = line.Top + line.Height;
                var prevRight = TransformX(0, line.Width, TextAlignment);

                if (i + 1 < _lines.Count)
                {
                    nextTop = _lines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var (color, _) = GetColor(i, li, line.Text.Length);
                    var bounds = default(SKRect);
                    var w = _paint.MeasureText(c.ToString(), ref bounds);
                    _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                    if (AlignBaseline)
                    {
                        using var bmp = new SKBitmap(new SKImageInfo((int)w, (int)(nextTop - line.Top), SKColorType.Bgra8888));
                        using var canvas = new SKCanvas(bmp);

                        var resultRect = new RectangleF(
                            prevRight + bounds.Left,
                            line.Top,
                            w,
                            nextTop - line.Top);
                        canvas.DrawText(c.ToString(), (bounds.Width / 2) - bounds.MidX, -_fontMetrics.Ascent, _paint);

                        prevRight += w;
                        prevRight += CharacterSpacing;

                        yield return new(bmp.ToImage32(), resultRect);
                    }
                    else
                    {
                        using var bmp = new SKBitmap(new SKImageInfo((int)bounds.Width, (int)bounds.Height, SKColorType.Bgra8888));
                        using var canvas = new SKCanvas(bmp);

                        var resultRect = new RectangleF(
                            prevRight + bounds.Left,
                            line.Top + bounds.Top - _fontMetrics.Ascent,
                            bounds.Width,
                            bounds.Height);
                        canvas.DrawText(c.ToString(), (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY, _paint);

                        prevRight += w;
                        prevRight += CharacterSpacing;

                        yield return new(bmp.ToImage32(), resultRect);
                    }
                }
            }
        }

        private IEnumerable<FormattedTextCharacter> DrawMultipleStroke()
        {
            if (_propertyChanged) Rebuild();
            _paint.StrokeMiter = StrokeMiter;
            _paint.StrokeJoin = StrokeJoin;

            for (var i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var nextTop = line.Top + line.Height;
                var prevRight = TransformX(0, line.Width, TextAlignment);

                if (i + 1 < _lines.Count)
                {
                    nextTop = _lines[i + 1].Top;
                }

                for (var li = 0; li < line.Text.Length; li++)
                {
                    var c = line.Text[li];
                    var (color, stroke) = GetColor(i, li, line.Text.Length);
                    var strokeBounds = default(SKRect);

                    // ストロークありのBoundsを図る
                    _paint.Style = SKPaintStyle.StrokeAndFill;
                    var w = _paint.MeasureText(c.ToString(), ref strokeBounds);

                    if (AlignBaseline)
                    {
                        using var bmp = new SKBitmap(new SKImageInfo((int)strokeBounds.Width, (int)(nextTop - line.Top + (_paint.StrokeWidth * 2)), SKColorType.Bgra8888));
                        using var canvas = new SKCanvas(bmp);
                        canvas.Translate(0, _paint.StrokeWidth);
                        var x = (strokeBounds.Width / 2) - strokeBounds.MidX;

                        // ストロークを描画
                        {
                            _paint.Style = SKPaintStyle.StrokeAndFill;
                            _paint.Color = new SKColor(stroke.R, stroke.G, stroke.B, stroke.A);

                            canvas.DrawText(c.ToString(), x, -_fontMetrics.Ascent, _paint);
                        }

                        // 文字本体を描画
                        {
                            _paint.Style = SKPaintStyle.Fill;
                            _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                            canvas.DrawText(c.ToString(), x, -_fontMetrics.Ascent, _paint);
                        }

                        var resultRect = new RectangleF(
                            prevRight + strokeBounds.Left + _paint.StrokeWidth,
                            line.Top,
                            bmp.Width,
                            nextTop - line.Top);

                        prevRight += w;
                        prevRight += CharacterSpacing;

                        yield return new(bmp.ToImage32(), resultRect);
                    }
                    else
                    {
                        using var bmp = new SKBitmap(new SKImageInfo((int)strokeBounds.Width, (int)strokeBounds.Height, SKColorType.Bgra8888));
                        using var canvas = new SKCanvas(bmp);

                        var x = (strokeBounds.Width / 2) - strokeBounds.MidX;
                        var y = (strokeBounds.Height / 2) - strokeBounds.MidY;

                        // ストロークを描画
                        {
                            _paint.Style = SKPaintStyle.StrokeAndFill;
                            _paint.Color = new SKColor(stroke.R, stroke.G, stroke.B, stroke.A);

                            canvas.DrawText(c.ToString(), x, y, _paint);
                        }

                        // 文字本体を描画
                        {
                            _paint.Style = SKPaintStyle.Fill;
                            _paint.Color = new SKColor(color.R, color.G, color.B, color.A);

                            canvas.DrawText(c.ToString(), x, y, _paint);
                        }

                        var resultRect = new RectangleF(
                            prevRight + strokeBounds.Left + _paint.StrokeWidth,
                            line.Top + strokeBounds.Top - _fontMetrics.Ascent,
                            strokeBounds.Width,
                            strokeBounds.Height);

                        prevRight += w;
                        prevRight += CharacterSpacing;

                        yield return new(bmp.ToImage32(), resultRect);
                    }
                }
            }
        }
    }
}
