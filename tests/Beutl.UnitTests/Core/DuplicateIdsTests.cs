using Beutl.Animation;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Core;

// Ids are unique per project: a duplicate is counted and logged once, and keeps its Id (b-editor/beutl#2885).
[TestFixture]
[NonParallelizable]
public class DuplicateIdsTests
{
    private string _root = null!;
    private CapturingLoggerProvider _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "duplicate-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _logs = new CapturingLoggerProvider(category => category == typeof(DuplicateIds).FullName);
        Log.LoggerFactory.AddProvider(_logs);
    }

    [TearDown]
    public void TearDown()
    {
        _logs.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Count_CountsEachObjectWhoseIdAnObjectBeforeItHas()
    {
        var element = new Element();
        var first = new RectShape();
        var second = new RectShape { Id = first.Id };
        var width = new KeyFrameAnimation<float>();
        var key = new KeyFrame<float> { KeyTime = MediaTime.Zero };
        width.KeyFrames.Add(key);
        width.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.FromSeconds(1), Id = key.Id });
        second.Width.SetKeyFrameAnimation(width);
        element.AddObject(first);
        element.AddObject(second);
        element.AddObject(new PortalObject { Id = Guid.Empty });
        element.AddObject(new PortalObject { Id = Guid.Empty });
        var shared = new PortalObject();
        var holder = new SharedHolder { First = { Value = shared }, Second = { Value = shared } };

        (int count, Type[] types) = DuplicateIds.Count(element);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2), "objects without an Id are not counted");
            Assert.That(types, Is.EqualTo(new[] { typeof(RectShape), typeof(KeyFrame<float>) }));
            Assert.That(DuplicateIds.Count(holder).Count, Is.Zero, "one object reached twice is not a duplicate");
        });
    }

    [Test]
    public void ReadingAProject_LogsItsDuplicateIdsOnce_AndKeepsThem()
    {
        Project project = CreateProject(out Element element);
        CoreSerializer.StoreToUri(project, project.Uri!);
        string elementPath = element.Uri!.LocalPath;
        // A file copied beside the original, as a user can do outside Beutl.
        File.Copy(elementPath, Path.Combine(Path.GetDirectoryName(elementPath)!, "copy.belm"));

        Project loaded = CoreSerializer.RestoreFromUri<Project>(project.Uri!);
        CoreSerializer.RestoreFromUri<Project>(project.Uri!);

        Element[] elements = [.. ((Scene)loaded.Items.Single()).Children];
        Assert.Multiple(() =>
        {
            Assert.That(elements, Has.Length.EqualTo(2));
            Assert.That(elements[1].Id, Is.EqualTo(elements[0].Id), "a duplicate is not given a new Id");
            Assert.That(elements[1].Objects[0].Id, Is.EqualTo(elements[0].Objects[0].Id));
            Assert.That(_logs.Messages, Has.Count.EqualTo(1), "the same duplicates are reported once");
            Assert.That(_logs.Messages.Single(), Does.Contain("on load: 2 object(s)")
                .And.Contain(nameof(Element)).And.Contain(nameof(PortalObject)));
        });
    }

    [Test]
    public void SavingAProjectWithItsFiles_LogsItsDuplicateIds_AndStoringTheProjectFileAloneDoesNot()
    {
        Project project = CreateProject(out Element element);
        var scene = (Scene)project.Items.Single();
        scene.Children.Add(new Element
        {
            Id = element.Id,
            Start = MediaTime.FromSeconds(1),
            Length = MediaTime.FromSeconds(1),
            Uri = new Uri(Path.Combine(_root, "scene", "second.belm")),
        });

        // An auto-save stores the project file this way after every edit.
        CoreSerializer.StoreToUri(project, project.Uri!, CoreSerializationMode.Write);
        Assert.That(_logs.Messages, Is.Empty);

        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.That(_logs.Messages.Single(), Does.Contain("on save: 1 object(s)"));
    }

    [Test]
    public void AProjectWithoutDuplicates_LogsNothing()
    {
        Project project = CreateProject(out _);
        CoreSerializer.StoreToUri(project, project.Uri!);

        CoreSerializer.RestoreFromUri<Project>(project.Uri!);

        Assert.That(_logs.Messages, Is.Empty);
    }

    private Project CreateProject(out Element element)
    {
        var project = new Project { Uri = new Uri(Path.Combine(_root, "project.bep")) };
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(Path.Combine(_root, "scene", "scene.scene")) };
        element = new Element { Length = MediaTime.FromSeconds(1), Uri = new Uri(Path.Combine(_root, "scene", "element.belm")) };
        element.AddObject(new PortalObject());
        scene.Children.Add(element);
        project.Items.Add(scene);
        return project;
    }

    private sealed class SharedHolder : CoreObject
    {
        public SharedHolder() => ScanProperties<SharedHolder>();

        public IProperty<PortalObject?> First { get; } = new ValueOnlyProperty<PortalObject?>(null);

        public IProperty<PortalObject?> Second { get; } = new ValueOnlyProperty<PortalObject?>(null);
    }
}
