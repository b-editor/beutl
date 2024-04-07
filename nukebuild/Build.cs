
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
    RuntimeIdentifier Runtime = null;

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

    private string GetTFM()
    {
        AbsolutePath mainProj = SourceDirectory / "Beutl" / "Beutl.csproj";
        using IProcess proc = StartProcess(DotNetPath, $"msbuild --getProperty:TargetFrameworks {mainProj}");
        proc.WaitForExit();
        return proc.Output.First().Text.Split(';')[0];
    }

    Target Publish => _ => _
        //.DependsOn(Compile)
        .DependsOn(Restore)
        .Executes(() =>
        {
            AbsolutePath mainProj = SourceDirectory / "Beutl" / "Beutl.csproj";
            AbsolutePath mainOutput = OutputDirectory / "Beutl";

            string tfm = GetTFM();

            DotNetPublish(s => s
                .EnableNoRestore()
                .When(Runtime != null, s => s.SetRuntime(Runtime).SetSelfContained(SelfContained))
                .When(Runtime == RuntimeIdentifier.win_x64, s => s.SetFramework($"{tfm}-windows"))
                .When(Runtime != RuntimeIdentifier.win_x64, s => s.SetFramework(tfm))
                .SetConfiguration(Configuration)
                .SetProject(mainProj)
                .SetOutput(mainOutput)
                .SetProperty("NukePublish", true));

            string[] subProjects =
            [
                "Beutl.ExceptionHandler",
                "Beutl.PackageTools",
                "Beutl.PackageTools.UI",
                "Beutl.WaitingDialog",
            ];
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                DotNetPublish(s => s
                    .When(Runtime != null, s => s.SetRuntime(Runtime).SetSelfContained(SelfContained))
                    .When(Runtime == RuntimeIdentifier.win_x64, s => s.SetFramework($"{tfm}-windows"))
                    .When(Runtime != RuntimeIdentifier.win_x64, s => s.SetFramework(tfm))
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetProject(SourceDirectory / item / $"{item}.csproj")
                    .SetOutput(output));

                output.GlobFiles($"**/{item}*")
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

    Target BundleApp => _ => _
        .Executes(() =>
        {
            // dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:TargetFramework=net8.0 -p:UseAppHost=true -p:SelfContained=true
            AbsolutePath directory = SourceDirectory / "Beutl";
            AbsolutePath output = OutputDirectory / "AppBundle";
            string tfm = GetTFM();
            DotNetRestore(s => s.SetProjectFile(directory / "Beutl.csproj"));
            DotNetMSBuild(s => s
                .SetProcessWorkingDirectory(directory)
                .SetTargets("BundleApp")
                .SetConfiguration(Configuration)
                .SetProperty("PublishDir", output)
                .SetProperty("CFBundleVersion", NerdbankVersioning.AssemblyFileVersion)
                .SetProperty("CFBundleShortVersionString", NerdbankVersioning.AssemblyFileVersion)
                .SetProperty("RuntimeIdentifier", Runtime.ToString())
                .SetProperty("TargetFramework", tfm)
                .SetProperty("UseAppHost", true)
                .SetProperty("SelfContained", true));
        });

    Target NuGetPack => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            string[] projects =
            [
                "Beutl.Configuration",
                "Beutl.Core",
                "Beutl.Extensibility",
                "Beutl.Engine",
                "Beutl.Language",
                "Beutl.Operators",
                "Beutl.ProjectSystem",
                "Beutl.Threading",
                "Beutl.Utilities",
            ];

            string tfm = GetTFM();
            foreach (string proj in projects)
            {
                DotNetBuild(s => s
                    .EnableNoRestore()
                    .SetFramework(tfm)
                    .SetConfiguration(Configuration)
                    .SetProjectFile(SourceDirectory / proj / $"{proj}.csproj"));

                DotNetPack(s => s
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetProject(SourceDirectory / proj / $"{proj}.csproj"));
            }
        });
}
