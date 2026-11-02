using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// An edit reads the whole document, so it costs at least what the scene holds. These check that it costs no more.
// They count allocations and list accesses rather than time, which depends on the machine.
public sealed class ApplyEditScalingTests
{
    private const int SmallScene = 100;
    private const int LargeScene = 400;

    // FindById and ReferenceScope.Find walk the tree and allocate as they go. Calling them for every object made a
    // scene four times larger allocate about sixteen times as much.
    [TestCase(false, TestName = "An edit allocates in proportion to the scene")]
    [TestCase(true, TestName = "An edit allocates in proportion to a scene full of references")]
    public void Allocations_grow_with_the_scene(bool references)
    {
        // The first edit also fills the caches of the serializer.
        _ = AllocatedByARename(SmallScene, references);
        long small = AllocatedByARename(SmallScene, references);
        long large = AllocatedByARename(LargeScene, references);

        Assert.That(large, Is.LessThan(small * 8), $"{SmallScene} elements: {small} bytes, {LargeScene} elements: {large} bytes");
    }

    // Placing each element used to scan the list from its start. A deleted element, which stays in the list until the
    // others are placed, keeps every later one a step away from its index.
    [TestCase(false, TestName = "Renaming an element reads the list in proportion to its length")]
    [TestCase(true, TestName = "Deleting the first element reads the list in proportion to its length")]
    public void List_accesses_grow_with_the_list(bool delete)
    {
        const int count = 256;
        Scene scene = CreateScene(count, references: false);
        using var session = new AgentToolkitTestSession(scene);
        int accesses = 0;
        ((ListProperty<Element>)scene.Children).BeforeAccess = () => accesses++;
        JsonObject item = delete
            ? new JsonObject { ["Id"] = scene.Children[0].Id.Value.ToString(), ["$delete"] = true }
            : new JsonObject { ["Id"] = scene.Children[0].Id.Value.ToString(), [nameof(CoreObject.Name)] = "Renamed" };

        ToolResult<ApplyEditResponse> result = session.CreateEditTools().ApplyEdit(
            patch: new JsonObject { ["Elements"] = new JsonArray(item) },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(accesses, Is.LessThan(count * 16));
        });
    }

    private static long AllocatedByARename(int count, bool references)
    {
        Scene scene = CreateScene(count, references);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        var patch = new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["Id"] = scene.Children[count / 2].Id.Value.ToString(),
                [nameof(CoreObject.Name)] = "Renamed",
            })
        };

        long before = GC.GetAllocatedBytesForCurrentThread();
        ToolResult<ApplyEditResponse> result = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        return allocated;
    }

    private static Scene CreateScene(int count, bool references)
    {
        Scene scene = TestWorkspace.CreateScene();
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        Scene? referenced = null;
        if (references)
        {
            referenced = TestWorkspace.CreateScene("referenced");
            var project = new Project();
            project.Items.Add(scene);
            project.Items.Add(referenced);
            _ = new BeutlApplication { Project = { Value = project } };
        }

        var elements = new Element[count];
        for (int i = 0; i < count; i++)
        {
            elements[i] = new Element
            {
                Name = { Value = $"Element {i}" },
                Start = { Value = MediaTime.FromSeconds(i * 3) },
                Length = { Value = MediaTime.FromSeconds(2) },
                Uri = new Uri(Path.Combine(directory, $"{Guid.NewGuid():N}.belm")),
            };
            if (referenced is null)
            {
                elements[i].AddObject(new RectShape());
            }
            else
            {
                var drawable = new SceneDrawable();
                drawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(referenced.Id.Value);
                elements[i].AddObject(drawable);
            }
        }

        scene.Children.AddRange(elements);
        return scene;
    }
}
