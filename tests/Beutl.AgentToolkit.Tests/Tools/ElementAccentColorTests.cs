using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ElementAccentColorTests
{
    // These are the existing UI-generated colors for the corresponding content types.
    [TestCase(false, typeof(RectShape), "#ffc9ba6f")]
    [TestCase(true, typeof(RectShape), "#ffc9ba6f")]
    [TestCase(true, typeof(RoundedRectShape), "#ff9857cc")]
    [TestCase(true, typeof(EllipseShape), "#ffaac75a")]
    [TestCase(true, typeof(TextBlock), "#ffd77071")]
    [TestCase(false, typeof(DrawableGroup), "#ffa062d7")]
    [TestCase(true, typeof(DrawableGroup), "#ffa062d7")]
    public void Apply_edit_generates_an_accent_color_for_new_elements_when_omitted(
        bool usePatch, Type contentType, string expectedColor)
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject element = CreateElementJson((EngineObject)Activator.CreateInstance(contentType)!);
        element.Remove(nameof(Element.AccentColor));
        JsonObject document = usePatch ? new JsonObject() : session.Documents.Read(scene);
        document["Elements"] = new JsonArray(element);

        var result = tools.ApplyEdit(
            desired: usePatch ? null : document,
            patch: usePatch ? document : null,
            schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(Color.Parse(expectedColor)));
    }

    [TestCase("#ff008080")]
    [TestCase("#ffff7f50")]
    public void Apply_edit_preserves_an_explicit_accent_color_on_new_elements(string colorText)
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject element = CreateElementJson(new RectShape());
        Color color = Color.Parse(colorText);
        element[nameof(Element.AccentColor)] = CoreSerializer.SerializeToJsonNode(color);

        var result = tools.ApplyEdit(
            patch: new JsonObject { ["Elements"] = new JsonArray(element) },
            schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
    }

    [Test]
    public void Apply_edit_does_not_regenerate_an_existing_elements_omitted_accent_color()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        EditTools edits = session.CreateEditTools();
        JsonObject added = CreateElementJson(new RectShape());
        Assert.That(edits.ApplyEdit(
            patch: new JsonObject { ["Elements"] = new JsonArray(added) },
            schemaVersion: SchemaVersion.Current).IsSuccess, Is.True);
        Color color = Colors.Coral;
        JsonObject setColorPatch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = scene.Children.Single().Id.ToString(),
                [nameof(Element.AccentColor)] = CoreSerializer.SerializeToJsonNode(color)
            })
        };
        Assert.That(edits.ApplyEdit(patch: setColorPatch, schemaVersion: SchemaVersion.Current).IsSuccess, Is.True);
        JsonObject desired = session.Documents.Read(scene);
        desired["Elements"]![0]!.AsObject().Remove(nameof(Element.AccentColor));
        desired["Elements"]![0]![nameof(Element.Start)] = TimeSpan.FromSeconds(2).ToString("c");

        var result = edits.ApplyEdit(desired: desired, schemaVersion: SchemaVersion.Current);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children.Single().Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
        });
    }

    [Test]
    public void Generated_accent_color_survives_undo_redo()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject element = CreateElementJson(new RectShape());
        element.Remove(nameof(Element.AccentColor));
        Assert.That(tools.ApplyEdit(
            patch: new JsonObject { ["Elements"] = new JsonArray(element) },
            schemaVersion: SchemaVersion.Current).IsSuccess, Is.True);
        Color color = Color.Parse("#ffc9ba6f");

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(scene.Children, Is.Empty);
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
    }

    private static JsonObject CreateElementJson(EngineObject content)
    {
        var element = new Element { Length = MediaTime.FromSeconds(1) };
        element.AddObject(content);
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        CollectionReconciler.RemoveIds(json);
        return json;
    }
}
