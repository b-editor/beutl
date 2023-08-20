
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Xml.Linq;

partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter()]
    Configuration Configuration = Configuration.Release;

    [Parameter(Name = "--runtime")]
    DotNetRuntimeIdentifier Runtime = null;

    [Parameter(Name = "--self-contained")]
    bool SelfContained = false;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(GitVersion.SemVer)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target Publish => _ => _
        //.DependsOn(Compile)
        .DependsOn(Restore)
        .Executes(() =>
        {
            if (!IsSupportedRid(Runtime))
            {
                throw new NotSupportedException("This runtime is not yet supported.");
            }

            AbsolutePath mainProj = SourceDirectory / "Beutl" / "Beutl.csproj";
            var mainOutput = OutputDirectory / "Beutl";

            DotNetPublish(s => s
                .SetVersion(GitVersion.SemVer)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore()

                .When(Runtime != null, s => s.SetRuntime(Runtime).SetSelfContained(SelfContained))
                .SetConfiguration(Configuration)
                .SetProject(mainProj)
                .SetOutput(mainOutput)
                .SetProperty("NukePublish", true));

            string[] subProjects = new string[]
            {
                "Beutl.ExceptionHandler",
                "Beutl.PackageTools",
                "Beutl.WaitingDialog",
            };
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                DotNetPublish(s => s
                    .SetVersion(GitVersion.SemVer)
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion)
                    .EnableNoRestore()

                    .SetConfiguration(Configuration)
                    .SetProject(SourceDirectory / item / $"{item}.csproj")
                    .SetOutput(output));

                GlobFiles(output, $"**/{item}.*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => CopyFile(t.Source, t.Target));
            }

            // Eg: Beutl-main-0.0.0+0000.zip
            var fileName = new StringBuilder();
            fileName.Append("Beutl");
            if (Runtime != null)
            {
                fileName.Append('-');
                fileName.Append(Runtime.ToString());
            }
            if (SelfContained && Runtime != null)
            {
                fileName.Append("-sc");
            }

            fileName.Append('-');
            fileName.Append(GitVersion.EscapedBranchName);
            fileName.Append('-');
            fileName.Append(GitVersion.FullSemVer);
            fileName.Append(".zip");

            Compress(mainOutput, ArtifactsDirectory / fileName.ToString());
        });

    bool IsSupportedRid(DotNetRuntimeIdentifier rid)
    {
        return rid == DotNetRuntimeIdentifier.linux_x64
            || rid == DotNetRuntimeIdentifier.win_x64
            || rid == DotNetRuntimeIdentifier.win10_x64
            || rid == DotNetRuntimeIdentifier.win7_x64
            || rid == DotNetRuntimeIdentifier.win81_x64
            || rid == DotNetRuntimeIdentifier.osx_x64
            || rid == null;
    }
}
