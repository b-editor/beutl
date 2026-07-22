using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Issue #2109: a Sound nested as a property value must inherit the element's TimeRange,
// or audio composition throws ("Duration must be positive") at render time.
[TestFixture]
public sealed class NestedSoundTimeRangeTests
{
    [Test]
    public void Nested_sound_applied_via_apply_edit_receives_element_time_range()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(6)));
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var tools = new EditTools(manager);

        JsonObject addElement = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                ["Name"] = "viz",
                ["Start"] = "00:00:00",
                ["Length"] = "00:00:05",
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(AudioWaveformDrawable)),
                    ["Name"] = "waveform"
                })
            })
        };
        ToolResult<ApplyEditResponse> first = tools.ApplyEdit(patch: addElement, schemaVersion: SchemaVersion.Current);
        Assert.That(first.IsSuccess, Is.True, first.Error?.Message);

        Element element = session.Scene.Children.Single();
        var drawable = (AudioWaveformDrawable)element.Objects.Single();

        JsonObject setSource = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = element.Id.ToString(),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = drawable.Id.ToString(),
                    ["Source"] = new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SourceSound))
                    }
                })
            })
        };
        ToolResult<ApplyEditResponse> second = tools.ApplyEdit(patch: setSource, schemaVersion: SchemaVersion.Current);
        Assert.That(second.IsSuccess, Is.True, second.Error?.Message);

        Sound? sound = drawable.Source.CurrentValue;
        Assert.That(sound, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(sound!.TimeRange, Is.EqualTo(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(5))));
            Assert.That(((IHierarchical)sound).HierarchicalRoot, Is.Not.Null);
        });
    }

    [Test]
    public void Nested_sound_inserted_together_with_its_drawable_receives_element_time_range()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(6)));
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var tools = new EditTools(manager);

        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                ["Name"] = "viz",
                ["Start"] = "00:00:01",
                ["Length"] = "00:00:04",
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = IdentityHelper.WriteDiscriminator(typeof(AudioSpectrumDrawable)),
                    ["Name"] = "spectrum",
                    ["Source"] = new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SourceSound))
                    }
                })
            })
        };
        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);

        var drawable = (AudioSpectrumDrawable)session.Scene.Children.Single().Objects.Single();
        Sound? sound = drawable.Source.CurrentValue;
        Assert.That(sound, Is.Not.Null);
        Assert.That(sound!.TimeRange, Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4))));
    }

    [Test]
    public void Apply_edit_keeps_existing_objects_attached_to_the_hierarchy()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var source = new FileSessionSource();
        FileEditingSession session = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "demo.bep"), 640, 360, 30, TimeSpan.FromSeconds(6)));
        var element = new Element
        {
            Name = "viz",
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(session.Scene.Uri!.LocalPath)!, "element.belm"))
        };
        var drawable = new AudioWaveformDrawable { Name = "waveform" };
        element.AddObject(drawable);
        session.Scene.Children.Add(element);

        JsonObject desired = session.Documents.Read(session.Root);
        session.Documents.Write(session.Root, desired);

        Assert.Multiple(() =>
        {
            Assert.That(((IHierarchical)element).HierarchicalParent, Is.SameAs(session.Scene));
            Assert.That(((IHierarchical)drawable).HierarchicalParent, Is.SameAs(element));
            Assert.That(((IHierarchical)drawable).HierarchicalRoot, Is.Not.Null);
        });
    }
}
