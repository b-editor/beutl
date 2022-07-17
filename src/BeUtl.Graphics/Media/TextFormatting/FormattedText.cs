using System.Diagnostics;
using System.Text.Json.Nodes;

using BeUtl.Graphics;

using SkiaSharp;

namespace BeUtl.Media.TextFormatting;

[DebuggerDisplay("{Text}")]
public struct FormattedText_ : IEquatable<FormattedText_>
{
    private FontWeight _weight = FontWeight.Regular;
    private FontStyle _style = FontStyle.Normal;
    private FontFamily _font = FontFamily.Default;
    private float _size = 11;
    private float _spacing = 0;
    private StringSpan _text = StringSpan.Empty;
    private FontMetrics _metrics = default;
    private Size _bounds = default;
    private bool _isDirty = false;

    public FormattedText_()
    {
    }

    public FontWeight Weight
    {
        get => _weight;
        set => SetProperty(ref _weight, value);
    }

    public FontStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value);
    }

    public FontFamily Font
    {
        get => _font;
        set => SetProperty(ref _font, value);
    }

    // > 0
    public float Size
    {
        get => _size;
        set => SetProperty(ref _size, value);
    }

    // >= 0
    public float Spacing
    {
        get => _spacing;
        set => SetProperty(ref _spacing, value);
    }

    // 改行コードは含まない
    public StringSpan Text
    {
        get => _text;
        set
        {
            ReadOnlySpan<char> span = value.AsSpan();
            if (span.Contains('\n') || span.Contains('\r'))
            {
                throw new Exception("Cannot contain newline codes.");
            }
            SetProperty(ref _text, value);
        }
    }

    public bool BeginOnNewLine { get; set; } = false;

    public Thickness Margin { get; set; } = new();

    public FontMetrics Metrics => MeasureAndSetField().Metrics;

    public Size Bounds => MeasureAndSetField().Bounds;

    public override bool Equals(object? obj)
    {
        return obj is FormattedText_ text && Equals(text);
    }

    public bool Equals(FormattedText_ other)
    {
        return _weight == other._weight
            && _style == other._style
            && _font.Equals(other._font)
            && _size == other._size
            && _spacing == other._spacing
            && _text == other._text;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_weight, _style, _font, _size, _spacing, _text);
    }

    private (FontMetrics, Size) Measure()
    {
        using SKTypeface typeface = new Typeface(Font, Style, Weight).ToSkia();
        using SKPaint paint = new()
        {
            TextSize = Size,
            Typeface = typeface
        };

        FontMetrics fontMetrics = paint.FontMetrics.ToFontMetrics();
        float w = paint.MeasureText(Text.AsSpan());
        var size = new Size(
            w + (Text.Length - 1) * Spacing,
            fontMetrics.Descent - fontMetrics.Ascent);

        return (fontMetrics, size);
    }

    private void SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            _isDirty = true;
        }
    }

    private (FontMetrics Metrics, Size Bounds) MeasureAndSetField()
    {
        if (_isDirty)
        {
            (_metrics, _bounds) = Measure();
        }

        return (_metrics, _bounds);
    }

    public static bool operator ==(FormattedText_ left, FormattedText_ right) => left.Equals(right);

    public static bool operator !=(FormattedText_ left, FormattedText_ right) => !(left == right);
}

public sealed class TextLines : AffectsRenders<TextLine>
{

}

public class FormattedText : Drawable
{
    public static readonly CoreProperty<TextLines> LinesProperty;
    private readonly TextLines _lines;

    static FormattedText()
    {
        LinesProperty = ConfigureProperty<TextLines, FormattedText>(nameof(Lines))
            .Accessor(o => o.Lines, (o, v) => o.Lines = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();
    }

    public FormattedText()
    {
        _lines = new()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _lines.Invalidated += (_, _) => Invalidate();
    }

    private FormattedText(List<TextLine> lines)
        : this()
    {
        _lines.AddRange(lines);
    }

    public TextLines Lines
    {
        get => _lines;
        set => _lines.Replace(value);
    }

    public static FormattedText Parse(string s, FormattedTextInfo info)
    {
        var tokenizer = new FormattedTextParser(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        return new FormattedText(lines);
    }

    public void Load(string s, FormattedTextInfo info)
    {
        var tokenizer = new FormattedTextParser(s);
        List<TextLine> lines = tokenizer.ToLines(info);

        _lines.Replace(lines);
        Invalidate();
    }

    protected override void OnDraw(ICanvas canvas)
    {
        DrawCore(canvas);
    }

    private void DrawCore(ICanvas canvas)
    {
        float prevBottom = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            TextLine line = Lines[i];
            line.Measure(canvas.Size.ToSize(1));
            Rect lineBounds = line.Bounds;

            using (canvas.PushTransform(Matrix.CreateTranslation(0, prevBottom)))
            {
                line.Draw(canvas);
                prevBottom += lineBounds.Height;
            }
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        float width = 0;
        float height = 0;

        foreach (TextLine line in Lines)
        {
            line.Measure(availableSize);
            Rect bounds = line.Bounds;
            width = MathF.Max(bounds.Width, width);
            height += bounds.Height;
        }

        return new Size(width, height);
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("lines", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _lines.Clear();
                if (_lines.Capacity < childrenArray.Count)
                {
                    _lines.Capacity = childrenArray.Count;
                }

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    var item = new TextLine();
                    item.ReadFromJson(childJson);
                    _lines.Add(item);
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (json is JsonObject jobject)
        {
            var array = new JsonArray();

            foreach (TextLine item in _lines.AsSpan())
            {
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);

                array.Add(node);
            }

            jobject["lines"] = array;
        }
    }
}
