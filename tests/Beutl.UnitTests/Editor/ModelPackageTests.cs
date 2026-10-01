using System.IO.Compression;
using System.Numerics;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics3D;

namespace Beutl.UnitTests.Editor;

public class ModelPackageTests
{
    [Test]
    public async Task Absolute_model_dependencies_are_reported_when_the_copy_still_uses_the_original()
    {
        string root = Directory.CreateTempSubdirectory("absolute-model-package-").FullName;
        try
        {
            string external = Directory.CreateDirectory(Path.Combine(root, "external")).FullName;
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            string texture = Path.Combine(external, "albedo.png");
            using (var bitmap = new Beutl.Media.Bitmap(1, 1))
            {
                bitmap.GetPixelSpan().Fill(255);
                Assert.That(bitmap.Save(texture), Is.True);
            }
            File.WriteAllText(Path.Combine(external, "material.mtl"), $"newmtl Painted\nKd 1 1 1\nmap_Kd {texture}\n");
            string modelPath = Path.Combine(external, "model.obj");
            File.WriteAllText(modelPath, "mtllib material.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Painted\nf 1 2 3\n");
            var source = new ModelSource(); source.ReadFrom(new Uri(modelPath));
            var model = new Model3D(); model.Source.CurrentValue = source;
            var scene3D = new Scene3D(); scene3D.Objects.Add(model);
            var element = new Element { Uri = new Uri(Path.Combine(projectDirectory, "model.belm")) };
            element.Objects.Add(scene3D);
            var scene = new Scene(64, 64, "Model") { Uri = new Uri(Path.Combine(projectDirectory, "main.scene")) };
            scene.Children.Add(element);
            var project = new Project { Uri = new Uri(Path.Combine(projectDirectory, "main.bep")) };
            project.Items.Add(scene);
            CoreSerializer.StoreToUri(project, project.Uri);

            ExportResult result = await ProjectPackageService.Current.ExportAsync(project, Path.Combine(root, "shared.beutl"));

            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Not.Empty);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task Export_round_trip_preserves_external_models_and_edits(bool externalBuffer, bool absoluteReference)
        => await AssertRoundTrip(externalBuffer, absoluteReference, internalModel: false);

    [TestCase(false)]
    [TestCase(true)]
    public async Task Export_round_trip_preserves_internal_models_with_external_buffers(bool absoluteReference)
        => await AssertRoundTrip(externalBuffer: true, absoluteReference, internalModel: true);

    private static async Task AssertRoundTrip(bool externalBuffer, bool absoluteReference, bool internalModel)
    {
        string root = Directory.CreateTempSubdirectory("model-package-").FullName;
        try
        {
            string external = Path.Combine(root, "external");
            string projectDirectory = Path.Combine(root, "project");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(external);
            string path = ModelTestFiles.WriteGltf(internalModel ? projectDirectory : external, externalBuffer);
            if (internalModel)
            {
                Directory.Move(Path.Combine(projectDirectory, "buffers"), Path.Combine(external, "buffers"));
                var json = JsonNode.Parse(File.ReadAllText(path))!;
                json["buffers"]![0]!["uri"] = "../external/buffers/geometry.bin";
                File.WriteAllText(path, json.ToJsonString());
            }
            byte[] modelBytes = File.ReadAllBytes(path);
            File.WriteAllText(Path.Combine(external, "unrelated-private.txt"), "not part of the model");
            var source = new ModelSource(); source.ReadFrom(new Uri(path));
            var model = new Model3D(); model.Source.CurrentValue = source;
            Object3D child = model.Children.Single();
            child.Position.CurrentValue = new Vector3(10, 20, 30);
            ((PBRMaterial)child.Material.CurrentValue!).Albedo.CurrentValue = Beutl.Media.Colors.Red;
            var scene3D = new Scene3D(); scene3D.Objects.Add(model);
            var element = new Element { Length = TimeSpan.FromSeconds(1), Uri = new Uri(Path.Combine(projectDirectory, "model.belm")) };
            element.Objects.Add(scene3D);
            var scene = new Scene(64, 64, "Model") { Uri = new Uri(Path.Combine(projectDirectory, "main.scene")), Duration = TimeSpan.FromSeconds(1) };
            scene.Children.Add(element);
            var project = new Project { Uri = new Uri(Path.Combine(projectDirectory, "main.bep")) };
            project.Items.Add(scene);
            CoreSerializer.StoreToUri(project, project.Uri);
            if (absoluteReference)
            {
                var json = JsonNode.Parse(File.ReadAllText(element.Uri.LocalPath))!;
                json["Objects"]![0]!["Objects"]![0]!["Source"] = source.Uri.AbsoluteUri;
                File.WriteAllText(element.Uri.LocalPath, json.ToJsonString());
            }
            project = CoreSerializer.RestoreFromUri<Project>(project.Uri);
            byte[] original = File.ReadAllBytes(element.Uri.LocalPath);
            string package = Path.Combine(root, "shared.beutl");

            ExportResult result = await ProjectPackageService.Current.ExportAsync(project, package);

            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            Assert.That(File.ReadAllBytes(element.Uri.LocalPath), Is.EqualTo(original));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(modelBytes));
            using (var archive = ZipFile.OpenRead(package))
            {
                Assert.That(archive.Entries.Any(e => e.FullName.EndsWith("geometry.bin")), Is.EqualTo(externalBuffer));
                Assert.That(archive.Entries.Any(e => e.FullName.EndsWith("unrelated-private.txt")), Is.False);
            }
            Directory.Delete(external, recursive: true);
            Project? imported = await ProjectPackageService.Current.ImportAsync(package, Path.Combine(root, "imported"));
            Assert.That(imported, Is.Not.Null);
            var importedModel = imported!.Items.OfType<Scene>().Single().Children.Single().Objects.OfType<Scene3D>().Single().Objects.OfType<Model3D>().Single();
            Object3D importedChild = importedModel.Children.Single();
            string importedRoot = Path.GetDirectoryName(imported.Uri!.LocalPath)!;
            Assert.Multiple(() =>
            {
                Assert.That(importedChild.Id, Is.EqualTo(child.Id));
                Assert.That(importedChild.Position.CurrentValue, Is.EqualTo(new Vector3(10, 20, 30)));
                Assert.That(((PBRMaterial)importedChild.Material.CurrentValue!).Albedo.CurrentValue, Is.EqualTo(Beutl.Media.Colors.Red));
                Assert.That(importedModel.Source.CurrentValue!.MeshCount, Is.EqualTo(1));
                Assert.That(importedModel.Source.CurrentValue.Uri.LocalPath, Does.StartWith(importedRoot + Path.DirectorySeparatorChar));
                Assert.That(importedModel.Source.CurrentValue.Dependencies.All(File.Exists), Is.True);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
