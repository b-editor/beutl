using System.IO.Compression;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public class NodeGraphPackageTests
{
    [Test]
    public void Node_input_fonts_are_collected()
    {
        var font = new FontFamily("Node package font");
        var port = new DefaultInputPort<FontFamily>();
        port.SetPropertyAdapter(new NodePropertyAdapter<FontFamily>("Font", font, null));

        var collected = ExternalResourceCollector.Collect(port, Path.GetTempPath());

        Assert.That(collected.FontFamilies, Is.EquivalentTo(new[] { font }));
    }

    [TestCase(false, "direct")]
    [TestCase(true, "direct")]
    [TestCase(false, "animated")]
    [TestCase(true, "animated")]
    [TestCase(false, "brush")]
    [TestCase(true, "brush")]
    public async Task Node_inputs_remain_portable_after_original_files_are_removed(bool external, string kind)
    {
        string root = Directory.CreateTempSubdirectory("node-package-").FullName;
        try
        {
            string projectDirectory = Path.Combine(root, "project"); Directory.CreateDirectory(projectDirectory);
            string assetDirectory = external ? Path.Combine(root, "external") : projectDirectory;
            Directory.CreateDirectory(assetDirectory);
            ImageSource first = TestImages.WriteImage(Path.Combine(assetDirectory, "first.png"));
            ImageSource second = TestImages.WriteImage(Path.Combine(assetDirectory, "second.png"));
            var drawable = new NodeGraphDrawable();
            GraphModel graph = drawable.Model.Value!;
            var output = new OutputNode(); graph.Nodes.Add(output);
            if (kind == "brush")
            {
                var node = new GeometryShapeNode();
                node.Geometry.Property!.SetValue(new RectGeometry { Width = { Value = 4 }, Height = { Value = 4 } });
                node.Fill.Property!.SetValue(new ImageBrush { Source = { Value = first } });
                graph.Nodes.Add(node); graph.Connect(output.InputPort, node.Output);
            }
            else
            {
                var node = new ImageSourceNode(); node.Source.Property!.SetValue(first);
                if (kind == "animated")
                {
                    var animation = new KeyFrameAnimation<ImageSource?>();
                    animation.KeyFrames.Add(new KeyFrame<ImageSource?> { KeyTime = MediaTime.Zero, Value = first });
                    animation.KeyFrames.Add(new KeyFrame<ImageSource?> { KeyTime = MediaTime.FromSeconds(1), Value = second });
                    ((IAnimatablePropertyAdapter<ImageSource?>)node.Source.Property).Animation = animation;
                }
                graph.Nodes.Add(node); graph.Connect(output.InputPort, node.Output);
            }

            var element = new Element { Uri = new Uri(Path.Combine(projectDirectory, "image.belm")), Length = MediaTime.FromSeconds(2) };
            element.Objects.Add(drawable);
            var scene = new Scene(64, 64, "Nodes") { Uri = new Uri(Path.Combine(projectDirectory, "main.scene")), Duration = MediaTime.FromSeconds(2) };
            scene.Children.Add(element);
            var project = new Project { Uri = new Uri(Path.Combine(projectDirectory, "main.bep")) };
            project.Items.Add(scene);
            CoreSerializer.StoreToUri(project, project.Uri);
            byte[] original = File.ReadAllBytes(element.Uri.LocalPath);
            project = CoreSerializer.RestoreFromUri<Project>(project.Uri);
            string package = Path.Combine(root, "shared.beutl");

            ExportResult exported = await ProjectPackageService.Current.ExportAsync(project, package);

            Assert.That(exported.Success, Is.True);
            Assert.That(exported.FailedResources, Is.Empty);
            Assert.That(File.ReadAllBytes(element.Uri.LocalPath), Is.EqualTo(original));
            using (var archive = ZipFile.OpenRead(package))
            {
                Assert.That(archive.Entries.Any(entry => entry.FullName.EndsWith("first.png")), Is.True);
                if (kind == "animated")
                    Assert.That(archive.Entries.Any(entry => entry.FullName.EndsWith("second.png")), Is.True);
            }
            Directory.Delete(projectDirectory, recursive: true);
            if (external) Directory.Delete(assetDirectory, recursive: true);
            Project? imported = await ProjectPackageService.Current.ImportAsync(package, Path.Combine(root, "imported"));
            Assert.That(imported, Is.Not.Null);
            string importedDirectory = Path.GetDirectoryName(imported!.Uri!.LocalPath)!;
            var restored = imported.Items.OfType<Scene>().Single().Children.Single().Objects.OfType<NodeGraphDrawable>().Single().Model.Value!;
            var sources = new List<ImageSource>();
            if (kind == "brush")
                sources.Add(((ImageBrush)restored.Nodes.OfType<GeometryShapeNode>().Single().Fill.Property!.GetValue()!).Source.Value!);
            else
            {
                IPropertyAdapter<ImageSource?> property = restored.Nodes.OfType<ImageSourceNode>().Single().Source.Property!;
                sources.Add(property.GetValue()!);
                if (kind == "animated")
                {
                    var animation = (KeyFrameAnimation<ImageSource?>)((IAnimatablePropertyAdapter<ImageSource?>)property).Animation!;
                    Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
                    sources.AddRange(animation.KeyFrames.Select(frame => (ImageSource)frame.Value!));
                }
            }
            foreach (ImageSource source in sources)
            {
                Assert.That(source.Uri.LocalPath, Does.StartWith(importedDirectory + Path.DirectorySeparatorChar));
                using var resource = source.ToResource(new CompositionContext(MediaTime.Zero));
                Assert.That(resource.Bitmap, Is.Not.Null);
                Assert.That((resource.Bitmap!.Width, resource.Bitmap.Height), Is.EqualTo((4, 4)));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
