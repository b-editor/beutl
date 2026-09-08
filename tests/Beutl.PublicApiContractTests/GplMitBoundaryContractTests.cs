using System.Xml.Linq;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class GplMitBoundaryContractTests
{
    [Test]
    public void Repository_projects_respect_the_ffmpeg_worker_boundary()
    {
        string repositoryRoot = FindRepositoryRoot();
        IReadOnlyList<string> violations = FindBoundaryViolations(repositoryRoot);

        Assert.That(
            violations,
            Is.Empty,
            "GPL/MIT boundary violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> FindBoundaryViolations(string repositoryRoot)
    {
        var violations = new List<string>();

        foreach (string file in EnumerateBuildFiles(repositoryRoot))
        {
            string relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
            if (relativePath.StartsWith("src/Beutl.FFmpegWorker/", StringComparison.Ordinal))
            {
                continue;
            }

            XDocument document = XDocument.Load(file);
            bool allowsWorkerSourceLinks = relativePath.StartsWith(
                    "tests/Beutl.FFmpegBenchmarks/",
                    StringComparison.Ordinal)
                || relativePath.StartsWith(
                    "tests/Beutl.FFmpegWorker.Tests/",
                    StringComparison.Ordinal);

            foreach (XElement element in document.Descendants())
            {
                string? include = element.Attribute("Include")?.Value;
                string? update = element.Attribute("Update")?.Value;
                string? itemSpec = include ?? update;
                if (itemSpec is null
                    || !itemSpec.Contains("Beutl.FFmpegWorker", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (element.Name.LocalName == "Compile")
                {
                    if (include is not null && !allowsWorkerSourceLinks)
                    {
                        violations.Add($"{relativePath}: source-links Beutl.FFmpegWorker");
                    }
                }
                else if (element.Name.LocalName == "ProjectReference")
                {
                    string? referenceOutputAssembly = GetMetadata(element, "ReferenceOutputAssembly");
                    if (update is not null)
                    {
                        if (referenceOutputAssembly is not null
                            && !string.Equals(
                                referenceOutputAssembly,
                                "false",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add(
                                $"{relativePath}: updates Beutl.FFmpegWorker into the compile closure");
                        }
                    }
                    else
                    {
                        bool isBuildOrderOnlyAppReference = relativePath == "src/Beutl/Beutl.csproj"
                            && string.Equals(
                                referenceOutputAssembly,
                                "false",
                                StringComparison.OrdinalIgnoreCase);
                        if (!isBuildOrderOnlyAppReference)
                        {
                            violations.Add($"{relativePath}: references Beutl.FFmpegWorker");
                        }
                    }
                }
            }
        }

        return violations;
    }

    private static string? GetMetadata(XElement element, string name)
    {
        return element.Attribute(name)?.Value
            ?? element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;
    }

    [Test]
    public void Build_file_enumeration_skips_nested_repositories_and_local_agent_state()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-enumeration-" + Guid.NewGuid().ToString("N"));

        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "src", "Project")).FullName;
            string nestedRepository = Directory.CreateDirectory(Path.Combine(testRoot, "nested")).FullName;
            string localAgentWorktree = Directory.CreateDirectory(
                Path.Combine(testRoot, ".claude", "worktrees", "nested")).FullName;
            File.WriteAllText(Path.Combine(projectDirectory, "Project.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(nestedRepository, ".git"), "gitdir: elsewhere");
            File.WriteAllText(Path.Combine(nestedRepository, "Nested.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(localAgentWorktree, "Agent.csproj"), "<Project />");

            string[] files = EnumerateBuildFiles(testRoot)
                .Select(path => Path.GetRelativePath(testRoot, path).Replace('\\', '/'))
                .ToArray();

            Assert.That(files, Is.EqualTo(new[] { "src/Project/Project.csproj" }));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    [Test]
    public void Boundary_scan_rejects_an_update_that_enables_the_worker_compile_reference()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-update-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Build.targets"),
                """
                <Project>
                  <ItemGroup>
                    <ProjectReference Update="src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj">
                      <ReferenceOutputAssembly>true</ReferenceOutputAssembly>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "Directory.Build.targets: updates Beutl.FFmpegWorker into the compile closure",
                }));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }

    private static IReadOnlyList<string> EnumerateBuildFiles(string repositoryRoot)
    {
        return EnumerateBuildFiles(new DirectoryInfo(repositoryRoot))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateBuildFiles(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            if (file.Extension is ".csproj" or ".props" or ".targets")
            {
                yield return file.FullName;
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (ShouldSkipDirectory(child)
                || IsRepositoryRoot(child))
            {
                continue;
            }

            foreach (string file in EnumerateBuildFiles(child))
            {
                yield return file;
            }
        }
    }

    private static bool ShouldSkipDirectory(DirectoryInfo directory)
    {
        return directory.Name is ".git" or ".agents" or ".claude" or ".codex"
            or "bin" or "obj" or "node_modules" or "TestResults"
            || directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool IsRepositoryRoot(DirectoryInfo directory)
    {
        return Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
    }
}
