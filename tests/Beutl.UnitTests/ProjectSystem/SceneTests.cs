using System.Text.Json.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneTests
{
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"beutl-scene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "elements"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_tempDirectory, recursive: true);
    }

    [Test]
    public void Windows_style_exclude_is_accepted_and_serialized_with_forward_slashes()
    {
        string scenePath = Path.Combine(_tempDirectory, "project.scene");
        string includedPath = Path.Combine(_tempDirectory, "elements", "included.belm");
        string excludedPath = Path.Combine(_tempDirectory, "elements", "excluded.belm");
        CoreSerializer.StoreToUri(new Element(), new Uri(includedPath));
        CoreSerializer.StoreToUri(new Element(), new Uri(excludedPath));

        var source = new Scene
        {
            Uri = new Uri(scenePath),
        };
        JsonObject json = CoreSerializer.SerializeToJsonObject(
            source,
            new CoreSerializerOptions { BaseUri = source.Uri });
        json["Elements"] = new JsonObject
        {
            ["Include"] = "**\\*.belm",
            ["Exclude"] = "elements\\excluded.belm",
        };
        json.JsonSave(scenePath);

        Scene restored = CoreSerializer.RestoreFromUri<Scene>(new Uri(scenePath));
        JsonObject roundTrip = CoreSerializer.SerializeToJsonObject(
            restored,
            new CoreSerializerOptions { BaseUri = restored.Uri });
        JsonObject elements = roundTrip["Elements"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(restored.Children.Select(x => Path.GetFileName(x.Uri!.LocalPath)),
                Is.EquivalentTo(new[] { "included.belm" }));
            Assert.That((string?)elements["Include"], Is.EqualTo("**/*.belm"));
            Assert.That((string?)elements["Exclude"], Is.EqualTo("elements/excluded.belm"));
        });
    }
}
