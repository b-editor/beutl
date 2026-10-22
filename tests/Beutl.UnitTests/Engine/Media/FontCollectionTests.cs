using System.Buffers.Binary;
using Beutl.Configuration;
using Beutl.Media;
using Beutl.ProjectSystem;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Beutl.UnitTests.Engine.Media;

// On macOS a face after the first needs FontManager's own loader (b-editor/beutl#3283).
[TestFixture]
public class FontCollectionTests
{
    private string _root = null!;
    private string _collection = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Directory.CreateTempSubdirectory("beutl-font-collection-").FullName;
        _collection = Path.Combine(_root, "resources", "fonts", "families.ttc");
        Directory.CreateDirectory(Path.GetDirectoryName(_collection)!);
        File.WriteAllBytes(_collection, CreateCollection("Roboto-Regular.ttf", "BeutlTestVariable.ttf"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void OpenFontFaces_OpensEveryFaceWithTheFontDataItShapesFrom()
    {
        SKTypeface[] faces = [.. FontManager.OpenFontFaces(_collection)];
        try
        {
            Assert.That(faces.Select(face => face.FamilyName).ToArray(), Is.EqualTo(new[] { "Roboto", "Beutl Test Variable" }));
            foreach (SKTypeface face in faces)
            {
                // TextShaper builds its HarfBuzz face from this stream and index.
                using Blob blob = face.OpenStream(out int index).ToHarfBuzzBlob();
                using var shapingFace = new Face(blob, index);
                Assert.That(shapingFace.GlyphCount, Is.EqualTo(face.GlyphCount));
            }
        }
        finally
        {
            foreach (SKTypeface face in faces) face.Dispose();
        }
    }

    [Test]
    public void LoadProjectFonts_RegistersAFaceAfterTheFirst()
    {
        var project = new Project { Uri = new Uri(Path.Combine(_root, "project.bep")) };
        var typeface = new Typeface(new FontFamily("Beutl Test Variable"));
        // Another fixture may have installed the family; the project's face must replace it.
        FontManager.Instance.TryResolveSkia(typeface, out SKTypeface? installed);
        try
        {
            FontManager.Instance.LoadProjectFonts(project);

            Assert.That(FontManager.Instance.TryResolveSkia(typeface, out SKTypeface face), Is.True);
            Assert.That(face, Is.Not.SameAs(installed));
            Assert.That(face.FamilyName, Is.EqualTo(typeface.FontFamily.Name));
        }
        finally
        {
            FontManager.Instance.ClearProjectFonts(project);
        }
    }

    [Test]
    public void InstalledCollections_RegisterEveryFace()
    {
        var family = new FontFamily("Beutl Test Variable");
        FontManager manager = CreateManager(Path.GetDirectoryName(_collection)!);

        Assert.Multiple(() =>
        {
            Assert.That(manager.FontFamilies, Is.SupersetOf(new[] { new FontFamily("Roboto"), family }));
            Assert.That(manager.IsRegistered(family), Is.True);
            // macOS loads a later face only when its family is first looked up.
            Assert.That(manager._fonts.ContainsKey(family), Is.EqualTo(!OperatingSystem.IsMacOS()));
        });
        Assert.That(manager.TryResolveSkia(new Typeface(family), out SKTypeface face), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(face.FamilyName, Is.EqualTo(family.Name));
            Assert.That(manager.GetTypefaces(family), Is.Not.Empty);
        });
    }

    [Test]
    public void ReadFamilyNames_NamesEveryFace()
    {
        Assert.That(FontManager.ReadFamilyNames(_collection), Is.EqualTo(new[] { "Roboto", "Beutl Test Variable" }));
    }

    [Test]
    public void LoadCopy_StopsAtTheCapOfTheCollection()
    {
        // Faces sharing one set of tables make every copy as large as the font, as a crafted file can.
        byte[] collection = CreateSharedCollection("Roboto-Regular.ttf", faces: 5);
        long copy = FontCollection.CopyFace(collection, 1)!.Length;
        var fonts = new FontCollection(_collection, collection, maxCopiedBytes: copy * 2);
        SKTypeface?[] faces = [.. Enumerable.Range(1, 4).Select(fonts.LoadCopy)];
        try
        {
            Assert.That(faces.Select(face => face?.FamilyName), Is.EqualTo(new[] { "Roboto", "Roboto", null, null }));
        }
        finally
        {
            foreach (SKTypeface? face in faces) face?.Dispose();
        }
    }

    private static FontManager CreateManager(string fontDirectory)
    {
        var directories = GlobalConfiguration.Instance.FontConfig.FontDirectories;
        directories.Add(fontDirectory);
        try
        {
            return (FontManager)Activator.CreateInstance(typeof(FontManager), nonPublic: true)!;
        }
        finally
        {
            directories.Remove(fontDirectory);
        }
    }

    private static byte[] CreateSharedCollection(string name, int faces)
    {
        byte[] font = ReadFont(name);
        int tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        int directory = 12 + tables * 16;
        int start = 12 + faces * 4 + (faces - 1) * directory;
        for (int table = 0; table < tables; table++)
        {
            Span<byte> offset = font.AsSpan(12 + table * 16 + 8, 4);
            BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)start);
        }

        byte[] collection = new byte[start + font.Length];
        "ttcf"u8.CopyTo(collection);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x10000);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), (uint)faces);
        font.CopyTo(collection, start);
        for (int face = 0; face < faces; face++)
        {
            int at = face == 0 ? start : 12 + faces * 4 + (face - 1) * directory;
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12 + face * 4), (uint)at);
            font.AsSpan(0, directory).CopyTo(collection.AsSpan(at));
        }

        return collection;
    }

    private static byte[] CreateCollection(params string[] names)
    {
        byte[][] fonts = [.. names.Select(ReadFont)];
        int[] offsets = new int[fonts.Length];
        int length = 12 + fonts.Length * 4;
        for (int i = 0; i < fonts.Length; i++)
        {
            offsets[i] = length;
            length = (length + fonts[i].Length + 3) & ~3;
        }

        byte[] collection = new byte[length];
        "ttcf"u8.CopyTo(collection);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4), 0x10000);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8), (uint)fonts.Length);
        for (int i = 0; i < fonts.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12 + i * 4), (uint)offsets[i]);
            // Table offsets in a collection count from the start of the file.
            int tables = BinaryPrimitives.ReadUInt16BigEndian(fonts[i].AsSpan(4));
            for (int table = 0; table < tables; table++)
            {
                Span<byte> offset = fonts[i].AsSpan(12 + table * 16 + 8, 4);
                BinaryPrimitives.WriteUInt32BigEndian(offset, BinaryPrimitives.ReadUInt32BigEndian(offset) + (uint)offsets[i]);
            }

            fonts[i].CopyTo(collection, offsets[i]);
        }

        return collection;
    }

    private static byte[] ReadFont(string name)
    {
        using Stream input = typeof(FontCollectionTests).Assembly
            .GetManifestResourceStream("Beutl.UnitTests.Assets.Font." + name)!;
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
