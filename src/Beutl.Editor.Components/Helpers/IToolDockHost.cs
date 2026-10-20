using System.Text.Json.Nodes;

namespace Beutl.Editor.Components.Helpers;

// The editor's dock, for tool tabs that save, apply or reset its layout.
internal interface IToolDockHost
{
    JsonObject CaptureLayout();

    bool ApplyLayout(JsonObject layout);

    void ResetLayout();
}
