using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Editor.Components.GraphEditorTab;
using Beutl.Editor.Components.PathEditorTab;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ContextCommandIdTests
{
    [AvaloniaTest]
    public void Older_keymaps_load_alongside_the_new_editor_shortcuts()
    {
        var json = JsonNode.Parse("""
            {
              "Windows": {
                "Beutl.Services.PrimitiveImpls.TimelineTabExtension.Paste": "Alt+P",
                "timeline.delete": [null, "F8"]
              },
              "OSX": {
                "timeline.paste": "Cmd+Shift+V"
              }
            }
            """)!.AsObject();
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(json, persist: false));
        manager.Register(TimelineTabExtension.Instance);
        manager.Register(GraphEditorTabExtension.Instance);
        manager.Register(PathEditorTabExtension.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(Gestures("timeline.paste", OSPlatform.Windows), Is.EqualTo(new[] { KeyGesture.Parse("Alt+P") }));
            Assert.That(Gestures("timeline.paste", OSPlatform.OSX), Is.EqualTo(new[] { KeyGesture.Parse("Cmd+Shift+V") }));
            Assert.That(Gestures("timeline.delete", OSPlatform.Windows), Is.EqualTo(new[] { null, KeyGesture.Parse("F8") }));
            Assert.That(Gestures("graphEditor.paste", OSPlatform.Windows), Does.Contain(KeyGesture.Parse("Ctrl+V")));
            Assert.That(Gestures("graphEditor.paste", OSPlatform.OSX), Does.Contain(KeyGesture.Parse("Cmd+V")));
            Assert.That(Gestures("pathEditor.selectAll", OSPlatform.OSX), Is.EqualTo(new[] { KeyGesture.Parse("Cmd+A") }));
        });

        IEnumerable<KeyGesture?> Gestures(string id, OSPlatform platform)
            => manager.GetDefinitions().Single(e => e.Definition.Id == id).KeyGestures
                .Where(gesture => gesture.Platform == platform).Select(gesture => gesture.KeyGesture);
    }

    [AvaloniaTest]
    public void Ids_are_unique_and_every_key_from_before_ids_moves_to_one_of_them()
    {
        string[] ids = [.. TestShell.MainViewModel.ContextCommandManager.GetDefinitions()
            .Select(entry => entry.Definition.Id)];

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.Not.Empty);
            Assert.That(ids, Is.All.Not.Empty);
            Assert.That(ids, Is.Unique);
            Assert.That(ContextCommandLegacyKeys.Ids.Values, Is.SubsetOf(ids));
            Assert.That(ContextCommandLegacyKeys.Removed.Intersect(ids), Is.Empty, "Dropping a live id loses its gestures.");
        });
    }
}
