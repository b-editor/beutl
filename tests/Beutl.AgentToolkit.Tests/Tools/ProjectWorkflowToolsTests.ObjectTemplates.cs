using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed partial class ProjectWorkflowToolsTests
{
    [Test]
    public async Task Object_template_saves_a_nested_brush_and_replaces_a_property_as_one_undo_step()
    {
        string profile = TestWorkspace.CreateDirectory();
        using (var library = new ObjectTemplateService(SceneRenderServices.Empty, Path.Combine(profile, "templates")))
        {
            Assert.That(await library.AddFromInstanceAsync(new Element(), "Existing element"), Is.Not.Null);
        }

        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(profile);
        ListTemplatesResponse existing = Success(await Call<ListTemplatesResponse>(server.Client, "list_templates"));
        Assert.That(existing.Templates.Single().BaseType, Is.EqualTo(IdentityHelper.WriteDiscriminator(typeof(Element))));
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var sourceBrush = new SolidColorBrush(Colors.Red);
        var source = new RectShape { Fill = { Value = sourceBrush } };
        var target = new RectShape();
        Brush original = target.Fill.Value!;
        element.AddObject(source);
        element.AddObject(target);
        session.History.Commit("Seed shapes");
        session.History.Clear();

        ReadDocumentResponse read = Success(await Call<ReadDocumentResponse>(server.Client, "read_document",
            ("rootId", source.Id.Value.ToString())));
        string brushId = read.Document["Fill"]!["Id"]!.GetValue<string>();
        ObjectTemplateSummary saved = Success(await Call<ObjectTemplateSummary>(server.Client, "save_object_template",
            ("objectId", brushId), ("name", "Red fill")));

        Assert.Multiple(() =>
        {
            Assert.That(saved.Name, Is.EqualTo("Red fill"));
            Assert.That(saved.BaseType, Is.EqualTo(IdentityHelper.WriteDiscriminator(typeof(Brush))));
            Assert.That(saved.Type, Is.EqualTo(IdentityHelper.WriteDiscriminator(typeof(SolidColorBrush))));
            Assert.That(File.Exists(Path.Combine(profile, "templates", "Red fill.json")), Is.True);
            Assert.That(session.History.UndoCount, Is.Zero);
        });
        ListTemplatesResponse all = Success(await Call<ListTemplatesResponse>(server.Client, "list_templates"));
        Assert.That(all.Templates, Is.EquivalentTo(existing.Templates.Append(saved)));
        ListTemplatesResponse brushes = Success(await Call<ListTemplatesResponse>(server.Client, "list_templates", ("baseType", "Brush")));
        Assert.That(brushes.Templates, Is.EqualTo(new[] { saved }));
        brushes = Success(await Call<ListTemplatesResponse>(server.Client, "list_templates", ("baseType", saved.BaseType)));
        Assert.That(brushes.Templates, Is.EqualTo(new[] { saved }));

        ApplyObjectTemplateResponse applied = Success(await Call<ApplyObjectTemplateResponse>(server.Client, "apply_object_template",
            ("templateId", saved.TemplateId), ("objectId", target.Id.Value.ToString()), ("propertyName", nameof(target.Fill))));
        Assert.That(target.Fill.Value, Is.TypeOf<SolidColorBrush>());
        var appliedBrush = (SolidColorBrush)target.Fill.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(appliedBrush.Color.Value, Is.EqualTo(Colors.Red));
            Assert.That(applied.ObjectId, Is.EqualTo(appliedBrush.Id.Value.ToString()));
            Assert.That(applied.ObjectId, Is.Not.EqualTo(brushId));
            Assert.That(source.Fill.Value, Is.SameAs(sourceBrush));
            Assert.That(session.History.UndoCount, Is.EqualTo(1));
            Assert.That(session.IsDirty, Is.True);
        });

        HistoryStateResponse undo = Success(await Call<HistoryStateResponse>(server.Client, "undo"));
        Assert.That(undo.Applied.Single().Name, Is.EqualTo(CommandNames.ApplyTemplate));
        Assert.That(target.Fill.Value, Is.SameAs(original));
        Assert.That(session.History.UndoCount, Is.Zero);
    }

    [Test]
    public async Task Object_template_inserts_an_effect_group_with_fresh_ids_at_the_requested_index()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var source = new RectShape();
        var target = new RectShape();
        var sourceGroup = (FilterEffectGroup)source.FilterEffect.Value!;
        var targetGroup = (FilterEffectGroup)target.FilterEffect.Value!;
        var blur = new Blur { Sigma = { Value = new Size(7, 11) } };
        sourceGroup.Children.Add(blur);
        var before = new Blur();
        var after = new Blur();
        targetGroup.Children.Add(before);
        targetGroup.Children.Add(after);
        element.AddObject(source);
        element.AddObject(target);
        session.History.Commit("Seed effects");
        session.History.Clear();
        ObjectTemplateSummary saved = Success(await Call<ObjectTemplateSummary>(server.Client, "save_object_template",
            ("objectId", sourceGroup.Id.Value.ToString()), ("name", "Effect group")));

        ToolResult<ApplyObjectTemplateResponse> outOfRange = await Call<ApplyObjectTemplateResponse>(server.Client,
            "apply_object_template", ("templateId", saved.TemplateId), ("objectId", targetGroup.Id.Value.ToString()),
            ("propertyName", nameof(targetGroup.Children)), ("index", 3));
        Assert.That(outOfRange.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        Assert.That(outOfRange.Error?.Target, Is.EqualTo("index"));
        Assert.That(targetGroup.Children, Is.EqualTo(new FilterEffect[] { before, after }));
        Assert.That(session.History.UndoCount, Is.Zero);

        ApplyObjectTemplateResponse applied = Success(await Call<ApplyObjectTemplateResponse>(server.Client, "apply_object_template",
            ("templateId", saved.TemplateId), ("objectId", targetGroup.Id.Value.ToString()),
            ("propertyName", nameof(targetGroup.Children)), ("index", 1)));
        Assert.That(targetGroup.Children, Has.Count.EqualTo(3));
        var inserted = (FilterEffectGroup)targetGroup.Children[1];
        var insertedBlur = (Blur)inserted.Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(targetGroup.Children[0], Is.SameAs(before));
            Assert.That(targetGroup.Children[2], Is.SameAs(after));
            Assert.That(inserted.Id.Value, Is.Not.EqualTo(sourceGroup.Id.Value));
            Assert.That(applied.ObjectId, Is.EqualTo(inserted.Id.Value.ToString()));
            Assert.That(insertedBlur.Id.Value, Is.Not.EqualTo(blur.Id.Value));
            Assert.That(insertedBlur.Sigma.Value, Is.EqualTo(blur.Sigma.Value));
            Assert.That(session.History.UndoCount, Is.EqualTo(1));
        });

        Success(await Call<HistoryStateResponse>(server.Client, "undo"));
        Assert.That(targetGroup.Children, Is.EqualTo(new FilterEffect[] { before, after }));
        Assert.That(session.History.UndoCount, Is.Zero);
    }

    [Test]
    public async Task Object_template_rejects_an_incompatible_property_type()
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var shape = new RectShape { FilterEffect = { Value = new Blur() } };
        Brush original = shape.Fill.Value!;
        element.AddObject(shape);
        session.History.Commit("Seed shape");
        session.History.Clear();
        ObjectTemplateSummary saved = Success(await Call<ObjectTemplateSummary>(server.Client, "save_object_template",
            ("objectId", shape.FilterEffect.Value!.Id.Value.ToString()), ("name", "Blur")));

        ToolResult<ApplyObjectTemplateResponse> result = await Call<ApplyObjectTemplateResponse>(server.Client, "apply_object_template",
            ("templateId", saved.TemplateId), ("objectId", shape.Id.Value.ToString()), ("propertyName", nameof(shape.Fill)));
        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain(nameof(Blur)).And.Contain(nameof(Brush)).And.Contain(nameof(shape.Fill)));
            Assert.That(shape.Fill.Value, Is.SameAs(original));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Object_template_respects_element_and_layer_locks(bool layerLocked)
    {
        await using InProcessAgentToolkitServer server = await InProcessAgentToolkitServer.StartAsync(TestWorkspace.CreateDirectory());
        FileEditingSession session = await CreateProjectAsync(server);
        Element element = TestWorkspace.AddElement(session.Scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var source = new RectShape { Fill = { Value = new SolidColorBrush(Colors.Red) } };
        var target = new RectShape();
        Brush original = target.Fill.Value!;
        element.AddObject(source);
        element.AddObject(target);
        if (layerLocked)
            session.Scene.Layers.Add(new TimelineLayer { ZIndex = { Value = element.ZIndex.Value }, IsLocked = { Value = true } });
        else
            element.IsLocked.Value = true;
        session.History.Commit("Seed locked shape");
        session.History.Clear();
        ObjectTemplateSummary saved = Success(await Call<ObjectTemplateSummary>(server.Client, "save_object_template",
            ("objectId", source.Fill.Value!.Id.Value.ToString()), ("name", "Red fill")));

        ToolResult<ApplyObjectTemplateResponse> result = await Call<ApplyObjectTemplateResponse>(server.Client, "apply_object_template",
            ("templateId", saved.TemplateId), ("objectId", target.Id.Value.ToString()), ("propertyName", nameof(target.Fill)));
        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error?.Message, Does.Contain("locked"));
            Assert.That(target.Fill.Value, Is.SameAs(original));
            Assert.That(session.History.UndoCount, Is.Zero);
        });
    }
}
