using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

public static class NestedEditorContextHelper
{
    public static void AcceptChildren(
        IPropertyEditorContextVisitor visitor,
        IPropertyEditorContext? group,
        IPropertiesEditorViewModel? properties)
    {
        group?.Accept(visitor);
        if (properties != null)
        {
            foreach (IPropertyEditorContext item in properties.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public static void ReadNestedJson(
        JsonObject json,
        ReactivePropertySlim<bool> isExpanded,
        IPropertiesEditorViewModel? properties,
        IJsonSerializable? group = null)
    {
        try
        {
            if (json.TryGetPropertyValue("IsExpanded", out JsonNode? isExpandedNode)
                && isExpandedNode is JsonValue isExpandedValue)
            {
                isExpanded.Value = (bool)isExpandedValue;
            }

            properties?.ReadFromJson(json);

            if (group != null
                && json.TryGetPropertyValue("Group", out JsonNode? groupNode)
                && groupNode is JsonObject groupJson)
            {
                group.ReadFromJson(groupJson);
            }
        }
        catch
        {
        }
    }

    public static void WriteNestedJson(
        JsonObject json,
        bool isExpanded,
        IPropertiesEditorViewModel? properties,
        IJsonSerializable? group = null)
    {
        try
        {
            json["IsExpanded"] = isExpanded;
            properties?.WriteToJson(json);

            if (group != null)
            {
                var groupJson = new JsonObject();
                group.WriteToJson(groupJson);
                json["Group"] = groupJson;
            }
        }
        catch
        {
        }
    }
}

public sealed record ChildVisitor(IServiceProvider Owner) : IServiceProvider, IPropertyEditorContextVisitor
{
    public object? GetService(Type serviceType) => Owner.GetService(serviceType);

    public void Visit(IPropertyEditorContext context)
    {
    }
}
