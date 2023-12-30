
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Xml.Linq;

using static Nuke.Common.Tools.InnoSetup.InnoSetupTasks;
using Nuke.Common.Tools.NerdbankGitVersioning;

using Serilog;
using Nuke.Common.Tools.InnoSetup;

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

    [Parameter()]
    DotNetRuntimeIdentifier Runtime = null;

    [Parameter()]
    bool SelfContained = false;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    [NerdbankGitVersioning] readonly NerdbankGitVersioning NerdbankVersioning;


    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(p => p.DeleteDirectory());
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(p => p.DeleteDirectory());
            OutputDirectory.CreateOrCleanDirectory();
            ArtifactsDirectory.CreateOrCleanDirectory();
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
            AbsolutePath mainOutput = OutputDirectory / "Beutl";

            DotNetPublish(s => s
                .EnableNoRestore()
                .When(Runtime != null, s => s.SetRuntime(Runtime).SetSelfContained(SelfContained))
                .SetConfiguration(Configuration)
                .SetProject(mainProj)
                .SetOutput(mainOutput)
                .SetProperty("NukePublish", true));

            string[] subProjects =
            [
                "Beutl.ExceptionHandler",
                "Beutl.PackageTools",
                "Beutl.WaitingDialog",
            ];
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                DotNetPublish(s => s
                    .When(Runtime != null, s => s.SetRuntime(Runtime).SetSelfContained(SelfContained))
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetProject(SourceDirectory / item / $"{item}.csproj")
                    .SetOutput(output));

                output.GlobFiles($"**/{item}.*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => CopyFile(t.Source, t.Target));
            }

            string[] asmsToCopy =
            [
                "FluentTextTable",
                "Kokuban",
                "Kurukuru",
                "Sharprompt",
                "DeviceId",
            ];
            foreach (string asm in asmsToCopy)
            {
                foreach (string item in subProjects)
                {
                    AbsolutePath output = OutputDirectory / asm;
                    output.GlobFiles($"**/{asm}.*")
                        .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                        .ForEach(t => CopyFile(t.Source, t.Target));
                }
            }
        });

    Target Zip => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            AbsolutePath mainOutput = OutputDirectory / "Beutl";

            // Eg: Beutl-0.0.0+0000.zip
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
            fileName.Append(NerdbankVersioning.SemVer2);
            fileName.Append(".zip");

            mainOutput.CompressTo(ArtifactsDirectory / fileName.ToString());
        });

    Target BuildInstaller => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            InnoSetup(c => c
                .SetKeyValueDefinition("MyAppVersion", NerdbankVersioning.AssemblyFileVersion)
                .SetKeyValueDefinition("MyOutputDir", ArtifactsDirectory)
                .SetKeyValueDefinition("MyLicenseFile", RootDirectory / "LICENSE")
                .SetKeyValueDefinition("MySetupIconFile", RootDirectory / "assets/logos/logo.ico")
                .SetKeyValueDefinition("MySource", OutputDirectory / "Beutl")
                .SetScriptFile(RootDirectory / "nukebuild/beutl-setup.iss"));
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
