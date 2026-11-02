using Beutl.Editor;
using Beutl.Graphics3D.Models;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class FileSourceFilesTests
{
    [Test]
    public void A_source_that_read_a_file_names_it()
    {
        TestMediaHelper.RegisterTestDecoder();
        string path = TestMediaHelper.CreateTestAudioFile(durationSeconds: 1.0);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));

        Assert.That(FileSourceFiles.EnumerateLocalPaths(source), Is.EqualTo(new[] { path }));
    }

    [Test]
    public void A_read_model_names_its_file_and_the_files_it_reads()
    {
        string root = Directory.CreateTempSubdirectory("file-source-files-").FullName;
        try
        {
            string model = Path.Combine(root, "model.obj");
            string material = Path.Combine(root, "material.mtl");
            File.WriteAllText(model, "mtllib material.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Surface\nf 1 2 3\n");
            File.WriteAllText(material, "newmtl Surface\nKd 1 1 1\n");
            var source = new ModelSource();
            source.ReadFrom(new Uri(model));

            Assert.That(FileSourceFiles.EnumerateLocalPaths(source), Is.SupersetOf(new[] { model, material }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Media and model sources throw from Uri until they read a file, so an unread one names nothing.
    [Test]
    public void An_unread_media_or_model_source_names_no_file()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FileSourceFiles.EnumerateLocalPaths(new SoundSource()), Is.Empty);
            Assert.That(FileSourceFiles.EnumerateLocalPaths(new ModelSource()), Is.Empty);
        });
    }

    [Test]
    public void A_source_away_from_the_file_system_names_no_file()
    {
        var source = new RemoteSource(new Uri("https://example.com/clip.wav"));

        Assert.That(FileSourceFiles.EnumerateLocalPaths(source), Is.Empty);
    }

    private sealed class RemoteSource(Uri uri) : IFileSource
    {
        public Uri Uri { get; } = uri;

        public void ReadFrom(Uri uri) => throw new NotSupportedException();
    }
}
