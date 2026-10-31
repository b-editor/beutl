using System.Globalization;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using SkiaSharp;

namespace Beutl.Media.TextFormatting;

// Owns the oriented runs; both measurement and rendering consume this layout.
internal sealed class VerticalTextLayout : IDisposable
{
    internal sealed record Run(FormattedText Text, float Offset)
    {
        public bool Sideways => Text.Orientation == TextRunOrientation.Sideways;
    }
    internal sealed record Column(List<Run> Runs, float Width, float Height);
    public List<Column> Columns { get; } = [];
    public Size Size { get; private set; }

    public VerticalTextLayout(TextElements elements, Dictionary<(SKTypeface Typeface, string Cluster), bool> alternates)
    {
        var shapers = new Dictionary<SKTypeface, TextShaper>();
        try
        {
            FormattedText? lastSource = null;
            foreach (Span<FormattedText> line in elements.Lines)
            {
                var runs = new List<Run>();
                // Register ownership before shaping so a failed measurement releases all runs.
                Columns.Add(new Column(runs, 0, 0));
                float height = 0;
                float width = 0;
                foreach (FormattedText source in line)
                {
                    lastSource = source;
                    string text = source.Text.ToString();
                    if (text.Length == 0)
                        width = MathF.Max(width, MeasureEmptyColumn(source));
                    int start = 0;
                    bool? previousSideways = null;
                    using SKFont font = source.ToSKFont();
                    var enumerator = StringInfo.GetTextElementEnumerator(text);
                    while (enumerator.MoveNext())
                    {
                        string cluster = enumerator.GetTextElement();
                        int orientation = VerticalOrientation.Get(cluster);
                        bool sideways = orientation == 0
                            || (orientation == 2 && !HasVerticalAlternate(source, font, cluster, alternates, shapers));
                        if (previousSideways is { } previous && previous != sideways)
                        {
                            AddRun(source, text[start..enumerator.ElementIndex], previous);
                            start = enumerator.ElementIndex;
                        }
                        previousSideways = sideways;
                    }
                    if (previousSideways is { } last)
                        AddRun(source, text[start..], last);
                }
                Columns[^1] = new Column(runs, width, height);
                Size = new Size(Size.Width + width, MathF.Max(Size.Height, height));

                void AddRun(FormattedText source, string text, bool sideways)
                {
                    var formatted = new FormattedText
                    {
                        Text = text,
                        Font = source.Font,
                        Style = source.Style,
                        Weight = source.Weight,
                        Size = source.Size,
                        Spacing = source.Spacing,
                        Brush = source.Brush,
                        Pen = source.Pen,
                        Orientation = sideways ? TextRunOrientation.Sideways : TextRunOrientation.Upright,
                    };
                    runs.Add(new Run(formatted, height + source.Spacing / 2));
                    Rect bounds = formatted.Bounds;
                    height += (sideways ? bounds.Width : bounds.Height) + source.Spacing;
                    width = MathF.Max(width, MeasureColumnWidth(formatted.Size, formatted.Metrics));
                }
            }
            // The shared line enumerator omits the final empty line after a trailing newline.
            if (lastSource is not null && elements[^1] is { IgnoreLineBreaks: false } lastElement
                && (lastElement.Text.EndsWith('\n') || lastElement.Text.EndsWith('\r')))
            {
                float width = MeasureEmptyColumn(lastSource);
                Columns.Add(new Column([], width, 0));
                Size = new Size(Size.Width + width, Size.Height);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            foreach (TextShaper shaper in shapers.Values)
                shaper.Dispose();
        }
    }

    private static float MeasureColumnWidth(float size, FontMetrics metrics)
        => MathF.Max(size, metrics.Descent - metrics.Ascent + metrics.Leading);

    private static float MeasureEmptyColumn(FormattedText source)
    {
        // An empty column uses the active font without shaping the preceding occupied text.
        using var font = source.ToSKFont();
        return MeasureColumnWidth(source.Size, font.Metrics.ToFontMetrics());
    }

    internal static Matrix GetTransform(Run run, float center, float top)
    {
        if (!run.Sideways)
            return Matrix.CreateTranslation(center, top + run.Offset);
        FontMetrics metrics = run.Text.Metrics;
        return Matrix.CreateRotation(MathF.PI / 2)
            * Matrix.CreateTranslation(center + (metrics.Ascent + metrics.Descent) / 2, top + run.Offset);
    }

    private static bool HasVerticalAlternate(FormattedText source, SKFont font, string cluster,
        Dictionary<(SKTypeface Typeface, string Cluster), bool> alternates, Dictionary<SKTypeface, TextShaper> shapers)
    {
        foreach (TextFontFallback.Run run in TextFontFallback.GetRuns(cluster.AsSpan(), font, source.Style, source.Weight))
        {
            var key = (run.Typeface, cluster);
            if (alternates.TryGetValue(key, out bool hasAlternate))
            {
                if (hasAlternate)
                    return true;
                continue;
            }
            if (!shapers.TryGetValue(run.Typeface, out TextShaper? shaper))
            {
                // Opening the same font data per cluster dominates text with many rotatable clusters.
                shaper = new TextShaper(run.Typeface);
                shapers.Add(run.Typeface, shaper);
            }
            using var horizontal = new HarfBuzzSharp.Buffer();
            horizontal.AddUtf16(cluster.AsSpan(), run.Start, run.Length);
            horizontal.GuessSegmentProperties();
            var h = shaper.ShapeWithAdvance(horizontal, font);
            using var vertical = new HarfBuzzSharp.Buffer();
            vertical.AddUtf16(cluster.AsSpan(), run.Start, run.Length);
            vertical.GuessSegmentProperties();
            vertical.Direction = HarfBuzzSharp.Direction.TopToBottom;
            var v = shaper.ShapeWithAdvance(vertical, font);
            hasAlternate = !h.Codepoints.AsSpan().SequenceEqual(v.Codepoints);
            alternates.Add(key, hasAlternate);
            if (hasAlternate)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (Column column in Columns)
            foreach (Run run in column.Runs)
                run.Text.Dispose();
        Columns.Clear();
    }
}
