using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ReadDocumentTests
{
    [Test]
    public void Get_started_returns_low_context_entrypoints()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<GettingStartedResponse> result = tools.GetStarted();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.RecommendedCalls, Does.Contain("In live mode, call attach_active_editor before scene tools."));
            Assert.That(result.Value.CategoryAliases["visualEffect"], Is.EqualTo("FilterEffect"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("Server-Sent Events"));
        });
    }

    [Test]
    public void Read_document_returns_full_document_and_scoped_subtree()
    {
        var scene = new Scene(1920, 1080, "Scene");
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Scoped" } });
        element.Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var full = tools.ReadDocument();
        var scoped = tools.ReadDocument(element.Id.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(full.IsSuccess, Is.True);
            Assert.That(full.Value!.Document["Elements"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(scoped.IsSuccess, Is.True);
            Assert.That(scoped.Value!.Document["Id"]!.GetValue<string>(), Is.EqualTo(element.Id.ToString()));
            Assert.That(scoped.Value.SchemaVersion, Is.EqualTo(SchemaVersion.Current));
        });
    }

    [Test]
    public void Read_document_unknown_root_id_returns_stale_handle()
    {
        using var session = new AgentToolkitTestSession(new Scene());
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var result = tools.ReadDocument(Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }

    [Test]
    public void Read_document_summary_returns_compact_scene_shape()
    {
        var scene = new Scene(1280, 720, "Summary")
        {
            Duration = TimeSpan.FromSeconds(8)
        };
        var element = new Element
        {
            Name = "Title element",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 3
        };
        var text = new TextBlock
        {
            Name = "Title",
            Text = { CurrentValue = "Summary" },
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
            FilterEffect = { CurrentValue = new FilterEffectGroup { Children = { new Blur() } } }
        };
        text.Opacity.Animation = new KeyFrameAnimation<float>();
        element.AddObject(text);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        ToolResult<DocumentSummaryResponse> result = tools.ReadDocumentSummary();
        ElementSummary elementSummary = result.Value!.Elements.Single();
        ObjectSummary objectSummary = elementSummary.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Width, Is.EqualTo(1280));
            Assert.That(result.Value.Height, Is.EqualTo(720));
            Assert.That(result.Value.Duration, Is.EqualTo("00:00:08"));
            Assert.That(result.Value.ElementCount, Is.EqualTo(1));
            Assert.That(elementSummary.Name, Is.EqualTo("Title element"));
            Assert.That(elementSummary.Start, Is.EqualTo("00:00:01"));
            Assert.That(elementSummary.Length, Is.EqualTo("00:00:04"));
            Assert.That(objectSummary.Discriminator, Is.EqualTo("[Beutl.Engine]Beutl.Graphics.Shapes:TextBlock"));
            Assert.That(objectSummary.AnimatedProperties, Does.Contain(nameof(TextBlock.Opacity)));
            Assert.That(objectSummary.BrushProperties, Does.Contain(nameof(TextBlock.Fill)));
            Assert.That(objectSummary.EffectProperties, Does.Contain(nameof(TextBlock.FilterEffect)));
        });
    }
}
