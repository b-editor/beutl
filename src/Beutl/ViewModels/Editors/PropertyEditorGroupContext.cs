using System.Text.Json.Nodes;

namespace Beutl.ViewModels.Editors;

public sealed class PropertyEditorGroupContext(IPropertyEditorContext?[] children, string groupName, bool isFirst) : IPropertyEditorContext
{
    public IReadOnlyList<IPropertyEditorContext?> Properties => children;

    public PropertyEditorExtension Extension => PropertyEditorExtension.Instance;

    public string GroupName { get; } = groupName;

    public bool IsFirst { get; } = isFirst;

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        throw new InvalidOperationException("PropertiesはSourceOperatotViewModelがAcceptしています。");
    }

    public void Dispose()
    {
        foreach (IPropertyEditorContext? item in children.AsSpan())
        {
            item?.Dispose();
        }

        children = [];
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(Properties), out JsonNode? childrenNode)
            && childrenNode is JsonArray childrenArray)
        {
            foreach ((JsonNode? node, IPropertyEditorContext? context) in childrenArray.Zip(children))
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

        foreach (IPropertyEditorContext? item in children.AsSpan())
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
