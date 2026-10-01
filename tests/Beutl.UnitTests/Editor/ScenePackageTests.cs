using System.IO.Compression;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.Editor;

public class ScenePackageTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task External_scenes_keep_their_own_elements_after_original_files_are_removed(bool topLevel, bool nestedElements)
    {
        string root = Directory.CreateTempSubdirectory("scene-package-").FullName;
        try
        {
            string projectDirectory = Path.Combine(root, "project");
            string mainDirectory = Path.Combine(projectDirectory, "Main");
            Directory.CreateDirectory(mainDirectory);
            string library = Path.Combine(root, "library");
            var main = new Scene(64, 64, "Main") { Uri = new Uri(Path.Combine(mainDirectory, "main.scene")) };
            var project = new Project { Uri = new Uri(Path.Combine(projectDirectory, "main.bep")) };
            project.Items.Add(main);
            var expected = new Dictionary<string, Guid>();
            foreach (string name in new[] { "Alpha", "Beta" })
            {
                string directory = Path.Combine(library, name);
                string elementsDirectory = nestedElements ? Path.Combine(directory, "clips") : directory;
                Directory.CreateDirectory(elementsDirectory);
                string imagePath = Path.Combine(directory, "image.png");
                using (var bitmap = new Bitmap(4, 4))
                {
                    bitmap.GetPixelSpan().Fill(255);
                    Assert.That(bitmap.Save(imagePath, EncodedImageFormat.Png), Is.True);
                }
                var image = new ImageSource();
                image.ReadFrom(new Uri(imagePath));
                var element = new Element { Name = "Clip-" + name, Uri = new Uri(Path.Combine(elementsDirectory, "shared.belm")), Length = TimeSpan.FromSeconds(1) };
                element.Objects.Add(new SourceImage { Source = { CurrentValue = image } });
                expected.Add(name, element.Id);
                var scene = new Scene(64, 64, name) { Uri = new Uri(Path.Combine(directory, "shared.scene")) };
                scene.Children.Add(element);
                var removed = new Element { Uri = new Uri(Path.Combine(elementsDirectory, "removed.belm")) };
                scene.Children.Add(removed);
                CoreSerializer.StoreToUri(scene, scene.Uri);
                scene.Children.Remove(removed);
                CoreSerializer.StoreToUri(scene, scene.Uri);
                File.WriteAllText(Path.Combine(directory, "unrelated-private.txt"), "not part of the scene");
                if (topLevel)
                {
                    project.Items.Add(scene);
                }
                else
                {
                    var parent = new Element { Uri = new Uri(Path.Combine(mainDirectory, name + ".belm")), Length = TimeSpan.FromSeconds(1) };
                    parent.Objects.Add(new SceneDrawable { ReferencedScene = { CurrentValue = scene } });
                    main.Children.Add(parent);
                }
            }
            CoreSerializer.StoreToUri(project, project.Uri);
            project = CoreSerializer.RestoreFromUri<Project>(project.Uri);
            Assert.That(GetScenes(project, topLevel).All(scene => scene.Children.Count == 1), Is.True);
            var originals = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            string package = Path.Combine(root, "shared.beutl");

            ExportResult result = await ProjectPackageService.Current.ExportAsync(project, package);

            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            foreach (var (path, bytes) in originals)
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), path);
            using (var archive = ZipFile.OpenRead(package))
            {
                Assert.That(archive.Entries.Any(entry => entry.FullName.EndsWith("unrelated-private.txt")), Is.False);
                Assert.That(archive.Entries.Any(entry => entry.FullName.EndsWith("removed.belm")), Is.False);
            }
            Directory.Delete(projectDirectory, recursive: true);
            Directory.Delete(library, recursive: true);
            Project? imported = await ProjectPackageService.Current.ImportAsync(package, Path.Combine(root, "imported"));
            Assert.That(imported, Is.Not.Null);
            string importedRoot = Path.GetDirectoryName(imported!.Uri!.LocalPath)!;
            Scene[] scenes = GetScenes(imported, topLevel).ToArray();
            Assert.That(scenes.Select(scene => scene.Name), Is.EquivalentTo(expected.Keys));
            foreach (Scene scene in scenes)
            {
                Assert.That(scene.Children, Has.Count.EqualTo(1), scene.Name);
                Element element = scene.Children.Single();
                Assert.That(element.Id, Is.EqualTo(expected[scene.Name]));
                Assert.That(element.Name, Is.EqualTo("Clip-" + scene.Name));
                Assert.That(scene.Uri!.LocalPath, Does.StartWith(importedRoot + Path.DirectorySeparatorChar));
                Assert.That(Path.GetRelativePath(Path.GetDirectoryName(scene.Uri.LocalPath)!, element.Uri!.LocalPath),
                    Is.EqualTo(nestedElements ? Path.Combine("clips", "shared.belm") : "shared.belm"));
                ImageSource source = element.Objects.OfType<SourceImage>().Single().Source.CurrentValue!;
                Assert.That(source.Uri.LocalPath, Does.StartWith(importedRoot + Path.DirectorySeparatorChar));
                using var resource = source.ToResource(new CompositionContext(TimeSpan.Zero));
                Assert.That(resource.Bitmap, Is.Not.Null);
                Assert.That((resource.Bitmap!.Width, resource.Bitmap.Height), Is.EqualTo((4, 4)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<Scene> GetScenes(Project project, bool topLevel)
        => topLevel
            ? project.Items.OfType<Scene>().Where(scene => scene.Name != "Main")
            : project.Items.OfType<Scene>().Single().Children.Select(element => element.Objects.OfType<SceneDrawable>().Single().ReferencedScene.CurrentValue!);
}
