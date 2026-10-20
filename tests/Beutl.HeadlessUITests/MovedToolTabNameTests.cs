using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.TransitionTab;
using Beutl.Editor.Components.VersionControlTab;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;

namespace Beutl.HeadlessUITests;

// The tool tabs moved out of the app keep the layouts and pins saved under the name the app wrote for them.
[TestFixture]
public sealed class MovedToolTabNameTests
{
    private static readonly (ToolTabExtension Extension, string FormerName)[] s_moved =
    [
        (TransitionTabExtension.Instance, "[Beutl].Services.PrimitiveImpls:TransitionTabExtension"),
        (VersionControlTabExtension.Instance, "[Beutl].Services.PrimitiveImpls:VersionControlTabExtension"),
    ];

    [AvaloniaTest]
    public async Task A_layout_and_a_pin_saved_under_the_former_name_find_the_tab()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("moved-tool-tab");
        JsonObject layout = editor.DockHost.CaptureLayout();
        JsonArray tools = FirstToolList(layout)!;
        foreach ((_, string formerName) in s_moved)
        {
            tools.Add(new JsonObject
            {
                ["$type"] = "tool",
                ["extension"] = new JsonObject { ["$type"] = formerName }
            });
        }

        Assert.That(editor.DockHost.ApplyLayout(layout), Is.True);
        ToolTabExtension[] opened = [.. editor.DockHost.Factory.EnumerateTools().Select(tool => tool.ToolContext.Extension)];
        Assert.Multiple(() =>
        {
            foreach ((ToolTabExtension extension, string formerName) in s_moved)
            {
                Assert.That(opened, Does.Contain(extension), formerName);
                Assert.That(NewToolTabDockable.IndexOfPin([formerName], extension), Is.Zero, formerName);
            }
        });

        static JsonArray? FirstToolList(JsonNode? node) => node switch
        {
            JsonObject obj when obj["tools"] is JsonArray found => found,
            JsonObject obj => obj.Select(pair => FirstToolList(pair.Value)).FirstOrDefault(found => found is not null),
            JsonArray array => array.Select(FirstToolList).FirstOrDefault(found => found is not null),
            _ => null
        };
    }
}
