using System.Buffers.Binary;
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

            byte[] bundled;
            using (var inputArchive = ZipFile.OpenRead(package))
            using (Stream input = inputArchive.Entries.Single(entry => entry.FullName.EndsWith(".ttf")).Open())
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                bundled = output.ToArray();
            }
            byte[] host = WithFontRevision(bundled, 0x00020000);
            string hostDirectory = Directory.CreateDirectory(Path.Combine(destination, "host-fonts")).FullName;
            File.WriteAllBytes(Path.Combine(hostDirectory, "host.ttf"), host);
            GlobalConfiguration.Instance.FontConfig.FontDirectories.Add(hostDirectory);
            using (var stream = new MemoryStream(host)) FontManager.Instance.AddFont(stream);
            Assert.That(FontRevision(family), Is.EqualTo(0x00020000));

            Project project = (await ProjectPackageService.Current.ImportAsync(package, destination))!;

            Assert.That(project, Is.Not.Null);
            Assert.That(FontManager.Instance.IsRegistered(family), Is.True);
            Assert.That(new Typeface(family).ToSkia().FamilyName, Is.EqualTo(family.Name));
            uint bundledRevision = ReadFontRevision(bundled);
            Assert.That(FontRevision(family), Is.EqualTo(bundledRevision));
            string projectRoot = Path.GetDirectoryName(project.Uri!.LocalPath)!;
            string fontsRoot = Path.Combine(projectRoot, "resources", "fonts");
            Assert.That(Directory.GetFiles(fontsRoot), Has.Length.EqualTo(1));
            // Reopening/loading again must not accumulate duplicate typefaces.
            int count = FontManager.Instance.GetTypefaces(family).Length;
            var originalTypeface = new Typeface(family).ToSkia();
            FontManager.Instance.LoadProjectFonts(CoreSerializer.RestoreFromUri<Project>(project.Uri));
            Assert.That(FontManager.Instance.GetTypefaces(family).Length, Is.EqualTo(count));
            Assert.That(new Typeface(family).ToSkia(), Is.SameAs(originalTypeface));

            string otherRoot = Directory.CreateDirectory(Path.Combine(destination, "other-project", "resources", "fonts")).Parent!.Parent!.FullName;
            File.WriteAllBytes(Path.Combine(otherRoot, "resources", "fonts", "custom.ttf"), WithFontRevision(bundled, 0x00030000));
            FontManager.Instance.LoadProjectFonts(new Project { Uri = new Uri(Path.Combine(otherRoot, "other.bep")) });
            Assert.That(FontRevision(family), Is.EqualTo(0x00030000));
            FontManager.Instance.LoadProjectFonts(new Project { Uri = new Uri(Path.Combine(destination, "empty", "empty.bep")) });
            Assert.That(FontRevision(family), Is.EqualTo(0x00020000));
            FontManager.Instance.LoadProjectFonts(project);
            Assert.That(new Typeface(family).ToSkia(), Is.SameAs(originalTypeface));

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

    private static uint FontRevision(FontFamily family)
        => BinaryPrimitives.ReadUInt32BigEndian(new Typeface(family).ToSkia().GetTableData(0x68656164).AsSpan(4));

    private static uint ReadFontRevision(byte[] font) => BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(HeadOffset(font) + 4));

    private static int HeadOffset(byte[] font)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int index = 0; index < count; index++)
        {
            int entry = 12 + index * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry)) == 0x68656164)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8));
        }
        throw new InvalidDataException("Font has no head table.");
    }

    private static byte[] WithFontRevision(byte[] original, uint revision)
    {
        byte[] font = original.ToArray();
        int head = HeadOffset(font);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(head + 4), revision);
        // These fixtures retain the same family/style and glyph outlines; only the
        // revision identifies which file the renderer actually selected.
        return font;
    }
}
