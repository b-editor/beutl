namespace Beutl.HeadlessUITests;

// Keeps FormatManifest.txt in step with what first-party code writes. FormatManifest says what the file lists, and
// tests/README.md when and how to regenerate it.
[TestFixture]
public sealed class FormatManifestTests
{
    private const string UpdateVariable = "BEUTL_UPDATE_FORMAT_MANIFEST";
    private const int ReportedEntries = 100;

    [Test]
    public void TheManifestMatchesWhatFirstPartyCodeWrites()
    {
#if WITHOUT_MEDIA_FOUNDATION
        Assert.Ignore(
            "This Windows ARM64 build leaves out Beutl.Extensions.MediaFoundation, as the app does, because the decoder "
            + "builds as x64. The manifest lists its types, so it is compared and regenerated on other platforms.");
#endif
        string path = Path.Combine(FindRepositoryRoot(), "tests", "Beutl.HeadlessUITests", "FormatManifest.txt");
        string actual = FormatManifest.Build();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual);
            Assert.Pass($"Wrote {path}. Review the diff and commit it with the change.");
        }

        string expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        if (actual == expected)
            return;

        // An entry is a line and the indented lines below it. Within a section the entries are unique and sorted,
        // so the entries only one side has are the change.
        List<string> expectedEntries = Entries(expected);
        List<string> actualEntries = Entries(actual);
        string[] changes =
        [
            .. Except(expectedEntries, actualEntries).Select(entry => Prefix(entry, "- ")),
            .. Except(actualEntries, expectedEntries).Select(entry => Prefix(entry, "+ ")),
        ];
        string report = changes.Length == 0
            ? "(The same entries in another order.)"
            : string.Join('\n', changes.Take(ReportedEntries));
        if (changes.Length > ReportedEntries)
            report += $"\n... and {changes.Length - ReportedEntries} more entries";

        Assert.Fail(
            $"""
             What first-party code writes no longer matches {path}.
             A rename, or a change to a type's keys or to whether it is sealed, changes the format: what was saved under
             the old names no longer resolves. If the change is intended, regenerate the file and commit it with the change:

               {UpdateVariable}=1 dotnet test tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj -f net10.0 --filter "FullyQualifiedName~FormatManifestTests"

             Lines marked - are in the file, and lines marked + are what the code writes now:

             {report}
             """);
    }

    [Test]
    public void EveryProjectUnderSrcIsReadOrLeftOutWithAReason()
    {
        // Every project file below src/ at any depth, leaving out the build output in bin and obj.
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string[] projects = Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(src, file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(directory => directory is "bin" or "obj"))
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] classified = FormatManifest.ScannedProjects.Keys
            .Concat(FormatManifest.SkippedProjects.Keys)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.That(classified, Is.EqualTo(projects),
            "A project under src/ can declare types that are persisted: add it to FormatManifest.ScannedProjects, "
            + "or to SkippedProjects with the reason the manifest leaves it out.");
    }

    private static List<string> Entries(string text)
    {
        var entries = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("  ", StringComparison.Ordinal) && entries.Count > 0)
                entries[^1] += "\n" + line;
            else
                entries.Add(line);
        }

        return entries;
    }

    // The entries of first that second does not have, counting repeated ones such as blank lines.
    private static IEnumerable<string> Except(List<string> first, List<string> second)
    {
        var remaining = second.CountBy(entry => entry).ToDictionary();
        foreach (string entry in first)
        {
            if (remaining.TryGetValue(entry, out int count) && count > 0)
                remaining[entry] = count - 1;
            else
                yield return entry;
        }
    }

    private static string Prefix(string entry, string prefix)
    {
        return string.Join('\n', entry.Split('\n').Select(line => prefix + line));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
    }
}
