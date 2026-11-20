using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.ProjectSystem;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media;

// Two live projects bundle the same family/style/weight with different bytes; each is told apart by the
// fontRevision its head table carries.
public class ProjectFontScopeTests
{
    private const uint HeadTag = 0x68656164;
    private static readonly FontFamily s_family = new("Beutl Test Variable");
    private string _root = null!;

    [SetUp]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("beutl-font-scope-").FullName;

    [TearDown]
    public void DeleteRoot() => Directory.Delete(_root, true);

    [Test]
    public void ScopesResolveTheSameFamilyFromTheirOwnProjects()
    {
        FontManager fonts = CreateManager();
        Project first = CreateProject("first", 0x00010000);
        Project second = CreateProject("second", 0x00020000);

        FontManager firstScope = fonts.ForProject(first);
        FontManager secondScope = fonts.ForProject(second);

        Assert.Multiple(() =>
        {
            Assert.That(fonts.ForProject(first), Is.SameAs(firstScope));
            Assert.That(firstScope.ForProject(first), Is.SameAs(firstScope));
            Assert.That(firstScope.ForProject(second), Is.SameAs(secondScope));
            Assert.That(FontRevision(firstScope), Is.EqualTo(0x00010000));
            Assert.That(FontRevision(secondScope), Is.EqualTo(0x00020000));
            Assert.That(fonts.IsRegistered(s_family), Is.False, "A scope must not change the active palette.");
            Assert.That(firstScope.FontFamilies, Does.Contain(s_family));
            Assert.That(firstScope.GetTypefaces(s_family), Is.Not.Empty);
        });

        long secondRevision = secondScope.Revision;
        fonts.LoadProjectFonts(first);
        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(fonts), Is.EqualTo(0x00010000));
            Assert.That(fonts.ResolveSkia(new Typeface(s_family)), Is.SameAs(firstScope.ResolveSkia(new Typeface(s_family))));
            Assert.That(FontRevision(secondScope), Is.EqualTo(0x00020000));
            Assert.That(secondScope.Revision, Is.EqualTo(secondRevision), "Switching the palette must not invalidate another scope's text.");
        });

        // Installed fonts and fallback order stay shared.
        long rootRevision = fonts.Revision;
        firstScope.SetFallbackFonts([s_family], null);
        Assert.Multiple(() =>
        {
            Assert.That(fonts.GetFallbackFamilies().Families, Is.EqualTo(new[] { s_family }));
            Assert.That(secondScope.GetFallbackFamilies().Families, Is.EqualTo(new[] { s_family }));
            Assert.That(fonts.Revision, Is.Not.EqualTo(rootRevision));
            Assert.That(secondScope.Revision, Is.Not.EqualTo(secondRevision));
        });
    }

    [Test]
    public void ReloadingAProjectUpdatesItsScopeAndThePalette()
    {
        FontManager fonts = CreateManager();
        Project project = CreateProject("project", 0x00010000);
        FontManager scope = fonts.ForProject(project);
        fonts.LoadProjectFonts(project);
        long scopeRevision = scope.Revision;

        WriteFont(Path.GetDirectoryName(project.Uri!.LocalPath)!, 0x00030000);
        scope.LoadProjectFonts(project);

        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(scope), Is.EqualTo(0x00030000));
            Assert.That(FontRevision(fonts), Is.EqualTo(0x00030000));
            Assert.That(scope.Revision, Is.Not.EqualTo(scopeRevision));
            Assert.That(() => scope.LoadProjectFonts(CreateProject("other", 0x00040000)), Throws.ArgumentException);
            Assert.That(() => scope.ClearProjectFonts(), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void ACapturedScopeKeepsItsFontsAfterItsProjectCloses()
    {
        FontManager fonts = CreateManager();
        Project closing = CreateProject("closing", 0x00010000);
        Project next = CreateProject("next", 0x00020000);
        fonts.LoadProjectFonts(closing);
        // What a background export captured with its scene snapshot.
        FontManager captured = fonts.ForProject(closing);
        using var resource = CreateText().ToResource(new CompositionContext(MediaTime.Zero) { Fonts = captured });
        resource.Measure(new Size(300, 200));

        fonts.ClearProjectFonts(closing);
        fonts.LoadProjectFonts(next);
        Directory.Delete(Path.Combine(Path.GetDirectoryName(closing.Uri!.LocalPath)!, "resources"), true);
        bool changed = false;
        resource.Reconcile(CreateText(), new CompositionContext(MediaTime.Zero) { Fonts = captured }, ref changed);
        resource.Measure(new Size(300, 200));

        Assert.Multiple(() =>
        {
            Assert.That(FontRevision(fonts), Is.EqualTo(0x00020000));
            Assert.That(FontRevision(captured), Is.EqualTo(0x00010000));
            Assert.That(LaidOutFontRevisions(resource), Is.All.EqualTo(0x00010000));
        });
    }

    [Test]
    public async Task InterleavedRendersResolveTheirOwnProjectsFonts()
    {
        FontManager fonts = CreateManager();
        Project first = CreateProject("first", 0x00010000);
        Project second = CreateProject("second", 0x00020000);
        FontManager[] scopes = [fonts.ForProject(first), fonts.ForProject(second)];
        uint[] expected = [0x00010000, 0x00020000];
        using var stop = new CancellationTokenSource();

        // The desktop palette keeps switching while both projects render.
        Task palette = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
            {
                if (i % 3 == 2) fonts.ClearProjectFonts();
                else fonts.LoadProjectFonts(i % 3 == 0 ? first : second);
            }
        });
        Task<uint[]>[] renders = [.. Enumerable.Range(0, 4).Select(index => Task.Run(() =>
        {
            FontManager scope = scopes[index % 2];
            var revisions = new List<uint>();
            for (int frame = 0; frame < 20; frame++)
            {
                using var resource = CreateText().ToResource(new CompositionContext(MediaTime.Zero) { Fonts = scope });
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
            await palette;
        }

        for (int index = 0; index < renders.Length; index++)
            Assert.That(renders[index].Result, Is.Not.Empty.And.All.EqualTo(expected[index % 2]));
    }

    [Test]
    public void AScopeDoesNotKeepItsProjectOrItsFontsAlive()
    {
        FontManager fonts = CreateManager();
        (WeakReference project, WeakReference scope, WeakReference face) = OpenAndCloseProject(fonts);

        for (int i = 0; i < 3 && (project.IsAlive || scope.IsAlive || face.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Multiple(() =>
        {
            Assert.That(project.IsAlive, Is.False);
            Assert.That(scope.IsAlive, Is.False);
            Assert.That(face.IsAlive, Is.False);
            Assert.That(fonts.IsRegistered(s_family), Is.False);
        });

        // A project at the same place loads its fonts again.
        Project reopened = new() { Uri = new Uri(Path.Combine(_root, "closed", "project.bep")) };
        Assert.That(FontRevision(fonts.ForProject(reopened)), Is.EqualTo(0x00010000));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference Project, WeakReference Scope, WeakReference Face) OpenAndCloseProject(FontManager fonts)
    {
        Project project = CreateProject("closed", 0x00010000);
        FontManager scope = fonts.ForProject(project);
        fonts.LoadProjectFonts(project);
        Assert.That(scope.TryResolveSkia(new Typeface(s_family), out SKTypeface face), Is.True);
        var references = (new WeakReference(project), new WeakReference(scope), new WeakReference(face));
        fonts.ClearProjectFonts(project);
        return references;
    }

    private static TextBlock CreateText()
        => new() { Text = { Value = "IIII" }, FontFamily = { Value = s_family }, Size = { Value = 32 } };

    private static IEnumerable<uint> LaidOutFontRevisions(TextBlock.Resource resource)
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
        WriteFont(directory, fontRevision);
        return new Project { Uri = new Uri(Path.Combine(directory, "project.bep")) };
    }

    private static void WriteFont(string projectDirectory, uint fontRevision)
    {
        byte[] font;
        using (Stream input = typeof(ProjectFontScopeTests).Assembly
                   .GetManifestResourceStream("Beutl.UnitTests.Assets.Font.BeutlTestVariable.ttf")!)
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            font = buffer.ToArray();
        }

        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(HeadOffset(font) + 4), fontRevision);
        string directory = Directory.CreateDirectory(Path.Combine(projectDirectory, "resources", "fonts")).FullName;
        File.WriteAllBytes(Path.Combine(directory, "font.ttf"), font);
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
