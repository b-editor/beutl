using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class PropertyEditorGroupContext(IPropertyEditorContext?[] children, string groupName, bool isFirst) : IPropertyEditorContext
{
    public IReadOnlyList<IPropertyEditorContext?> Properties => children;

    public PropertyEditorExtension Extension => PropertyEditorExtension.Instance;

    public string GroupName { get; } = groupName;

    public bool IsFirst { get; } = isFirst;

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        throw new InvalidOperationException("PropertiesはEngineObjectPropertyViewModelがAcceptしています。");
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
        NestedEditorContextHelper.ReadEditorArray(json, nameof(Properties), children);
    }

    public void WriteToJson(JsonObject json)
    {
        NestedEditorContextHelper.WriteEditorArray(json, nameof(Properties), children);
    }
}
