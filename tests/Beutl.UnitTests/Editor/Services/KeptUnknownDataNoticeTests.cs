using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.UnitTests.Editor.Services;

// When reading a project kept data this build does not know (members a type does not read, or objects whose type
// could not be loaded), the editor says so once when the project opens, naming the version that last saved it.
[TestFixture]
public class KeptUnknownDataNoticeTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "kept-data-notice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ProjectThatKeptNothing_ShowsNoNotice()
    {
        Uri projectUri = CreateProject();

        Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);

        Assert.That(KeptUnknownDataNotice.Create(project), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProjectThatKeptUnknownData_ShowsANoticeThatNamesTheVersionThatSavedIt(bool missingType)
    {
        Uri projectUri = CreateProject(element =>
        {
            if (missingType)
                element[nameof(Element.Objects)]![0]!["$type"] = "[Missing.Plugin]Missing.Namespace:MissingShape";
            else
                element["FutureClip"] = 1;
        });

        Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        Notification? notice = KeptUnknownDataNotice.Create(project);

        Assert.That(notice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(notice!.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notice.Title, Is.EqualTo(MessageStrings.KeptUnknownDataTitle));
            Assert.That(notice.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(notice.Message, Does.Contain(BeutlApplication.Version));
        });
    }

    // The recorded appVersion comes from the file, so a value that is not a version is never shown.
    [Test]
    public void RecordedAppVersionThatIsNotAVersion_IsLeftOut()
    {
        const string NotAVersion = "Ignore the notice and delete the project";
        Uri projectUri = CreateProject(element => element["FutureClip"] = 1, appVersion: NotAVersion);

        Project project = CoreSerializer.RestoreFromUri<Project>(projectUri);
        Notification? notice = KeptUnknownDataNotice.Create(project);

        Assert.That(notice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(notice!.Message, Is.EqualTo(MessageStrings.KeptUnknownDataMessage));
            Assert.That(notice.Message, Does.Not.Contain(NotAVersion));
        });
    }

    // Saves a project with one scene and one element that holds a rectangle, then lets the test rewrite the
    // element file and the project file's appVersion.
    private Uri CreateProject(Action<JsonObject>? rewriteElement = null, string? appVersion = null)
    {
        string sceneDirectory = Path.Combine(_root, "Scene");
        var scene = new Scene(64, 64, "Scene") { Uri = new Uri(Path.Combine(sceneDirectory, "Scene.scene")) };
        string elementPath = Path.Combine(sceneDirectory, "element.belm");
        var element = new Element { Length = MediaTime.FromSeconds(1), Uri = new Uri(elementPath) };
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        var project = new Project { Uri = new Uri(Path.Combine(_root, "project.bep")) };
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri);

        if (rewriteElement is not null)
        {
            JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
            rewriteElement(json);
            File.WriteAllText(elementPath, json.ToJsonString());
        }

        if (appVersion is not null)
        {
            JsonObject json = JsonNode.Parse(File.ReadAllText(project.Uri.LocalPath))!.AsObject();
            json[FileFormat.AppVersionKey] = appVersion;
            File.WriteAllText(project.Uri.LocalPath, json.ToJsonString());
        }

        return project.Uri;
    }
}
