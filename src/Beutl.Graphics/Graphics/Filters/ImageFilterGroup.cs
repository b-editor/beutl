using System.Text.Json.Nodes;

using Beutl.Animation;

using SkiaSharp;

namespace Beutl.Graphics.Filters;

public sealed class ImageFilterGroup : ImageFilter
{
    public static readonly CoreProperty<ImageFilters> ChildrenProperty;
    private readonly ImageFilters _children;

    static ImageFilterGroup()
    {
        ChildrenProperty = ConfigureProperty<ImageFilters, ImageFilterGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.Designable | PropertyFlags.Styleable)
            .Register();
    }

    public ImageFilterGroup()
    {
        _children = new ImageFilters();
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    public ImageFilters Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        Rect original = rect;

        foreach (IImageFilter item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
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
                _children.EnsureCapacity(childrenArray.Count);

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType)
                        && TypeFormat.ToType(atType) is Type type
                        && type.IsAssignableTo(typeof(ImageFilter))
                        && Activator.CreateInstance(type) is IMutableImageFilter imageFilter)
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

            foreach (IImageFilter item in _children.GetMarshal().Value)
            {
                if (item is IMutableImageFilter obj)
                {
                    JsonNode node = new JsonObject();
                    obj.WriteToJson(ref node);
                    node["@type"] = TypeFormat.ToString(item.GetType());

                    array.Add(node);
                }
            }

            jobject["children"] = array;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (IImageFilter item in Children.GetMarshal().Value)
        {
            (item as IAnimatable)?.ApplyAnimations(clock);
        }
    }

    protected internal override SKImageFilter ToSKImageFilter()
    {
        var array = new SKImageFilter[ValidEffectCount()];
        int index = 0;
        foreach (IImageFilter item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                array[index] = item.ToSKImageFilter();
                index++;
            }
        }

        if (array.Length > 0)
        {
            return SKImageFilter.CreateMerge(array);
        }
        else
        {
            return SKImageFilter.CreateOffset(0, 0);
        }
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (IImageFilter item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }
}
