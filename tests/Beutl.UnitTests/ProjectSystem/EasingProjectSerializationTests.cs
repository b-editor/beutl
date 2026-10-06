using System.Text.Json.Nodes;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

public class EasingProjectSerializationTests
{
    [SuppressResourceClassGeneration]
    public sealed class EasingElement : Beutl.ProjectSystem.Element
    {
        public static readonly CoreProperty<Easing> EasingProperty =
            ConfigureProperty<Easing, EasingElement>(nameof(Easing))
                .DefaultValue(new LinearEasing())
                .Register();

        public Easing Easing
        {
            get => GetValue(EasingProperty);
            set => SetValue(EasingProperty, value);
        }
    }

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "easing-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [TestCase(null)]
    [TestCase("$type")]
    [TestCase("@type")]
    public void Restore_ProjectWithElementEasing_RetainsElementAndSplineControlPoints(string? discriminatorKey)
    {
        (Uri projectUri, string elementPath) = CreateProject();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        var splineJson = new JsonObject { ["X1"] = 0.1f, ["Y1"] = -0.2f, ["X2"] = 0.3f, ["Y2"] = 1.4f };
        if (discriminatorKey != null)
            splineJson[discriminatorKey] = TypeFormat.ToString(typeof(SplineEasing));
        json["Easing"] = splineJson;
        File.WriteAllText(elementPath, json.ToJsonString());

        Project restored = CoreSerializer.RestoreFromUri<Project>(projectUri);
        var scene = (Beutl.ProjectSystem.Scene)restored.Items.Single();

        Assert.That(scene.Children.Single(), Is.TypeOf<EasingElement>());
        var element = (EasingElement)scene.Children.Single();
        Assert.That(element.Easing, Is.TypeOf<SplineEasing>());
        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.True);
            Assert.That(element.SuppressedStorageSource, Is.Null);
            Assert.That(element.Objects.Single(), Is.TypeOf<RectShape>());
            Assert.That(((SplineEasing)element.Easing).Y1, Is.EqualTo(-0.2f));
            Assert.That(((SplineEasing)element.Easing).Y2, Is.EqualTo(1.4f));
        });

        CoreSerializer.StoreToUri(restored, projectUri);
        Project reopened = CoreSerializer.RestoreFromUri<Project>(projectUri);
        var reloaded = (EasingElement)((Beutl.ProjectSystem.Scene)reopened.Items.Single()).Children.Single();
        Assert.That(((SplineEasing)reloaded.Easing).Y2, Is.EqualTo(1.4f));
    }

    [TestCase("{\"$type\":\"[Missing.Assembly]Missing:Easing\"}")]
    [TestCase("{\"$type\":\"[Beutl.Engine]Beutl.Animation.Easings:Easing\"}")]
    [TestCase("{\"X1\":\"invalid\"}")]
    public void Restore_ProjectWithInvalidElementEasing_PreservesOriginalSidecar(string easingJson)
    {
        (Uri projectUri, string elementPath) = CreateProject();
        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        json["Easing"] = JsonNode.Parse(easingJson);
        File.WriteAllText(elementPath, json.ToJsonString());
        byte[] original = File.ReadAllBytes(elementPath);

        Project restored = CoreSerializer.RestoreFromUri<Project>(projectUri);
        var element = ((Beutl.ProjectSystem.Scene)restored.Items.Single()).Children.Single();
        CoreSerializer.StoreToUri(restored, projectUri);

        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(element.Objects.Single(), Is.InstanceOf<IFallback>());
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(original));
        });
    }

    private (Uri ProjectUri, string ElementPath) CreateProject()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_root, "project.beutl")) };
        var scene = new Beutl.ProjectSystem.Scene(64, 64, "Scene")
        {
            Uri = new Uri(Path.Combine(_root, "scene.scene")),
        };
        var element = new EasingElement
        {
            Uri = new Uri(Path.Combine(_root, "element.belm")),
            Length = TimeSpan.FromSeconds(1),
        };
        element.AddObject(new RectShape());
        scene.Children.Add(element);
        project.Items.Add(scene);
        CoreSerializer.StoreToUri(project, project.Uri);
        return (project.Uri, element.Uri.LocalPath);
    }
}
