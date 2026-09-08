using System.Diagnostics;
using System.Xml.Linq;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class GplMitBoundaryContractTests
{
    [Test]
    public void Repository_projects_respect_the_ffmpeg_worker_boundary()
    {
        string repositoryRoot = FindRepositoryRoot();
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
                if (include is null
                    || !include.Contains("Beutl.FFmpegWorker", StringComparison.Ordinal))
                {
                    continue;
                }

                if (element.Name.LocalName == "Compile")
                {
                    if (!allowsWorkerSourceLinks)
                    {
                        violations.Add($"{relativePath}: source-links Beutl.FFmpegWorker");
                    }
                }
                else if (element.Name.LocalName == "ProjectReference")
                {
                    bool isBuildOrderOnlyAppReference = relativePath == "src/Beutl/Beutl.csproj"
                        && string.Equals(
                            element.Attribute("ReferenceOutputAssembly")?.Value,
                            "false",
                            StringComparison.OrdinalIgnoreCase);
                    if (!isBuildOrderOnlyAppReference)
                    {
                        violations.Add($"{relativePath}: references Beutl.FFmpegWorker");
                    }
                }
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "GPL/MIT boundary violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> EnumerateBuildFiles(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("-z");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("*.csproj");
        startInfo.ArgumentList.Add("*.props");
        startInfo.ArgumentList.Add("*.targets");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git to enumerate tracked build files.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not enumerate tracked build files (exit {process.ExitCode}): {error}");
        }

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)))
            .ToArray();
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
