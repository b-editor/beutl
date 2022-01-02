using System.Text.Json.Nodes;

namespace BEditorNext.ProjectSystem;

public class RenderOperationViewState : BaseViewState
{
    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetAndRaise(ref _isExpanded, value);
    }

    public override void FromJson(JsonNode json)
    {
        if (json is JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("isExpanded", out JsonNode? isExpandedNode) &&
                isExpandedNode is JsonValue isExpandedValue &&
                isExpandedValue.TryGetValue(out bool isExpanded))
            {
                IsExpanded = isExpanded;
            }
        }
    }

    public override JsonNode ToJson()
    {
        return new JsonObject
        {
            ["isExpanded"] = IsExpanded
        };
    }
}
