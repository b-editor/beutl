using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl.UnitTests.Core;

[TestFixture]
public sealed class StoreToUriLifetimeTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.WorkDirectory,
            "beutl-save-uri-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        string testRoot = Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.That(_directory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase), Is.True);
        Directory.Delete(_directory, recursive: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Serialization_failure_preserves_the_previous_uri_and_allows_retry(bool previouslySaved)
    {
        Uri? previousUri = previouslySaved ? CreateUri("previous.json") : null;
        Uri destination = CreateUri("next.json");
        var failure = new InvalidOperationException("Injected serialization failure.");
        var value = new TrackedSerializable { Uri = previousUri };
        string? previousBytes = SavePreviousValue(value);
        value.Failure = failure;
        File.WriteAllText(destination.LocalPath, "previous destination bytes");

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.StoreToUri(value, destination));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(value.Uri, Is.EqualTo(previousUri));
            Assert.That(value.UriDuringSerialization, Is.EqualTo(destination));
            Assert.That(value.BaseUriDuringSerialization, Is.EqualTo(destination));
            Assert.That(File.ReadAllText(destination.LocalPath), Is.EqualTo("previous destination bytes"));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            if (previousUri is not null)
                Assert.That(File.ReadAllText(previousUri.LocalPath), Is.EqualTo(previousBytes));
        });

        value.Failure = null;
        CoreSerializer.StoreToUri(value, destination);
        Assert.Multiple(() =>
        {
            Assert.That(value.Uri, Is.EqualTo(destination));
            Assert.That(File.ReadAllText(destination.LocalPath), Does.Contain("persisted value"));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            if (previousUri is not null)
                Assert.That(File.ReadAllText(previousUri.LocalPath), Is.EqualTo(previousBytes));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void File_replacement_failure_preserves_the_previous_uri(bool previouslySaved)
    {
        Uri? previousUri = previouslySaved ? CreateUri("previous.json") : null;
        Uri destination = CreateUri("occupied.json");
        Directory.CreateDirectory(destination.LocalPath);
        var value = new TrackedSerializable { Uri = previousUri };
        string? previousBytes = SavePreviousValue(value);

        Exception? actual = Assert.Catch(() => CoreSerializer.StoreToUri(value, destination));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(value.Uri, Is.EqualTo(previousUri));
            Assert.That(value.UriDuringSerialization, Is.EqualTo(destination));
            Assert.That(Directory.Exists(destination.LocalPath), Is.True);
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            if (previousUri is not null)
                Assert.That(File.ReadAllText(previousUri.LocalPath), Is.EqualTo(previousBytes));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Failed_project_save_preserves_its_uri_without_undoing_the_migration_gate(bool previouslySaved)
    {
        Uri? previousUri = previouslySaved ? CreateUri("previous.beutl") : null;
        Uri destination = CreateUri("next.beutl");
        var project = new Project { Uri = previousUri };
        string? previousBytes = null;
        if (previousUri is not null)
        {
            CoreSerializer.StoreToUri(project, previousUri);
            previousBytes = File.ReadAllText(previousUri.LocalPath);
        }

        var failure = new InvalidOperationException("Injected project item failure.");
        var item = new FailingProjectItem { Failure = failure };
        item.MergePersistedContentMigration("99.0.0");
        project.Items.Add(item);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            CoreSerializer.StoreToUri(project, destination));

        var gate = JsonNode.Parse(File.ReadAllText(destination.LocalPath))!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(project.Uri, Is.EqualTo(previousUri));
            Assert.That(project.MinAppVersion, Is.EqualTo("99.0.0"));
            Assert.That(gate["minAppVersion"]!.GetValue<string>(), Is.EqualTo("99.0.0"));
            Assert.That(gate.ContainsKey("items"), Is.False,
                "A failed save retains the compatibility gate without publishing the item graph.");
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
            if (previousUri is not null)
                Assert.That(File.ReadAllText(previousUri.LocalPath), Is.EqualTo(previousBytes));
        });
    }

    private Uri CreateUri(string name) => new(Path.Combine(_directory, name));

    private sealed class FailingProjectItem : ProjectItem
    {
        public required Exception Failure { get; init; }

        public override void Serialize(ICoreSerializationContext context) => throw Failure;
    }

    private static string? SavePreviousValue(TrackedSerializable value)
    {
        if (value.Uri is null) return null;
        CoreSerializer.StoreToUri(value, value.Uri);
        return File.ReadAllText(value.Uri.LocalPath);
    }

    private sealed class TrackedSerializable : CoreObject
    {
        public Exception? Failure { get; set; }

        public Uri? UriDuringSerialization { get; private set; }

        public Uri? BaseUriDuringSerialization { get; private set; }

        public override void Serialize(ICoreSerializationContext context)
        {
            UriDuringSerialization = Uri;
            BaseUriDuringSerialization = context.BaseUri;
            base.Serialize(context);
            context.SetValue("Value", "persisted value");
            if (Failure is not null) throw Failure;
        }
    }
}
