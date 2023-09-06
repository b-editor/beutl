using System.Text.Json.Nodes;

using Beutl.Extensibility;

namespace Beutl.ViewModels.Editors;

public sealed class PropertyEditorGroupContext : IPropertyEditorContext
{
    private IPropertyEditorContext?[] _properties;

    public PropertyEditorGroupContext(IPropertyEditorContext?[] children, string groupName, bool isFirst)
    {
        _properties = children;
        GroupName = groupName;
        IsFirst = isFirst;
    }

    public IReadOnlyList<IPropertyEditorContext?> Properties => _properties;

    public PropertyEditorExtension Extension => PropertyEditorExtension.Instance;

    public string GroupName { get; }

    public bool IsFirst { get; }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        throw new InvalidOperationException("PropertiesはSourceOperatotViewModelがAcceptしています。");
    }

    public void Dispose()
    {
        foreach (IPropertyEditorContext? item in _properties.AsSpan())
        {
            item?.Dispose();
        }

        _properties = Array.Empty<IPropertyEditorContext?>();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(Properties), out JsonNode? childrenNode)
            && childrenNode is JsonArray childrenArray)
        {
            foreach ((JsonNode? node, IPropertyEditorContext? context) in childrenArray.Zip(_properties))
            {
                if (context != null && node != null)
                {
                    context.ReadFromJson(node.AsObject());
                }
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        var array = new JsonArray();

        foreach (IPropertyEditorContext? item in _properties.AsSpan())
        {
            if (item == null)
            {
                array.Add(null);
            }
            else
            {
                var node = new JsonObject();
                item.WriteToJson(node);
                array.Add(node);
            }
        }

        json[nameof(Properties)] = array;
    }
}
