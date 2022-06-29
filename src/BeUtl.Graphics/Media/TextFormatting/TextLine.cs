using System.Text.Json.Nodes;

using BeUtl.Graphics;

namespace BeUtl.Media.TextFormatting;

public sealed class TextElements : AffectsRenders<TextElement>
{

}

public sealed class TextLine : Drawable, ILogicalElement
{
    public static readonly CoreProperty<TextElements> ElementsProperty;
    private readonly TextElements _elements;

    static TextLine()
    {
        ElementsProperty = ConfigureProperty<TextElements, TextLine>(nameof(Elements))
            .Accessor(o => o.Elements, (o, v) => o.Elements = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();
    }

    public TextLine()
    {
        _elements = new()
        {
            Attached = item => (item as ILogicalElement).NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement).NotifyDetachedFromLogicalTree(new(this)),
        };
        _elements.Invalidated += (_, _) => Invalidate();
    }

    public TextElements Elements
    {
        get => _elements;
        set => _elements.Replace(value);
    }

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Elements;

    public float MinAscent()
    {
        float ascent = 0;
        foreach (TextElement item in Elements)
        {
            ascent = MathF.Min(item.FontMetrics.Ascent, ascent);
        }

        return ascent;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("elements", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _elements.Clear();
                if (_elements.Capacity < childrenArray.Count)
                {
                    _elements.Capacity = childrenArray.Count;
                }

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    var item = new TextElement();
                    item.ReadFromJson(childJson);
                    _elements.Add(item);
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

            foreach (TextElement item in _elements.AsSpan())
            {
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);

                array.Add(node);
            }

            jobject["elements"] = array;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        float width = 0;
        float height = 0;

        foreach (TextElement element in Elements)
        {
            element.Measure(availableSize);
            Rect bounds = element.Bounds;
            width += bounds.Width;
            width += element.Margin.Left + element.Margin.Right;

            height = MathF.Max(bounds.Height + element.Margin.Top + element.Margin.Bottom, height);
        }

        return new Size(width, height);

    }

    protected override void OnDraw(ICanvas canvas)
    {
        float ascent = MinAscent();

        using (canvas.PushTransform(Matrix.CreateTranslation(0, -ascent)))
        {
            float prevRight = 0;
            foreach (TextElement element in Elements)
            {
                canvas.Translate(new(prevRight, 0));
                Rect elementBounds = element.Bounds;

                element.Draw(canvas);

                prevRight = elementBounds.Width + element.Margin.Right;
            }
        }
    }
}
