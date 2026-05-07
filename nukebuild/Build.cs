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
                .When(_ => Runtime == RuntimeIdentifier.win_x64, s => s.SetFramework($"{tfm}-windows"))
                .When(_ => Runtime != RuntimeIdentifier.win_x64, s => s.SetFramework(tfm))
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
                "Beutl.FFmpegWorker",
            ];
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                DotNetPublish(s => s
                    .When(_ => Runtime != null, s => s
                        .SetRuntime(Runtime)
                        .SetSelfContained(SelfContained))
                    .When(_ => Runtime == RuntimeIdentifier.win_x64, s => s.SetFramework($"{tfm}-windows"))
                    .When(_ => Runtime != RuntimeIdentifier.win_x64, s => s.SetFramework(tfm))
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetProject(SourceDirectory / item / $"{item}.csproj")
                    .SetOutput(output));

                output.GlobFiles($"**/{item}*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => t.Source.Copy(t.Target));
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
                fileName.Append("-standalone");
            }

            fileName.Append('-');
            fileName.Append(Version);
            fileName.Append(".zip");

            mainOutput.CompressTo(ArtifactsDirectory / fileName.ToString());
        });

    Target PackageFlatpak => _ => _
        .Description("Build a Flatpak bundle from the existing linux-x64 standalone zip. Linux-only; requires flatpak and flatpak-builder.")
        .Executes(() =>
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new InvalidOperationException(
                    "PackageFlatpak target requires a Linux host with flatpak-builder installed.");
            }

            AbsolutePath flatpakDir = RootDirectory / "packages" / "flatpak";
            AbsolutePath sourcesDir = flatpakDir / "sources";
            AbsolutePath manifest = flatpakDir / "net.beditor.Beutl.yml";
            AbsolutePath buildDir = flatpakDir / "build-dir";
            AbsolutePath repoDir = flatpakDir / "repo";
            AbsolutePath logoSvg = RootDirectory / "assets" / "logos" / "logo.svg";
            AbsolutePath iconSvg = flatpakDir / "net.beditor.Beutl.svg";
            AbsolutePath metainfo = flatpakDir / "net.beditor.Beutl.metainfo.xml";

            string zipName = $"Beutl-linux-x64-standalone-{Version}.zip";
            AbsolutePath zipPath = ArtifactsDirectory / zipName;
            if (!zipPath.FileExists())
            {
                throw new FileNotFoundException(
                    $"Required artifact not found: {zipPath}. " +
                    "Run Zip target first with --runtime linux-x64 --self-contained true.");
            }

            // Stage source tree expected by the manifest (sources/Beutl-linux-x64-standalone)
            sourcesDir.CreateOrCleanDirectory();
            AbsolutePath staging = sourcesDir / "Beutl-linux-x64-standalone";
            staging.CreateOrCleanDirectory();
            zipPath.UnZipTo(staging);

            // Copy logo into the manifest directory so flatpak-builder can install it.
            logoSvg.Copy(iconSvg, ExistsPolicy.FileOverwrite);

            // Substitute version/date placeholders in the metainfo (write to a temp copy
            // first so the working tree stays clean if the build aborts).
            string metainfoText = File.ReadAllText(metainfo);
            string substituted = metainfoText
                .Replace("VERSION_PLACEHOLDER", Version)
                .Replace("DATE_PLACEHOLDER", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            File.WriteAllText(metainfo, substituted);

            try
            {
                buildDir.CreateOrCleanDirectory();
                repoDir.CreateOrCleanDirectory();

                StartProcess(
                    "flatpak-builder",
                    $"--user --force-clean --install-deps-from=flathub --repo={repoDir} {buildDir} {manifest}",
                    workingDirectory: flatpakDir)
                    .AssertZeroExitCode();

                AbsolutePath outBundle = ArtifactsDirectory / $"Beutl-{Version}.flatpak";
                outBundle.DeleteFile();
                StartProcess(
                    "flatpak",
                    $"build-bundle {repoDir} {outBundle} net.beditor.Beutl",
                    workingDirectory: flatpakDir)
                    .AssertZeroExitCode();
            }
            finally
            {
                // Restore the metainfo placeholders so VCS state is unchanged.
                File.WriteAllText(metainfo, metainfoText);
            }
        });

    Target BuildInstaller => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
            InnoSetup(c => c
                .SetKeyValueDefinition("MyAppVersion", AssemblyVersion)
                .SetKeyValueDefinition("MyOutputDir", ArtifactsDirectory)
                .SetKeyValueDefinition("MyLicenseFile", RootDirectory / "LICENSE")
                .SetKeyValueDefinition("MyGPLLicenseFile", RootDirectory / "LICENSE.GPL")
                .SetKeyValueDefinition("MySetupIconFile", RootDirectory / "assets/logos/logo.ico")
                .SetKeyValueDefinition("MySource", OutputDirectory / "Beutl")
                .SetKeyValueDefinition("MyOutputBaseFilename", $"beutl{(SelfContained ? "-standalone" : "")}-setup")
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
