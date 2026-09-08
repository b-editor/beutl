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
            if (string.Equals(
                relativePath,
                "src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument document = XDocument.Load(file);
            bool isWorkerSideSharedBuildFile = relativePath.StartsWith(
                    "src/Beutl.FFmpegWorker/",
                    StringComparison.OrdinalIgnoreCase)
                && IsSharedBuildFile(relativePath);
            bool allowsWorkerSourceLinks = relativePath.StartsWith(
                    "tests/Beutl.FFmpegBenchmarks/",
                    StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith(
                    "tests/Beutl.FFmpegWorker.Tests/",
                    StringComparison.OrdinalIgnoreCase);

            foreach (XElement element in document.Descendants())
            {
                string elementName = element.Name.LocalName;
                bool isCompile = string.Equals(elementName, "Compile", StringComparison.OrdinalIgnoreCase);
                bool isProjectReference = string.Equals(
                    elementName,
                    "ProjectReference",
                    StringComparison.OrdinalIgnoreCase);
                bool isAssemblyReference = string.Equals(
                    elementName,
                    "Reference",
                    StringComparison.OrdinalIgnoreCase);
                if (!isCompile && !isProjectReference && !isAssemblyReference)
                {
                    continue;
                }

                string? include = GetAttributeValue(element, "Include");
                string? update = GetAttributeValue(element, "Update");
                string? itemSpec = include ?? update;
                if (itemSpec is null)
                {
                    continue;
                }

                string resolvedItemSpec = ResolveItemSpec(document, file, itemSpec);
                if (IsDynamicItemSpec(resolvedItemSpec) && !isAssemblyReference)
                {
                    if (isProjectReference)
                    {
                        violations.Add(
                            $"{relativePath}: contains an unresolved dynamic ProjectReference");
                    }
                    else if (include is not null)
                    {
                        violations.Add(
                            $"{relativePath}: contains an unresolved dynamic Compile item");
                    }

                    continue;
                }

                if (isCompile
                    && include is not null
                    && isWorkerSideSharedBuildFile)
                {
                    violations.Add(
                        $"{relativePath}: exposes worker Compile items from a shared build file");
                    continue;
                }

                if (isAssemblyReference)
                {
                    bool referencesWorker = resolvedItemSpec.Contains(
                        "Beutl.FFmpegWorker",
                        StringComparison.OrdinalIgnoreCase);
                    foreach (string hintPath in EnumerateReferenceHintPaths(document, element))
                    {
                        string resolvedHintPath = ResolveItemSpec(document, file, hintPath);
                        referencesWorker |= resolvedHintPath.Contains(
                            "Beutl.FFmpegWorker",
                            StringComparison.OrdinalIgnoreCase);
                    }

                    if (referencesWorker)
                    {
                        violations.Add(
                            $"{relativePath}: directly references the Beutl.FFmpegWorker assembly");
                    }

                    continue;
                }

                if (!resolvedItemSpec.Contains(
                    "Beutl.FFmpegWorker",
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (isCompile)
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
                        bool isBuildOrderOnlyAppReference = string.Equals(
                                relativePath,
                                "src/Beutl/Beutl.csproj",
                                StringComparison.OrdinalIgnoreCase)
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

    private static IEnumerable<string> EnumerateReferenceHintPaths(
        XDocument document,
        XElement reference)
    {
        string? attributeHintPath = GetAttributeValue(reference, "HintPath");
        if (attributeHintPath is not null)
        {
            yield return attributeHintPath;
        }

        foreach (XElement hintPath in reference.Elements().Where(IsHintPath))
        {
            yield return hintPath.Value;
        }

        foreach (XElement itemDefinition in document.Descendants()
                     .Where(element => string.Equals(
                             element.Name.LocalName,
                             "Reference",
                             StringComparison.OrdinalIgnoreCase)
                         && string.Equals(
                             element.Parent?.Name.LocalName,
                             "ItemDefinitionGroup",
                             StringComparison.OrdinalIgnoreCase)))
        {
            string? defaultAttributeHintPath = GetAttributeValue(itemDefinition, "HintPath");
            if (defaultAttributeHintPath is not null)
            {
                yield return defaultAttributeHintPath;
            }

            foreach (XElement hintPath in itemDefinition.Elements().Where(IsHintPath))
            {
                yield return hintPath.Value;
            }
        }
    }

    private static bool IsHintPath(XElement element)
    {
        return string.Equals(
            element.Name.LocalName,
            "HintPath",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveItemSpec(XDocument document, string file, string itemSpec)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildThisFileDirectory"] = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar,
        };
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (XElement propertyGroup in document.Descendants()
                     .Where(element => string.Equals(
                         element.Name.LocalName,
                         "PropertyGroup",
                         StringComparison.OrdinalIgnoreCase)))
        {
            bool conditionalGroup = GetAttributeValue(propertyGroup, "Condition") is not null
                || propertyGroup.Ancestors().Any(element =>
                    string.Equals(element.Name.LocalName, "When", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        element.Name.LocalName,
                        "Otherwise",
                        StringComparison.OrdinalIgnoreCase));
            foreach (XElement property in propertyGroup.Elements())
            {
                string name = property.Name.LocalName;
                if (conditionalGroup || GetAttributeValue(property, "Condition") is not null)
                {
                    properties.Remove(name);
                    ambiguous.Add(name);
                    continue;
                }

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
        if (MetadataListContains(GetAttributeValue(element, "RemoveMetadata"), metadataName))
        {
            return false;
        }

        string? keepMetadata = GetAttributeValue(element, "KeepMetadata");
        if (keepMetadata is not null && !MetadataListContains(keepMetadata, metadataName))
        {
            return false;
        }

        string? attributeValue = GetAttributeValue(element, metadataName);
        if (attributeValue is not null)
        {
            return string.Equals(attributeValue, "false", StringComparison.OrdinalIgnoreCase);
        }

        XElement[] childMetadata = element.Elements()
            .Where(child => string.Equals(
                child.Name.LocalName,
                metadataName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return childMetadata.Length == 1
            && GetAttributeValue(childMetadata[0], "Condition") is null
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

    private static string? GetAttributeValue(XElement element, string name)
    {
        return element.Attributes()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Name.LocalName,
                name,
                StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static bool IsSharedBuildFile(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".props", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".targets", StringComparison.OrdinalIgnoreCase);
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
    public void Build_file_enumeration_includes_declared_submodules()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-submodule-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, ".gitmodules"),
                """
                [submodule "external/Tracked"]
                    path = external/Tracked
                    url = https://example.invalid/tracked.git
                """);
            string submoduleRoot = Directory.CreateDirectory(
                Path.Combine(testRoot, "external", "Tracked")).FullName;
            File.WriteAllText(Path.Combine(submoduleRoot, ".git"), "gitdir: elsewhere");
            File.WriteAllText(Path.Combine(submoduleRoot, "Tracked.csproj"), "<Project />");

            string[] files = EnumerateBuildFiles(testRoot)
                .Select(path => Path.GetRelativePath(testRoot, path).Replace('\\', '/'))
                .ToArray();

            Assert.That(files, Is.EqualTo(new[] { "external/Tracked/Tracked.csproj" }));
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
    public void Boundary_scan_rejects_conditionally_overridden_properties()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-conditional-property-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Project.csproj"),
                """
                <Project>
                  <PropertyGroup>
                    <WorkerProject>Safe.csproj</WorkerProject>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
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
                Is.EqualTo(new[]
                {
                    "Project.csproj: contains an unresolved dynamic ProjectReference",
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

    [Test]
    public void Boundary_scan_rejects_compile_items_backed_by_external_properties()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-compile-property-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Directory.Build.props"),
                """
                <Project>
                  <PropertyGroup>
                    <WorkerSource>src/Beutl.FFmpegWorker/WorkerHost.cs</WorkerSource>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(testRoot, "Project.csproj"),
                """
                <Project>
                  <ItemGroup>
                    <Compile Include="$(WorkerSource)" />
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "Project.csproj: contains an unresolved dynamic Compile item",
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
    public void Boundary_scan_rejects_compile_items_in_worker_side_shared_build_files()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-worker-import-" + Guid.NewGuid().ToString("N"));

        try
        {
            string workerDirectory = Directory.CreateDirectory(
                Path.Combine(testRoot, "src", "Beutl.FFmpegWorker")).FullName;
            File.WriteAllText(
                Path.Combine(workerDirectory, "WorkerItems.targets"),
                """
                <Project>
                  <ItemGroup>
                    <Compile Include="WorkerHost.cs" />
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "src/Beutl.FFmpegWorker/WorkerItems.targets: exposes worker Compile items from a shared build file",
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
    public void Boundary_scan_recognizes_mixed_case_item_names_and_build_extensions()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-mixed-case-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "WorkerItems.Props"),
                """
                <Project>
                  <ItemGroup>
                    <projectreference include="src/Beutl.FFmpegWorker/Beutl.FFmpegWorker.csproj" />
                    <compile include="src/Beutl.FFmpegWorker/WorkerHost.cs" />
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "WorkerItems.Props: references Beutl.FFmpegWorker",
                    "WorkerItems.Props: source-links Beutl.FFmpegWorker",
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
    public void Boundary_scan_rejects_direct_worker_assembly_references()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-assembly-reference-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Project.csproj"),
                """
                <Project>
                  <ItemGroup>
                    <Reference Include="WorkerAlias">
                      <HintPath>lib/Beutl.FFmpegWorker.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "Project.csproj: directly references the Beutl.FFmpegWorker assembly",
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
    public void Boundary_scan_reads_attribute_and_item_definition_hint_paths()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-effective-hint-path-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Attribute.csproj"),
                """
                <Project>
                  <ItemGroup>
                    <Reference Include="WorkerAlias"
                               HintPath="lib/Beutl.FFmpegWorker.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(testRoot, "ItemDefinition.csproj"),
                """
                <Project>
                  <ItemDefinitionGroup>
                    <Reference>
                      <HintPath>lib/Beutl.FFmpegWorker.dll</HintPath>
                    </Reference>
                  </ItemDefinitionGroup>
                  <ItemGroup>
                    <Reference Include="InheritedWorkerAlias" />
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(
                violations,
                Is.EqualTo(new[]
                {
                    "Attribute.csproj: directly references the Beutl.FFmpegWorker assembly",
                    "ItemDefinition.csproj: directly references the Beutl.FFmpegWorker assembly",
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
    public void Boundary_scan_allows_unrelated_dynamic_assembly_hint_paths()
    {
        string testRoot = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "gpl-boundary-unrelated-reference-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(
                Path.Combine(testRoot, "Project.csproj"),
                """
                <Project>
                  <ItemGroup>
                    <Reference Include="Other.Library">
                      <HintPath>$(NuGetPackageRoot)other.library/lib/Other.Library.dll</HintPath>
                    </Reference>
                  </ItemGroup>
                </Project>
                """);

            IReadOnlyList<string> violations = FindBoundaryViolations(testRoot);

            Assert.That(violations, Is.Empty);
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
        HashSet<string> submoduleRoots = FindSubmoduleRoots(repositoryRoot);
        return EnumerateBuildFiles(new DirectoryInfo(repositoryRoot), submoduleRoots)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateBuildFiles(
        DirectoryInfo directory,
        IReadOnlySet<string> submoduleRoots)
    {
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            if (string.Equals(file.Extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Extension, ".props", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Extension, ".targets", StringComparison.OrdinalIgnoreCase))
            {
                yield return file.FullName;
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (ShouldSkipDirectory(child)
                || (IsRepositoryRoot(child) && !submoduleRoots.Contains(child.FullName)))
            {
                continue;
            }

            foreach (string file in EnumerateBuildFiles(child, submoduleRoots))
            {
                yield return file;
            }
        }
    }

    private static HashSet<string> FindSubmoduleRoots(string repositoryRoot)
    {
        string boundaryRoot = Path.GetFullPath(repositoryRoot);
        var result = new HashSet<string>(PathComparer());
        var pending = new Queue<string>();
        pending.Enqueue(boundaryRoot);

        while (pending.TryDequeue(out string? currentRoot))
        {
            string modulesPath = Path.Combine(currentRoot, ".gitmodules");
            if (!File.Exists(modulesPath))
            {
                continue;
            }

            foreach (string line in File.ReadLines(modulesPath))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separator = trimmed.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                string relativePath = trimmed[(separator + 1)..].Trim();
                string fullPath = Path.GetFullPath(Path.Combine(currentRoot, relativePath));
                if (IsWithinDirectory(fullPath, boundaryRoot)
                    && Directory.Exists(fullPath)
                    && result.Add(fullPath))
                {
                    pending.Enqueue(fullPath);
                }
            }
        }

        return result;
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(normalizedDirectory, comparison);
    }

    private static StringComparer PathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
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
