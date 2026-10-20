using System.Text.Json.Nodes;

namespace Beutl.Editor.Components.Helpers;

// The editor's dock, for tool tabs that save, apply or reset its layout.
internal interface IToolDockHost
{
    JsonObject CaptureLayout();

    bool ApplyLayout(JsonObject layout);

    void ResetLayout();

    // Disables closing the tool's tab until the result is disposed, for work that must not lose its tab.
    IDisposable KeepOpen(IToolContext tool);
}
