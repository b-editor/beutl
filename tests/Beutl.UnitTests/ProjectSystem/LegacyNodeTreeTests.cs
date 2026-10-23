using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

// Beutl 1.x could draw an element through a node tree ("UseNode" on, nodes in "NodeTree") instead of
// its operators. 2.x cannot read those nodes, so opening such a project must not lose the tree: the
// element keeps its original file until the user removes the tree, and the project says so on open.
[TestFixture]
public class LegacyNodeTreeTests
{
    private const string ElementName = "Node tree clip";
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl-legacy-node-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Saving_a_1x_project_keeps_the_file_of_an_element_drawn_by_its_node_tree()
    {
        (string projectPath, string elementPath) = WriteLegacyProject(LegacyElement());
        byte[] original = File.ReadAllBytes(elementPath);

        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Element element = project.Items.OfType<Scene>().Single().Children.Single();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(original),
                "A save must not rewrite the element without its node tree.");
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
            Assert.That(element.Objects, Has.Count.EqualTo(1));
            Assert.That(ElementMigration.IsUnmigratedNodeTree(element.Objects[0]), Is.True);
            Assert.That(((IFallback)element.Objects[0]).Reason, Is.EqualTo(FallbackReason.TypeNotFound));
            Assert.That(
                JsonNode.DeepEquals(((IFallback)element.Objects[0]).Json!["Nodes"], LegacyElement()["NodeTree"]!["Nodes"]),
                Is.True,
                "The object that stands in for the tree holds its nodes.");
        });
    }

    [Test]
    public void The_operators_of_an_element_drawn_by_its_node_tree_follow_the_tree()
    {
        JsonObject json = LegacyElement();
        json["Operation"]!["Children"]!.AsArray().Add(new JsonObject
        {
            ["$type"] = "[Beutl.Operators].Source:RectOperator",
            ["Value"] = new JsonObject { ["Width"] = 100, ["Height"] = 100 },
        });
        (string projectPath, _) = WriteLegacyProject(json);

        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Element element = project.Items.OfType<Scene>().Single().Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(element.Objects, Has.Count.EqualTo(2));
            Assert.That(ElementMigration.IsUnmigratedNodeTree(element.Objects[0]), Is.True);
            Assert.That(element.Objects[1], Is.TypeOf<Beutl.Graphics.Shapes.RectShape>());
        });
    }

    // 1.x drew the operators of these elements, so they migrate as before.
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void An_element_that_did_not_draw_through_its_node_tree_migrates_as_before(bool useNode, bool hasNodes)
    {
        JsonObject json = LegacyElement();
        json["UseNode"] = useNode;
        if (!hasNodes) json["NodeTree"]!["Nodes"] = new JsonArray();
        (string projectPath, string elementPath) = WriteLegacyProject(json);

        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Element element = project.Items.OfType<Scene>().Single().Children.Single();
        CoreSerializer.StoreToUri(project, project.Uri!);

        Assert.Multiple(() =>
        {
            Assert.That(element.Objects, Is.Empty);
            Assert.That(element.SuppressedStorageSource, Is.Null);
            Assert.That(JsonNode.Parse(File.ReadAllText(elementPath))![nameof(Element.Objects)], Is.Not.Null);
            Assert.That(UnmigratedNodeTreeNotice.Create(project), Is.Null);
        });
    }

    [Test]
    public void Removing_the_node_tree_lets_the_element_save_again()
    {
        (string projectPath, string elementPath) = WriteLegacyProject(LegacyElement());
        Project project = CoreSerializer.RestoreFromUri<Project>(new Uri(projectPath));
        Element element = project.Items.OfType<Scene>().Single().Children.Single();
        using var harness = new HistoryHarness(element);

        bool removed = new ElementObjectService(harness.History).Remove(element, element.Objects[0]);
        CoreSerializer.StoreToUri(project, project.Uri!);

        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(element.SuppressedStorageSource, Is.Null);
            Assert.That(saved[nameof(Element.Objects)]!.AsArray(), Is.Empty);
            Assert.That(saved.ContainsKey("NodeTree"), Is.False);
        });
    }

    [Test]
    public void The_notice_names_each_element_whose_node_tree_was_not_migrated()
    {
        var project = new Project();
        var scene = new Scene(1920, 1080, "Scene1");
        project.Items.Add(scene);
        for (int i = 0; i < 6; i++)
        {
            JsonObject json = LegacyElement();
            json["Id"] = Guid.NewGuid().ToString();
            json["Name"] = i == 0 ? string.Empty : $"Clip {i}";
            json["Start"] = TimeSpan.FromSeconds(i).ToString();
            var element = new Element { Uri = new Uri(Path.Combine(_root, $"unnamed-{i}.belm")) };
            CoreSerializer.PopulateFromJsonObject(element, json);
            scene.Children.Add(element);
        }

        scene.Children.Add(new Element { Name = { Value = "2.x clip" } });

        Notification? notice = UnmigratedNodeTreeNotice.Create(project);

        Assert.That(notice, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(notice!.Title, Is.EqualTo(MessageStrings.UnmigratedNodeTreeTitle));
            Assert.That(notice.Type, Is.EqualTo(NotificationType.Warning));
            Assert.That(notice.Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
            Assert.That(notice.Message, Does.Contain("unnamed-0, Clip 1, Clip 2, Clip 3, Clip 4, …"));
            Assert.That(notice.Message, Does.Not.Contain("Clip 5"));
            Assert.That(notice.Message, Does.Not.Contain("2.x clip"));
        });
    }

    // Writes a project, a scene and one element the way Beutl 1.1.0 saved them.
    private (string ProjectPath, string ElementPath) WriteLegacyProject(JsonObject element)
    {
        string sceneDirectory = Path.Combine(_root, "Scene1");
        Directory.CreateDirectory(sceneDirectory);
        string projectPath = Path.Combine(_root, "Legacy.bep");
        string elementPath = Path.Combine(sceneDirectory, "dg5k1o0w.belm");
        File.WriteAllText(projectPath, """
            {
              "Id": "3f0d6c52-8a0e-4c1e-9b5e-6a51d7c0f2b4",
              "Name": "",
              "appVersion": "1.1.0",
              "minAppVersion": "1.0.0-preview.9",
              "items": [
                "Scene1/Scene1.scene"
              ],
              "variables": {
                "framerate": "30",
                "samplerate": "44100"
              }
            }
            """);
        File.WriteAllText(Path.Combine(sceneDirectory, "Scene1.scene"), """
            {
              "Id": "a7f3e1c9-2b4d-4f6a-8c0e-1d3b5f7a9c2e",
              "Name": "Scene1",
              "FrameSize": "1920, 1080",
              "Start": "00:00:00",
              "Duration": "00:00:10",
              "Width": 1920,
              "Height": 1080,
              "Elements": {
                "Include": "dg5k1o0w.belm"
              }
            }
            """);
        File.WriteAllText(elementPath, element.ToJsonString(JsonHelper.SerializerOptions));
        return (projectPath, elementPath);
    }

    // An element whose UseNode toggle is on and whose node tree draws a rectangle: RectGeometryNode
    // into GeometryShapeNode into LayerOutputNode. Its operator list is empty, as 1.x always wrote it.
    private static JsonObject LegacyElement() => JsonNode.Parse($$"""
        {
          "Id": "2b0c6f8e-7c1d-4b5e-9a39-0d1c55b0a7e1",
          "Name": "{{ElementName}}",
          "Start": "00:00:00",
          "Length": "00:00:05",
          "ZIndex": 0,
          "AccentColor": "#ff2196f3",
          "IsEnabled": true,
          "UseNode": true,
          "Operation": {
            "Id": "5f0e3b8a-1d2c-4e6f-8a9b-0c1d2e3f4a5b",
            "Name": "",
            "Children": []
          },
          "NodeTree": {
            "Id": "9d8c7b6a-5e4f-4a3b-9c2d-1e0f9a8b7c6d",
            "Name": "",
            "Nodes": [
              {
                "Id": "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
                "Name": "",
                "Position": "120,80",
                "Items": [
                  {
                    "Id": "c1a2b3c4-d5e6-4f70-8192-a3b4c5d6e7f8",
                    "Name": "Geometry",
                    "LocalId": 0,
                    "connection-inputs": [
                      "d2b3c4d5-e6f7-4081-9a2b-b4c5d6e7f809"
                    ],
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:OutputSocket<[Beutl.Engine]Beutl.Media:RectGeometry>"
                  },
                  {
                    "Id": "e3c4d5e6-f708-4192-ab3c-c5d6e7f8091a",
                    "Name": "Width",
                    "LocalId": 1,
                    "Property": "Width",
                    "Target": "[Beutl.Engine]Beutl.Media:RectGeometry",
                    "Setter": 640,
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:DefaultInputSocket<[System.Private.CoreLib]System:Single>"
                  },
                  {
                    "Id": "f4d5e6f7-0819-42a3-bc4d-d6e7f8091a2b",
                    "Name": "Height",
                    "LocalId": 2,
                    "Property": "Height",
                    "Target": "[Beutl.Engine]Beutl.Media:RectGeometry",
                    "Setter": 360,
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:DefaultInputSocket<[System.Private.CoreLib]System:Single>"
                  }
                ],
                "$type": "[Beutl.ProjectSystem]Beutl.NodeTree.Nodes.Geometry:RectGeometryNode"
              },
              {
                "Id": "1b2c3d4e-5f60-4718-9a0b-1c2d3e4f5a6b",
                "Name": "",
                "Position": "360,80",
                "Items": [
                  {
                    "Id": "05e6f708-192a-43b4-8d5e-e7f8091a2b3c",
                    "Name": "GeometryShape",
                    "LocalId": 0,
                    "connection-inputs": [
                      "16f70819-2a3b-44c5-9e6f-f8091a2b3c4d"
                    ],
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:OutputSocket<[Beutl.Engine]Beutl.Graphics.Shapes:GeometryShape>"
                  },
                  {
                    "Id": "d2b3c4d5-e6f7-4081-9a2b-b4c5d6e7f809",
                    "Name": "Data",
                    "LocalId": 1,
                    "connection-output": "c1a2b3c4-d5e6-4f70-8192-a3b4c5d6e7f8",
                    "Property": "Data",
                    "Target": "[Beutl.Engine]Beutl.Graphics.Shapes:GeometryShape",
                    "Setter": null,
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:DefaultInputSocket<[Beutl.Engine]Beutl.Media:Geometry>"
                  }
                ],
                "$type": "[Beutl.ProjectSystem]Beutl.NodeTree.Nodes:GeometryShapeNode"
              },
              {
                "Id": "2c3d4e5f-6071-4829-8b1c-2d3e4f5a6b7c",
                "Name": "",
                "Position": "600,80",
                "Items": [
                  {
                    "Id": "16f70819-2a3b-44c5-9e6f-f8091a2b3c4d",
                    "Name": "Drawable",
                    "LocalId": 0,
                    "connection-output": "05e6f708-192a-43b4-8d5e-e7f8091a2b3c",
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:InputSocket<[Beutl.Engine]Beutl.Graphics:Drawable>"
                  },
                  {
                    "Id": "2708192a-3b4c-45d6-af70-091a2b3c4d5e",
                    "Name": "BlendMode",
                    "LocalId": 1,
                    "Property": "BlendMode",
                    "Target": "[Beutl.Engine]Beutl.Graphics:Drawable",
                    "Setter": "SrcOver",
                    "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:DefaultInputSocket<[Beutl.Engine]Beutl.Graphics:BlendMode>"
                  }
                ],
                "$type": "[Beutl.ProjectSystem]Beutl.NodeTree.Nodes:LayerOutputNode"
              }
            ],
            "$type": "[Beutl.ProjectSystem]Beutl.NodeTree:ElementNodeTreeModel"
          }
        }
        """)!.AsObject();
}
