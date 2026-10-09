using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
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
    [TestCase("shape", "rect", "#ffc9ba6f")]
    [TestCase("shape", null, "#ff9857cc")]
    [TestCase("shape", "ellipse", "#ffaac75a")]
    [TestCase("text", null, "#ffd77071")]
    [TestCase("group", null, "#ffa062d7")]
    public void Add_element_generates_the_same_accent_color_as_the_ui(
        string contentKind, string? shape, string expectedColor)
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var tools = new ElementTools(CreateManager(session));

        var result = tools.AddElement(0, 1, 0, contentKind, text: "Title", shape: shape);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        Color color = Color.Parse(expectedColor);
        Assert.Multiple(() =>
        {
            Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
            Assert.That(result.Value!.Document["Elements"]![0]![nameof(Element.AccentColor)]!.GetValue<string>(),
                Is.EqualTo(color.ToString()));
        });
    }

    [TestCase(false, false, "#ffc9ba6f")]
    [TestCase(true, false, "#ffc9ba6f")]
    [TestCase(false, true, "#ffa062d7")]
    [TestCase(true, true, "#ffa062d7")]
    public void Apply_edit_generates_an_accent_color_for_new_elements_when_omitted(
        bool usePatch, bool useGroup, string expectedColor)
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var tools = new EditTools(CreateManager(session));
        JsonObject element = CreateElementJson(useGroup ? new DrawableGroup() : new RectShape());
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
        var tools = new EditTools(CreateManager(session));
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
        var manager = CreateManager(session);
        var elements = new ElementTools(manager);
        Assert.That(elements.AddElement(0, 1, 0, "shape", shape: "rect").IsSuccess, Is.True);
        var edits = new EditTools(manager);
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
            Assert.That(scene.Children.Single().Start, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
        });
    }

    [Test]
    public void Generated_accent_color_survives_undo_redo_duplicate_and_split()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var tools = new ElementTools(CreateManager(session));
        Assert.That(tools.AddElement(0, 2, 0, "shape", shape: "rect").IsSuccess, Is.True);
        Color color = Color.Parse("#ffc9ba6f");
        string elementId = scene.Children.Single().Id.ToString();

        Assert.That(session.History.Undo(), Is.True);
        Assert.That(scene.Children, Is.Empty);
        Assert.That(session.History.Redo(), Is.True);
        Assert.That(scene.Children.Single().AccentColor, Is.EqualTo(color));
        Assert.That(tools.DuplicateElement(elementId, startSeconds: 3).IsSuccess, Is.True);
        Assert.That(tools.SplitElement(elementId, 1).IsSuccess, Is.True);
        Assert.That(scene.Children.Select(element => element.AccentColor), Is.All.EqualTo(color));
    }

    private static AgentSessionManager CreateManager(IEditingSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        return manager;
    }

    private static JsonObject CreateElementJson(EngineObject content)
    {
        var element = new Element { Length = TimeSpan.FromSeconds(1) };
        element.AddObject(content);
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        CollectionReconciler.RemoveIds(json);
        return json;
    }
}
