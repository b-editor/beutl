using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class GplMitBoundaryContractTests
{
    private static readonly Regex s_propertyReference = new(
        @"\$\(([^)]+)\)",
        RegexOptions.CultureInvariant);

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
                string elementName = element.Name.LocalName;
                if (elementName is not ("Compile" or "ProjectReference"))
                {
                    continue;
                }

                string? include = element.Attribute("Include")?.Value;
                string? update = element.Attribute("Update")?.Value;
                string? itemSpec = include ?? update;
                if (itemSpec is null)
                {
                    continue;
                }

                string resolvedItemSpec = ResolveItemSpec(document, file, itemSpec);
                if (elementName == "ProjectReference" && IsDynamicItemSpec(resolvedItemSpec))
                {
                    violations.Add(
                        $"{relativePath}: contains an unresolved dynamic ProjectReference");
                    continue;
                }

                if (!resolvedItemSpec.Contains(
                    "Beutl.FFmpegWorker",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (elementName == "Compile")
                {
                    if (include is not null && !allowsWorkerSourceLinks)
                    {
                        violations.Add($"{relativePath}: source-links Beutl.FFmpegWorker");
                    }
                }
                else
                {
                    if (update is not null)
                    {
                        if (!HasSafeBuildOrderOnlyMetadata(element))
                        {
                            violations.Add(
                                $"{relativePath}: updates Beutl.FFmpegWorker into the compile closure");
                        }
                    }
                    else
                    {
                        bool isBuildOrderOnlyAppReference = relativePath == "src/Beutl/Beutl.csproj"
                            && HasSafeBuildOrderOnlyMetadata(element);
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

    private static string ResolveItemSpec(XDocument document, string file, string itemSpec)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildThisFileDirectory"] = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar,
        };
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement propertyGroup in document.Descendants()
                     .Where(element => element.Name.LocalName == "PropertyGroup"
                         && element.Attribute("Condition") is null))
        {
            foreach (XElement property in propertyGroup.Elements()
                         .Where(element => element.Attribute("Condition") is null))
            {
                string name = property.Name.LocalName;
                if (ambiguous.Contains(name))
                {
                    continue;
                }

                if (!properties.TryAdd(name, property.Value))
                {
                    properties.Remove(name);
                    ambiguous.Add(name);
                }
            }
        }

        string resolved = itemSpec;
        for (int i = 0; i < 16; i++)
        {
            string next = s_propertyReference.Replace(
                resolved,
                match => properties.TryGetValue(match.Groups[1].Value, out string? value)
                    ? value
                    : match.Value);
            if (next == resolved)
            {
                break;
            }

            resolved = next;
        }

        return resolved;
    }

    private static bool IsDynamicItemSpec(string itemSpec)
    {
        return itemSpec.Contains("$(", StringComparison.Ordinal)
            || itemSpec.Contains("@(", StringComparison.Ordinal)
            || itemSpec.Contains("%(", StringComparison.Ordinal)
            || itemSpec.Contains('*')
            || itemSpec.Contains('?');
    }

    private static bool HasSafeBuildOrderOnlyMetadata(XElement element)
    {
        const string metadataName = "ReferenceOutputAssembly";
        if (MetadataListContains(element.Attribute("RemoveMetadata")?.Value, metadataName))
        {
            return false;
        }

        string? keepMetadata = element.Attribute("KeepMetadata")?.Value;
        if (keepMetadata is not null && !MetadataListContains(keepMetadata, metadataName))
        {
            return false;
        }

        string? attributeValue = element.Attribute(metadataName)?.Value;
        if (attributeValue is not null)
        {
            return string.Equals(attributeValue, "false", StringComparison.OrdinalIgnoreCase);
        }

        XElement[] childMetadata = element.Elements()
            .Where(child => child.Name.LocalName == metadataName)
            .ToArray();
        return childMetadata.Length == 1
            && childMetadata[0].Attribute("Condition") is null
            && string.Equals(
                childMetadata[0].Value,
                "false",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool MetadataListContains(string? value, string metadataName)
    {
        return value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(metadataName, StringComparer.OrdinalIgnoreCase) == true;
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

    [Test]
    public void Boundary_scan_rejects_an_update_that_removes_build_order_only_metadata()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-remove-metadata-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Build.targets"),
                """
                <Project>
                  <ItemGroup>
                    <ProjectReference Update="src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj"
                                      RemoveMetadata="ReferenceOutputAssembly" />
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

    [Test]
    public void Boundary_scan_resolves_property_backed_worker_references()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-property-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Project.csproj"),
                """
                <Project>
                  <PropertyGroup>
                    <WorkerProject>src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj</WorkerProject>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="$(WorkerProject)" />
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[] { "Project.csproj: references Beutl.FFmpegWorker" }));
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
    public void Boundary_scan_rejects_conditioned_build_order_only_metadata()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-conditioned-metadata-" + Guid.NewGuid().ToString("N"));

        try
        {
            string appDirectory = Directory.CreateDirectory(Path.Combine(testRoot, "src", "Beutl")).FullName;
            File.WriteAllText(
                Path.Combine(appDirectory, "Beutl.csproj"),
                """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="../Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj">
                      <ReferenceOutputAssembly Condition="'$(Configuration)' == 'Debug'">false</ReferenceOutputAssembly>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[] { "src/Beutl/Beutl.csproj: references Beutl.FFmpegWorker" }));
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
