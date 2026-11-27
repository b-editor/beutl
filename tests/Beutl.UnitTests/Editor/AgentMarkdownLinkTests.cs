using Beutl.Editor.Components.AgentTab;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

public sealed class AgentMarkdownLinkTests
{
    private const string Scene = "1b246aed-020b-411e-a8e6-eb4744395fbb";
    private const string Element = "9efe645e-8648-4eed-8888-bd54924e458e";
    private const string Object = "d7955a18-dcc1-4d97-afdb-de1840156163";

    [TestCase("12.5", 12.5)]
    [TestCase("12.5s", 12.5)]
    [TestCase("12", 12)]
    [TestCase("-2.5", -2.5)]
    [TestCase("01:02.5", 62.5)]
    [TestCase("01:02", 62)]
    [TestCase("00:01:02.500", 62.5)]
    [TestCase("01:01:02", 3662)]
    [TestCase("01%3A02.50", 62.5)]
    public void ParsesSceneRelativeSecondsAndTimecodes(string value, double seconds)
    {
        Assert.That(Parse($"beutl://time/{value}?scene={Scene}"), Is.EqualTo(new AgentMarkdownLink(
            AgentMarkdownLinkKind.Time, SceneId: Guid.Parse(Scene), Time: MediaTime.FromSeconds(seconds))));
    }

    [Test]
    public void ParsesElementObjectAndSceneTargets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Parse($"beutl://element/{Element}"), Is.EqualTo(new AgentMarkdownLink(AgentMarkdownLinkKind.Element, Guid.Parse(Element))));
            Assert.That(Parse($"beutl://element/{Element}?scene={Scene}&time=2.25s"), Is.EqualTo(new AgentMarkdownLink(
                AgentMarkdownLinkKind.Element, Guid.Parse(Element), Guid.Parse(Scene), Time: MediaTime.FromSeconds(2.25))));
            Assert.That(Parse($"beutl://object/{Object}?element={Element}&scene={Scene}&property=%57idth"), Is.EqualTo(new AgentMarkdownLink(
                AgentMarkdownLinkKind.Object, Guid.Parse(Object), Guid.Parse(Scene), Guid.Parse(Element), PropertyName: "Width")));
            Assert.That(Parse($"beutl://scene/{Scene}?time=00:01.1"), Is.EqualTo(new AgentMarkdownLink(
                AgentMarkdownLinkKind.Scene, SceneId: Guid.Parse(Scene), Time: MediaTime.FromSeconds(1.1))));
        });
    }

    [TestCase("javascript:alert(1)")]
    [TestCase("data:text/plain,abc")]
    [TestCase("https://example.test/document")]
    [TestCase("file://example.test/share/notes.txt")]
    [TestCase("file:////example.test/share/notes.txt")]
    [TestCase("//example.test/share/notes.txt")]
    [TestCase(@"\\example.test\share\notes.txt")]
    [TestCase("%2F%2Fexample.test/share/notes.txt")]
    [TestCase("#notes")]
    [TestCase("beutl://time/NaN")]
    [TestCase("beutl://time/Infinity")]
    [TestCase("beutl://time/01:99")]
    [TestCase("beutl://time/1,5")]
    [TestCase("beutl://time/1?scene=stale-query")]
    [TestCase("beutl://time/1?scene=" + Scene + "&scene=" + Scene)]
    [TestCase("beutl://time/1#fragment")]
    [TestCase("beutl://user@time/1")]
    [TestCase("beutl://element/not-a-guid")]
    [TestCase("beutl://scene/" + Scene + "?time=broken")]
    [TestCase("beutl://object/" + Object + "?element=nope")]
    public void RejectsUnsupportedOrMalformedDestinations(string destination)
        => Assert.That(Parse(destination), Is.Null);

    [Test]
    public void ResolvesLocalUrisAndProjectRelativeDestinations()
    {
        using var directory = new TemporaryProject();
        string file = Path.Combine(directory.Root, "media", "clip notes.txt");
        string absoluteUri = new Uri(file).AbsoluteUri;
        string bareAbsolute = file.Replace('\\', '/');
        string outsideFile = Path.Combine(Path.GetDirectoryName(directory.Root)!, "outside.txt");

        Assert.Multiple(() =>
        {
            Assert.That(directory.Resolve("media/clip%20notes.txt"), Is.EqualTo(new AgentMarkdownLocalPath(file, false)));
            Assert.That(directory.Resolve(absoluteUri), Is.EqualTo(new AgentMarkdownLocalPath(file, false)));
            Assert.That(directory.Resolve(file), Is.EqualTo(new AgentMarkdownLocalPath(file, false)));
            Assert.That(directory.Resolve(bareAbsolute), Is.EqualTo(new AgentMarkdownLocalPath(file, false)));
            Assert.That(directory.Resolve(outsideFile), Is.EqualTo(new AgentMarkdownLocalPath(outsideFile, false)));
            Assert.That(directory.Resolve("media/"), Is.EqualTo(new AgentMarkdownLocalPath(Path.Combine(directory.Root, "media") + Path.DirectorySeparatorChar, true))
                .Or.EqualTo(new AgentMarkdownLocalPath(Path.Combine(directory.Root, "media"), true)));
            Assert.That(directory.Resolve("missing.txt"), Is.Null);
            Assert.That(directory.Resolve("../outside.txt"), Is.Null);
            Assert.That(directory.Resolve("media/%2e%2e/%2e%2e/outside.txt"), Is.Null);
            Assert.That(Parse(absoluteUri + "?x=1"), Is.Null);
            Assert.That(Parse(absoluteUri + "#preview"), Is.Null);
            Assert.That(Parse(bareAbsolute + "?x=1"), Is.Null);
            Assert.That(Parse(bareAbsolute + "#preview"), Is.Null);
            if (OperatingSystem.IsWindows())
                Assert.That(Parse("/notes.txt"), Is.Null);
            else
                Assert.That(Parse("C:/notes.txt"), Is.Null);
        });
    }

    [Test]
    public void RejectsAProjectRelativeDirectoryLinkThatEscapesTheRoot()
    {
        using var directory = new TemporaryProject();
        string link = Path.Combine(directory.Root, "alias");
        SymbolicLinks.CreateOrIgnore(link, Path.GetDirectoryName(directory.Root)!, directory: true);
        try
        {
            Assert.That(directory.Resolve("alias/outside.txt"), Is.Null);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    private static AgentMarkdownLink? Parse(string destination)
        => AgentMarkdownLink.Parse(new Uri(destination, UriKind.RelativeOrAbsolute));

    private sealed class TemporaryProject : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("beutl-agent-links-");

        public TemporaryProject()
        {
            Directory.CreateDirectory(Path.Combine(Root, "media"));
            File.WriteAllText(Path.Combine(Root, "media", "clip notes.txt"), "inside the project");
            File.WriteAllText(Path.Combine(_directory.FullName, "outside.txt"), "outside the project");
        }

        public string Root => Path.Combine(_directory.FullName, "project");

        public AgentMarkdownLocalPath? Resolve(string destination)
            => Parse(destination)?.LocalPath is { } path ? AgentMarkdownLocalPath.Resolve(path, Root) : null;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
