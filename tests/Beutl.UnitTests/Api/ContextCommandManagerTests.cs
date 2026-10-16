using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Microsoft.Extensions.Logging;
using Moq;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class ContextCommandManagerTests
{
    // A command binding two platform-less gestures (like the timeline's Exit* commands binding
    // V and Escape) — the regression shape for multi-gesture remapping.
    private sealed class TestViewExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new ContextCommandDefinition("ExitTool", "Exit Tool", null,
            [
                new ContextCommandKeyGesture("V"),
                new ContextCommandKeyGesture("Escape"),
            ]) { Id = "test.exitTool" },
        ];
    }

    private const string CommandId = "test.exitTool";

    private static ContextCommandManager CreateManager(JsonObject json)
    {
        return new ContextCommandManager(new ContextCommandSettingsStore(json, persist: false));
    }

    private static KeyGesture?[] GesturesFor(ContextCommandEntry entry, OSPlatform platform)
    {
        return entry.KeyGestures
            .Where(g => g.Platform == platform)
            .Select(g => g.KeyGesture)
            .ToArray();
    }

    [Test]
    public async Task Input_event_boundary_consumes_faulted_command_tasks()
    {
        await Assert.DoesNotThrowAsync(() => ContextCommandManager.ExecuteSafelyAsync(
            () => Task.FromException(new InvalidOperationException("command failed")),
            Mock.Of<ILogger>()));
    }

    [Test]
    public async Task Input_event_boundary_consumes_canceled_command_tasks()
    {
        await Assert.DoesNotThrowAsync(() => ContextCommandManager.ExecuteSafelyAsync(
            () => Task.FromCanceled(new CancellationToken(canceled: true)),
            Mock.Of<ILogger>()));
    }

    [Test]
    public void ChangeKeyGesture_DefaultIndex_ChangesOnlyFirstSlot()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        manager.ChangeKeyGesture(entry, KeyGesture.Parse("B"), OSPlatform.Windows);

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new[] { KeyGesture.Parse("B"), KeyGesture.Parse("Escape") }));
    }

    [Test]
    public void ChangeKeyGesture_SecondSlot_KeepsFirstSlotAndOtherPlatforms()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        manager.ChangeKeyGesture(entry, KeyGesture.Parse("B"), OSPlatform.Windows, gestureIndex: 1);

        Assert.Multiple(() =>
        {
            Assert.That(GesturesFor(entry, OSPlatform.Windows),
                Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("B") }));
            Assert.That(GesturesFor(entry, OSPlatform.OSX),
                Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("Escape") }));
        });
    }

    [Test]
    public void ChangeKeyGesture_ClearSlot_KeepsOtherSlot()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        manager.ChangeKeyGesture(entry, null, OSPlatform.Windows);

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new KeyGesture?[] { null, KeyGesture.Parse("Escape") }));
    }

    [Test]
    public void ChangeKeyGesture_MissingPlatform_AppendsGesture()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        manager.ChangeKeyGesture(entry, KeyGesture.Parse("B"), OSPlatform.FreeBSD);

        Assert.That(GesturesFor(entry, OSPlatform.FreeBSD),
            Is.EqualTo(new[] { KeyGesture.Parse("B") }));
    }

    [Test]
    public void ChangeKeyGesture_NextFreeSlot_AppendsGesture()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        manager.ChangeKeyGesture(entry, KeyGesture.Parse("F2"), OSPlatform.Windows, gestureIndex: 2);

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("Escape"), KeyGesture.Parse("F2") }));
    }

    [Test]
    public void ChangeKeyGesture_IndexBeyondNextFreeSlot_Throws()
    {
        var manager = CreateManager([]);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => manager.ChangeKeyGesture(entry, KeyGesture.Parse("B"), OSPlatform.Windows, gestureIndex: 5));
        // The failed call must not have persisted or mutated anything.
        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("Escape") }));
    }

    [Test]
    public void ChangeKeyGesture_RemapRoundTrip_RestoresBothSlots()
    {
        var json = new JsonObject();
        var manager = CreateManager(json);
        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();
        manager.ChangeKeyGesture(entry, KeyGesture.Parse("B"), OSPlatform.Windows, gestureIndex: 1);

        // A fresh manager on the same persisted JSON = the next application launch.
        var restored = CreateManager(json);
        restored.Register(new TestViewExtension());
        ContextCommandEntry restoredEntry = restored.GetDefinitions<TestViewExtension>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(GesturesFor(restoredEntry, OSPlatform.Windows),
                Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("B") }));
            Assert.That(GesturesFor(restoredEntry, OSPlatform.OSX),
                Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("Escape") }));
        });
    }

    [Test]
    public void Restore_LegacyStringEntry_AppliesToFirstSlotOnly()
    {
        // Entries written before multi-gesture support persist a plain string per platform.
        var json = new JsonObject { ["Windows"] = new JsonObject { [CommandId] = "F1" } };
        var manager = CreateManager(json);

        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new[] { KeyGesture.Parse("F1"), KeyGesture.Parse("Escape") }));
    }

    [Test]
    public void Restore_ClearedSlotInArray_RestoresAsCleared()
    {
        var json = new JsonObject
        {
            ["Windows"] = new JsonObject { [CommandId] = new JsonArray("F1", null) }
        };
        var manager = CreateManager(json);

        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new KeyGesture?[] { KeyGesture.Parse("F1"), null }));
    }

    [Test]
    public void Restore_ListLongerThanDefaults_AppendsExtraSlots()
    {
        var json = new JsonObject
        {
            ["Windows"] = new JsonObject { [CommandId] = new JsonArray("V", "Escape", "F2") }
        };
        var manager = CreateManager(json);

        manager.Register(new TestViewExtension());
        ContextCommandEntry entry = manager.GetDefinitions<TestViewExtension>().Single();

        Assert.That(GesturesFor(entry, OSPlatform.Windows),
            Is.EqualTo(new[] { KeyGesture.Parse("V"), KeyGesture.Parse("Escape"), KeyGesture.Parse("F2") }));
    }

    [Test]
    public void A_keymap_from_before_command_ids_moves_to_the_ids_once()
    {
        const string LegacyDelete = "Beutl.Services.PrimitiveImpls.TimelineTabExtension.Delete";
        const string LegacyUndo = "Beutl.Services.PrimitiveImpls.MainViewExtension.Undo";
        var json = new JsonObject
        {
            ["Windows"] = new JsonObject
            {
                [LegacyDelete] = new JsonArray("Shift+Delete", null),
                ["Removed.Extension.Command"] = "F9",
            },
            ["OSX"] = new JsonObject
            {
                [LegacyUndo] = "Cmd+U",
                ["main.undo"] = new JsonArray("Cmd+Alt+Z"),
            },
        };

        var store = new ContextCommandSettingsStore(json, persist: false);

        Assert.Multiple(() =>
        {
            Assert.That(json["Windows"]!.AsObject().Select(pair => pair.Key),
                Is.EquivalentTo(new[] { "timeline.delete", "Removed.Extension.Command" }));
            Assert.That(store.Restore("timeline.delete").Single().Gestures,
                Is.EqualTo(new KeyGesture?[] { KeyGesture.Parse("Shift+Delete"), null }));
            Assert.That(json["OSX"]!.AsObject().Select(pair => pair.Key), Is.EquivalentTo(new[] { "main.undo" }));
            Assert.That(store.Restore("main.undo").Single().Gestures, Is.EqualTo(new[] { KeyGesture.Parse("Cmd+Alt+Z") }),
                "A gesture saved under the id wins.");
        });
    }
}
