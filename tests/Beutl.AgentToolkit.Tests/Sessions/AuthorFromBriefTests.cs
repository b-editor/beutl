using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class AuthorFromBriefTests
{
    [Test]
    public void Create_apply_save_reload_preserves_authored_scene_and_elements()
    {
        string root = TestWorkspace.CreateDirectory();
        string projectPath = Path.Combine(root, "promo.bep");
        using var source = new FileSessionSource();
        FileEditingSession session = TestWorkspace.CreateFileSession(source, new ProjectCreateOptions(
            projectPath,
            1920,
            1080,
            30,
            TimeSpan.FromSeconds(10),
            Name: "promo"));

        var element = new Element
        {
            Start = MediaTime.FromSeconds(2),
            Length = MediaTime.FromSeconds(3),
            ZIndex = 5
        };
        element.AddObject(new TextBlock { Text = { Value = "Launch" } });

        JsonObject desired = session.Documents.Read(session.Scene);
        JsonObject elementJson = CoreSerializer.SerializeToJsonObject(element);
        elementJson.Remove(nameof(CoreObject.Id));
        foreach (JsonObject obj in ((JsonArray)elementJson["Objects"]!).OfType<JsonObject>())
        {
            obj.Remove(nameof(CoreObject.Id));
        }

        ((JsonArray)desired["Elements"]!).Add(elementJson);

        new Reconciler().Apply(session, desired);
        session.Save(skipConflictCheck: true);

        Project reloaded = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Scene scene = reloaded.Items.OfType<Scene>().Single();
        Element reloadedElement = scene.Children.Single();
        var text = (TextBlock)reloadedElement.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameSize.Width, Is.EqualTo(1920));
            Assert.That(scene.FrameSize.Height, Is.EqualTo(1080));
            Assert.That(scene.Duration, Is.EqualTo(MediaTime.FromSeconds(10)));
            Assert.That(reloadedElement.Start, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(reloadedElement.Length, Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(reloadedElement.ZIndex, Is.EqualTo(5));
            Assert.That(text.Text.Value, Is.EqualTo("Launch"));
        });
    }

    [Test]
    public async Task Add_scene_round_trips_through_project_save()
    {
        string root = TestWorkspace.CreateDirectory();
        string projectPath = Path.Combine(root, "multi.bep");
        using var source = new FileSessionSource();
        FileEditingSession session = TestWorkspace.CreateFileSession(source, new ProjectCreateOptions(
            projectPath,
            1280,
            720,
            24,
            TimeSpan.FromSeconds(5),
            Name: "multi"));

        await new FileProjectSessionGateway(source, new AgentSessionManager()).AddSceneAsync(session, new SceneCreateOptions(
            640,
            360,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(4),
            "second"));

        session.Save(skipConflictCheck: true);

        Project reloaded = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Scene[] scenes = reloaded.Items.OfType<Scene>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(scenes, Has.Length.EqualTo(2));
            Assert.That(scenes[1].FrameSize.Width, Is.EqualTo(640));
            Assert.That(scenes[1].Start, Is.EqualTo(MediaTime.FromSeconds(5)));
            Assert.That(scenes[1].Duration, Is.EqualTo(MediaTime.FromSeconds(4)));
        });
    }
}
