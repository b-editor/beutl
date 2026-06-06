using System.Text;
using Nuke.Common.Tools.InnoSetup;
using static Nuke.Common.Tools.InnoSetup.InnoSetupTasks;

class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter] Configuration Configuration = Configuration.Release;

    [Parameter] RuntimeIdentifier Runtime = null;

    [Parameter] bool SelfContained = false;

    [Parameter] string Version = "1.0.0";

    [Parameter] string AssemblyVersion = "1.0.0.0";

    [Parameter] string InformationalVersion = "1.0.0.0";

    [GitRepository] readonly GitRepository GitRepository;


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
            DotNetRestore();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
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
                .When(_ => Runtime != null, s => s
                    .SetRuntime(Runtime)
                    .SetSelfContained(SelfContained))
                .When(_ => Runtime?.IsWindows == true, s => s.SetFramework($"{tfm}-windows"))
                .When(_ => Runtime?.IsWindows != true, s => s.SetFramework(tfm))
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetProject(mainProj)
                .SetOutput(mainOutput)
                .SetProperty("NukePublish", true));

            string[] subProjects =
            [
                "Beutl.ExceptionHandler",
                "Beutl.PackageTools.UI",
                "Beutl.WaitingDialog",
            ];
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                DotNetPublish(s => s
                    .When(_ => Runtime != null, s => s
                        .SetRuntime(Runtime)
                        .SetSelfContained(SelfContained))
                    .When(_ => Runtime?.IsWindows == true, s => s.SetFramework($"{tfm}-windows"))
                    .When(_ => Runtime?.IsWindows != true, s => s.SetFramework(tfm))
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetProject(SourceDirectory / item / $"{item}.csproj")
                    .SetOutput(output));

                output.GlobFiles($"**/{item}*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => t.Source.Copy(t.Target));
            }

            // The 3 sub-exes above flat-copy only their own Beutl.<Name>* files because the rest of
            // their closure is a subset of the app's shared assemblies. The GPL worker is the
            // exception: it carries private deps (FFmpeg.AutoGen, FFmpegSharp) absent from the app's
            // MIT set, so copying only Beutl.FFmpegWorker* strands them and the worker dies with a
            // FileNotFoundException at startup. Copy its whole closure with FileSkip instead — the
            // app's shared assemblies/runtime stay the single canonical set (skipped), and only the
            // worker's identity + private deps are added.
            // `dotnet publish --output` does not clear the dir first, so clean it to keep stale
            // artifacts from an earlier RID/config out of the FileSkip copy.
            AbsolutePath workerOutput = OutputDirectory / "Beutl.FFmpegWorker";
            workerOutput.CreateOrCleanDirectory();
            DotNetPublish(s => s
                .When(_ => Runtime != null, s => s
                    .SetRuntime(Runtime)
                    .SetSelfContained(SelfContained))
                .When(_ => Runtime == RuntimeIdentifier.win_x64, s => s.SetFramework($"{tfm}-windows"))
                .When(_ => Runtime != RuntimeIdentifier.win_x64, s => s.SetFramework(tfm))
                .EnableNoRestore()
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetProject(SourceDirectory / "Beutl.FFmpegWorker" / "Beutl.FFmpegWorker.csproj")
                .SetOutput(workerOutput));
            workerOutput.GlobFiles("**/*")
                .Select(p => (Source: p, Target: mainOutput / workerOutput.GetRelativePathTo(p)))
                .ForEach(t => t.Source.Copy(t.Target, ExistsPolicy.FileSkip));
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
                fileName.Append("-standalone");
            }

            fileName.Append('-');
            fileName.Append(Version);
            fileName.Append(".zip");

            mainOutput.CompressTo(ArtifactsDirectory / fileName.ToString());
        });

    Target BuildInstaller => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            // win-arm64 installers get an "-arm64" suffix and the arm64 Inno Setup
            // architecture flags; win-x64 keeps its historical, suffix-less filename.
            bool isArm64 = Runtime == RuntimeIdentifier.win_arm64;
            string archSuffix = isArm64 ? "-arm64" : "";
            string architectures = isArm64 ? "arm64" : "x64compatible";

            InnoSetup(c => c
                .SetKeyValueDefinition("MyAppVersion", AssemblyVersion)
                .SetKeyValueDefinition("MyOutputDir", ArtifactsDirectory)
                .SetKeyValueDefinition("MyLicenseFile", RootDirectory / "LICENSE")
                .SetKeyValueDefinition("MyGPLLicenseFile", RootDirectory / "LICENSE.GPL")
                .SetKeyValueDefinition("MySetupIconFile", RootDirectory / "assets/logos/logo.ico")
                .SetKeyValueDefinition("MySource", OutputDirectory / "Beutl")
                .SetKeyValueDefinition("MyOutputBaseFilename", $"beutl{(SelfContained ? "-standalone" : "")}{archSuffix}-setup")
                .SetKeyValueDefinition("MyArchitecturesAllowed", architectures)
                .SetKeyValueDefinition("MyArchitecturesInstallIn64BitMode", architectures)
                .SetScriptFile(RootDirectory / "nukebuild/beutl-setup.iss"));
        });

    Target BundleApp => _ => _
        .Executes(() =>
        {
            // dotnet msbuild -t:BundleApp -p:RuntimeIdentifier=osx-arm64 -p:TargetFramework=net9.0 -p:UseAppHost=true -p:SelfContained=true
            AbsolutePath directory = SourceDirectory / "Beutl";
            AbsolutePath output = OutputDirectory / "AppBundle";
            string tfm = GetTFM();
            DotNetRestore(s => s.SetProjectFile(directory / "Beutl.csproj"));
            DotNetMSBuild(s => s
                .SetProcessWorkingDirectory(directory)
                .SetTargets("BundleApp")
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetProperty("PublishDir", output)
                .SetProperty("CFBundleVersion", AssemblyVersion)
                .SetProperty("CFBundleShortVersionString", AssemblyVersion)
                .SetProperty("RuntimeIdentifier", Runtime.ToString())
                .SetProperty("TargetFramework", tfm)
                .SetProperty("UseAppHost", true)
                .SetProperty("SelfContained", true));

            // The worker carries private deps (FFmpeg.AutoGen, FFmpegSharp) absent from the app's MIT
            // set, so the BundleApp MSBuild target — which drops only a partial flat worker set
            // (apphost + deps.json, no managed dll) via the dev ProjectReference — leaves it unable to
            // start. Delete that partial set and merge the worker's self-contained closure with
            // FileSkip: the app's shared assemblies/runtime stay canonical (skipped), and the worker's
            // identity + private deps are added flat. Runs before the workflow's codesign step.
            AbsolutePath workerProj = SourceDirectory / "Beutl.FFmpegWorker" / "Beutl.FFmpegWorker.csproj";
            // `dotnet publish --output` does not clear the dir first, so clean it to keep stale
            // artifacts from an earlier RID/config out of the FileSkip copy.
            AbsolutePath workerOutput = OutputDirectory / "Beutl.FFmpegWorker";
            workerOutput.CreateOrCleanDirectory();
            DotNetPublish(s => s
                .SetRuntime(Runtime)
                .SetSelfContained(true)
                .SetFramework(tfm)
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetProject(workerProj)
                .SetOutput(workerOutput));

            AbsolutePath bundleContents = output / "Beutl.app" / "Contents" / "MacOS";
            bundleContents.GlobFiles("Beutl.FFmpegWorker*").ForEach(p => p.DeleteFile());
            workerOutput.GlobFiles("**/*")
                .Select(p => (Source: p, Target: bundleContents / workerOutput.GetRelativePathTo(p)))
                .ForEach(t => t.Source.Copy(t.Target, ExistsPolicy.FileSkip));
        });

    Target NuGetPack => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            string tfm = GetTFM();
            (string Name, string TFM)[] projects =
            [
                ("Beutl.Configuration", tfm),
                ("Beutl.Core", tfm),
                ("Beutl.Extensibility", tfm),
                ("Beutl.Engine", tfm),
                ("Beutl.Engine.SourceGenerators", "netstandard2.0"),
                ("Beutl.Language", tfm),
                ("Beutl.ProjectSystem", tfm),
                ("Beutl.Threading", tfm),
                ("Beutl.Utilities", tfm),
                ("Beutl.NodeGraph", tfm),
                ("Beutl.Editor", tfm),
            ];

            foreach (var (name, projectTfm) in projects)
            {
                DotNetBuild(s => s
                    .EnableNoRestore()
                    .SetFramework(projectTfm)
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetProjectFile(SourceDirectory / name / $"{name}.csproj"));

                DotNetPack(s => s
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetProject(SourceDirectory / name / $"{name}.csproj"));
            }

            DotNetPack(s => s
                .EnableNoRestore()
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetProject(RootDirectory / "sdk" / "Beutl.Extensibility.Sdk" / "Beutl.Extensibility.Sdk.csproj"));
        });
}
