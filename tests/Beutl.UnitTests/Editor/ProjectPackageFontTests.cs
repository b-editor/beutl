using System.IO.Compression;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Editor;

public class ProjectPackageFontTests
{
    [Test]
    public async Task Import_InAFreshProcess_LoadsBundledFontsAndCanExportThemAgain()
    {
        string root = Directory.CreateTempSubdirectory("beutl-package-fonts-").FullName;
        try
        {
            string font = Path.Combine(root, "custom.ttf");
            using (Stream source = typeof(ProjectPackageFontTests).Assembly
                       .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.BeutlTestVariable.ttf")!)
            using (Stream destination = File.Create(font))
                source.CopyTo(destination);
            string projectRoot = Directory.CreateDirectory(Path.Combine(root, "sender")).FullName;
            var text = new TextBlock();
            text.FontFamily.CurrentValue = new FontFamily("Beutl Test Variable");
            text.Text.CurrentValue = "IIII";
            var element = new Element { Length = TimeSpan.FromSeconds(1) };
            element.Objects.Add(text);
            CoreSerializer.StoreToUri(element, new Uri(Path.Combine(projectRoot, "text.belm")));
            var scene = new Scene(64, 64, "scene") { Duration = TimeSpan.FromSeconds(1) };
            scene.Children.Add(element);
            CoreSerializer.StoreToUri(scene, new Uri(Path.Combine(projectRoot, "scene.scene")));
            var project = new Project { Name = "Font project" };
            project.Items.Add(scene);
            CoreSerializer.StoreToUri(project, new Uri(Path.Combine(projectRoot, "project.bep")));
            string package = Path.Combine(root, "font.beutlpkg");
            var service = new ProjectPackageService(new ResourceRelocationService(_ => [font]));

            ExportResult result = await service.ExportAsync(project, package);
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            File.Delete(font);
            Directory.Delete(projectRoot, true);

            await TestWorkerProgram.RunAsync(TestWorkerProgram.ProjectFontWorkerArgument,
                package, Path.Combine(root, "recipient"));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static async Task RunImportWorker(string package, string destination)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
        Log.LoggerFactory = loggerFactory;
        BeutlHomeIsolation.Begin("beutl-font-worker");
        try
        {
            GlobalConfiguration.Instance.FontConfig.FontDirectories.Clear();
            // Like the hosts, load the built-in scene types before resolving serialized discriminators.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Scene).TypeHandle);
            var family = new FontFamily("Beutl Test Variable");
            Assert.That(FontManager.Instance.IsRegistered(family), Is.False);

            Project project = (await ProjectPackageService.Current.ImportAsync(package, destination))!;

            Assert.That(project, Is.Not.Null);
            Assert.That(FontManager.Instance.IsRegistered(family), Is.True);
            Assert.That(new Typeface(family).ToSkia().FamilyName, Is.EqualTo(family.Name));
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string fontsRoot = Path.Combine(projectRoot, "resources", "fonts");
            Assert.That(Directory.GetFiles(fontsRoot), Has.Length.EqualTo(1));
            // Reopening/loading again must not accumulate duplicate typefaces.
            int count = FontManager.Instance.GetTypefaces(family).Length;
            FontManager.Instance.LoadProjectFonts(CoreSerializer.RestoreFromUri<Project>(project.Uri));
            Assert.That(FontManager.Instance.GetTypefaces(family).Length, Is.EqualTo(count));

            string secondPackage = Path.Combine(destination, "shared-again.beutlpkg");
            ExportResult result = await ProjectPackageService.Current.ExportAsync(project, secondPackage);
            Assert.That(result.Success, Is.True);
            Assert.That(result.FailedResources, Is.Empty);
            using var archive = ZipFile.OpenRead(secondPackage);
            Assert.That(archive.Entries.Count(entry => entry.FullName.StartsWith("resources/fonts/")), Is.EqualTo(1));
            File.Delete(Directory.GetFiles(fontsRoot).Single());
            Assert.That(new Typeface(family).ToSkia().FamilyName, Is.EqualTo(family.Name));
        }
        finally { BeutlHomeIsolation.End(); }
    }
}
