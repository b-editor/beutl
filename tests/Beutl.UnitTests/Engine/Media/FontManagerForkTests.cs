using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

// The host's manager, a fork per open project and a fork per scene editor. Two projects bundle the same
// family/style/weight with different bytes; each is told apart by the fontRevision its head table carries.
public class FontManagerForkTests
{
    private const uint HeadTag = 0x68656164;
    private static readonly FontFamily s_family = new("Beutl Test Variable");
    private string _root = null!;

    [SetUp]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("beutl-font-fork-").FullName;

    [TearDown]
    public void DeleteRoot() => Directory.Delete(_root, true);

    [Test]
    public void ProjectForksResolveTheSameFamilyFromTheirOwnFonts()
    {
        FontManager host = CreateManager();
        FontManager first = host.Fork();
        FontManager second = host.Fork();
        first.LoadProjectFonts(CreateProject("first", 0x00010000));
        second.LoadProjectFonts(CreateProject("second", 0x00020000));

        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(first), Is.EqualTo(0x00010000));
            Assert.That(FontRevision(second), Is.EqualTo(0x00020000));
            Assert.That(host.IsRegistered(s_family), Is.False, "A fork's fonts must not reach its parent.");
            Assert.That(first.FontFamilies, Does.Contain(s_family));
            Assert.That(host.FontFamilies, Does.Not.Contain(s_family));
            Assert.That(first.GetTypefaces(s_family), Is.Not.Empty);
            Assert.That(host.GetTypefaces(s_family), Is.Empty);
        });
    }

    [Test]
    public void ASceneForkSeesItsProjectsFontsAndTheInstalledOnes()
    {
        FontManager host = CreateManager();
        FontManager project = host.Fork();
        FontManager scene = project.Fork();
        long sceneRevision = scene.Revision;

        project.LoadProjectFonts(CreateProject("project", 0x00010000));

        Assert.Multiple(() =>
        {
            Assert.That(scene.Revision, Is.Not.EqualTo(sceneRevision), "Loading into a parent changes what its forks resolve.");
            Assert.That(scene.IsRegistered(s_family), Is.True);
            Assert.That(FontRevision(scene), Is.EqualTo(0x00010000));
            Assert.That(scene.ResolveSkia(new Typeface(s_family)), Is.SameAs(project.ResolveSkia(new Typeface(s_family))));
            Assert.That(scene.ResolveSkia(host.DefaultTypeface).FamilyName, Is.EqualTo(host.DefaultTypeface.FontFamily.Name));
        });

        // Fonts loaded into the scene's fork go ahead of the project's, and stay with the scene.
        long projectRevision = project.Revision;
        scene.LoadProjectFonts(CreateProject("scene", 0x00030000));
        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(scene), Is.EqualTo(0x00030000));
            Assert.That(FontRevision(project), Is.EqualTo(0x00010000));
            Assert.That(project.Revision, Is.EqualTo(projectRevision));
        });
    }

    [Test]
    public void InstalledFontsAndTheFallbackOrderAreShared()
    {
        FontManager host = CreateManager();
        FontManager project = host.Fork();
        FontManager sibling = host.Fork();
        project.LoadProjectFonts(CreateProject("project", 0x00010000));
        long siblingRevision = sibling.Revision;

        project.SetFallbackFonts([s_family], null);

        Assert.Multiple(() =>
        {
            Assert.That(host.GetFallbackFamilies().Families, Is.EqualTo(new[] { s_family }));
            Assert.That(sibling.GetFallbackFamilies().Families, Is.EqualTo(new[] { s_family }));
            Assert.That(sibling.Revision, Is.Not.EqualTo(siblingRevision));
        });

        // A sibling's fonts leave the other fork's text alone.
        siblingRevision = sibling.Revision;
        project.LoadProjectFonts(CreateProject("again", 0x00020000));
        Assert.That(sibling.Revision, Is.EqualTo(siblingRevision));
    }

    [Test]
    public void ACapturedForkKeepsItsFontsAfterItsProjectCloses()
    {
        FontManager host = CreateManager();
        string closingDirectory = Path.Combine(_root, "closing");
        // What a background export captured with its scene snapshot.
        FontManager captured = host.Fork();
        captured.LoadProjectFonts(CreateProject("closing", 0x00010000));
        using var resource = CreateText().ToResource(new CompositionContext(MediaTime.Zero) { Fonts = captured });
        resource.Measure(new Size(300, 200));

        // The project closes, its files go, and another project opens.
        host.Fork().LoadProjectFonts(CreateProject("next", 0x00020000));
        Directory.Delete(Path.Combine(closingDirectory, "resources"), true);
        bool changed = false;
        resource.Reconcile(CreateText(), new CompositionContext(MediaTime.Zero) { Fonts = captured }, ref changed);
        resource.Measure(new Size(300, 200));

        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(captured), Is.EqualTo(0x00010000));
            Assert.That(LaidOutFontRevisions(resource), Is.Not.Empty.And.All.EqualTo(0x00010000));
        });
    }

    [Test]
    public async Task InterleavedRendersResolveTheirOwnProjectsFonts()
    {
        FontManager host = CreateManager();
        FontManager[] projects = [host.Fork(), host.Fork()];
        projects[0].LoadProjectFonts(CreateProject("first", 0x00010000));
        projects[1].LoadProjectFonts(CreateProject("second", 0x00020000));
        uint[] expected = [0x00010000, 0x00020000];
        using var stop = new CancellationTokenSource();

        // Editors open and close scenes of both projects while they render.
        Task forking = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
                _ = projects[i % 2].Fork().IsRegistered(s_family);
        });
        Task<uint[]>[] renders = [.. Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            FontManager scene = projects[index % 2].Fork();
            var revisions = new List<uint>();
            for (int frame = 0; frame < 20; frame++)
            {
                using var resource = CreateText().ToResource(new CompositionContext(MediaTime.Zero) { Fonts = scene });
                resource.Measure(new Size(300, 200));
                revisions.AddRange(LaidOutFontRevisions(resource));
            }
            return revisions.ToArray();
        }))];

        try
        {
            await Task.WhenAll(renders);
        }
        finally
        {
            await stop.CancelAsync();
            await forking;
        }

        for (int index = 0; index < renders.Length; index++)
            Assert.That(renders[index].Result, Is.Not.Empty.And.All.EqualTo(expected[index % 2]));
    }

    [Test]
    public void AForkLetGoReleasesItsFonts()
    {
        FontManager host = CreateManager();
        (WeakReference fork, WeakReference face) = LoadAndDropFork(host);

        for (int i = 0; i < 3 && (fork.IsAlive || face.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Multiple(() =>
        {
            Assert.That(fork.IsAlive, Is.False);
            Assert.That(face.IsAlive, Is.False);
            Assert.That(host.IsRegistered(s_family), Is.False);
        });

        // The same project opened again loads its fonts again.
        FontManager reopened = host.Fork();
        reopened.LoadProjectFonts(new Project { Uri = new Uri(Path.Combine(_root, "closed", "project.bep")) });
        Assert.That(FontRevision(reopened), Is.EqualTo(0x00010000));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference Fork, WeakReference Face) LoadAndDropFork(FontManager host)
    {
        FontManager fork = host.Fork();
        fork.LoadProjectFonts(CreateProject("closed", 0x00010000));
        Assert.That(fork.TryResolveSkia(new Typeface(s_family), out SKTypeface face), Is.True);
        return (new WeakReference(fork), new WeakReference(face));
    }

    private static TextBlock CreateText()
        => new() { Text = { Value = "IIII" }, FontFamily = { Value = s_family }, Size = { Value = 32 } };

    private static List<uint> LaidOutFontRevisions(TextBlock.Resource resource)
    {
        var revisions = new List<uint>();
        foreach (Span<FormattedText> line in resource.GetTextElements().Lines)
        {
            foreach (FormattedText text in line)
            {
                using SKFont font = text.ToSKFont();
                revisions.Add(ReadFontRevision(font.Typeface));
            }
        }

        return revisions;
    }

    private static uint FontRevision(FontManager fonts) => ReadFontRevision(fonts.ResolveSkia(new Typeface(s_family)));

    private static uint ReadFontRevision(SKTypeface face) => BinaryPrimitives.ReadUInt32BigEndian(face.GetTableData(HeadTag).AsSpan(4));

    private Project CreateProject(string name, uint fontRevision)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        byte[] font;
        using (Stream input = typeof(FontManagerForkTests).Assembly
                   .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.BeutlTestVariable.ttf")!)
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            font = buffer.ToArray();
        }

        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(HeadOffset(font) + 4), fontRevision);
        string fonts = Directory.CreateDirectory(Path.Combine(directory, "resources", "fonts")).FullName;
        File.WriteAllBytes(Path.Combine(fonts, "font.ttf"), font);
        return new Project { Uri = new Uri(Path.Combine(directory, "project.bep")) };
    }

    private static int HeadOffset(byte[] font)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int index = 0; index < count; index++)
        {
            int entry = 12 + index * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry)) == HeadTag)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(entry + 8));
        }

        throw new InvalidDataException("Font has no head table.");
    }

    private static FontManager CreateManager()
    {
        var configuration = new FontConfig();
        configuration.FontDirectories.Clear();
        return new FontManager(configuration);
    }
}
