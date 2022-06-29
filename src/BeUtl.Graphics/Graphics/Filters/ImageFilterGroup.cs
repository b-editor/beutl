using System.Text.Json.Nodes;

using SkiaSharp;

namespace BeUtl.Graphics.Filters;

public sealed class ImageFilterGroup : ImageFilter
{
    public static readonly CoreProperty<ImageFilters> ChildrenProperty;
    private readonly ImageFilters _children;

    static ImageFilterGroup()
    {
        ChildrenProperty = ConfigureProperty<ImageFilters, ImageFilterGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();
    }

    public ImageFilterGroup()
    {
        _children = new ImageFilters()
        {
            Attached = item => (item as ILogicalElement)?.NotifyAttachedToLogicalTree(new(this)),
            Detached = item => (item as ILogicalElement)?.NotifyDetachedFromLogicalTree(new(this)),
        };
        _children.Invalidated += (_, _) => RaiseInvalidated();
    }

    public ImageFilters Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        Rect original = rect;

        foreach (ImageFilter item in _children.AsSpan())
        {
            rect = item.TransformBounds(original).Union(rect);
        }

        return rect;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("children", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _children.Clear();
                if (_children.Capacity < childrenArray.Count)
                {
                    _children.Capacity = childrenArray.Count;
                }

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType)
                        && TypeFormat.ToType(atType) is Type type
                        && type.IsAssignableTo(typeof(ImageFilter))
                        && Activator.CreateInstance(type) is ImageFilter imageFilter)
                    {
                        imageFilter.ReadFromJson(childJson);
                        _children.Add(imageFilter);
                    }
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

            foreach (ImageFilter item in _children.AsSpan())
            {
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);
                node["@type"] = TypeFormat.ToString(item.GetType());

                array.Add(node);
            }

            jobject["children"] = array;
        }
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        var array = new SKImageFilter[_children.Count];
        int index = 0;
        foreach (ImageFilter item in _children.AsSpan())
        {
            array[index] = item.ToSKImageFilter();

            index++;
        }

        return SKImageFilter.CreateMerge(array);
    }
}
