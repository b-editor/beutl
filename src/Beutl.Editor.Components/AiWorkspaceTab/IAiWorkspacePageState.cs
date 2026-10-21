using System.Text.Json.Nodes;

namespace Beutl.Editor.Components.AiWorkspaceTab;

// What a page keeps in the tab's saved layout besides which page it is.
internal interface IAiWorkspacePageState
{
    void ReadFromJson(JsonObject json);

    void WriteToJson(JsonObject json);
}
