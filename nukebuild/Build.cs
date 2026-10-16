using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Nuke.Common.Tools.InnoSetup;
using static Nuke.Common.Tools.InnoSetup.InnoSetupTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter] Configuration Configuration = Configuration.Release;

    [Parameter] RuntimeIdentifier Runtime = null;

    [Parameter] bool SelfContained = false;

    [Parameter] string Version = "1.0.0";

    [Parameter] string AssemblyVersion = "1.0.0.0";

    [Parameter] string InformationalVersion = "1.0.0.0";

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
            // Forward the RID so the assets file gains the runtime-specific target the
            // subsequent --no-restore publish needs; without it publish fails with NETSDK1047.
            DotNetRestore(s => s
                .When(_ => Runtime != null, s => s.SetRuntime(Runtime)));
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

    Target Dev => _ => _
        .Description("Run Beutl in Debug configuration for the current platform")
        .Executes(() =>
        {
            string tfm = GetTFM();

            DotNetRun(s => s
                .SetConfiguration(Configuration.Debug)
                .SetFramework(OperatingSystem.IsWindows() ? $"{tfm}-windows" : tfm)
                .SetProjectFile(SourceDirectory / "Beutl" / "Beutl.csproj"));
        });

    private string GetTFM()
    {
        AbsolutePath mainProj = SourceDirectory / "Beutl" / "Beutl.csproj";
        using IProcess proc = StartProcess(DotNetPath, $"msbuild --getProperty:TargetFrameworks {mainProj}");
        proc.AssertZeroExitCode();
        return proc.Output.First().Text.Split(';')[0];
    }

    // The projects marked BeutlPublicLibrary, each with the framework it builds for (build/BeutlLibraries.proj).
    private (AbsolutePath Project, string Framework)[] GetPublicLibraries()
    {
        using IProcess proc = StartProcess(
            DotNetPath,
            $"msbuild {RootDirectory / "build" / "BeutlLibraries.proj"} -t:CollectBeutlLibraries -getItem:BeutlLibrary",
            logOutput: false);
        proc.AssertZeroExitCode();
        using JsonDocument document = JsonDocument.Parse(string.Join('\n', proc.Output.Where(line => line.Type == OutputType.Std).Select(line => line.Text)));
        return [.. document.RootElement.GetProperty("Items").GetProperty("BeutlLibrary").EnumerateArray()
            .Where(item => item.GetProperty("Kind").GetString() == "Public")
            .Select(item => ((AbsolutePath)item.GetProperty("ProjectPath").GetString(), item.GetProperty("TargetFramework").GetString()!))];
    }

    private void CopyDistributionLicenses(AbsolutePath destination)
    {
        ReleaseLegalFiles.CopyTo(RootDirectory.ToString(), destination.ToString());
    }

    Target Publish => _ => _
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

            void PublishProject(string name, AbsolutePath output, bool useWindowsTfm)
            {
                DotNetPublish(s => s
                    .When(_ => Runtime != null, s => s
                        .SetRuntime(Runtime)
                        .SetSelfContained(SelfContained))
                    .SetFramework(useWindowsTfm && Runtime?.IsWindows == true ? $"{tfm}-windows" : tfm)
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetProject(SourceDirectory / name / $"{name}.csproj")
                    .SetOutput(output));
            }

            string[] subProjects =
            [
                "Beutl.ExceptionHandler",
                "Beutl.PackageTools.UI",
            ];
            foreach (string item in subProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                PublishProject(item, output, useWindowsTfm: true);

                output.GlobFiles($"**/{item}*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => t.Source.Copy(t.Target));
            }

            // The MCP server has private dependencies, so merge its whole output with FileSkip.
            string[] fullClosureSubProjects =
            [
                "Beutl.AgentToolkit.Mcp",
            ];
            foreach (string item in fullClosureSubProjects)
            {
                AbsolutePath output = OutputDirectory / item;
                output.CreateOrCleanDirectory();
                PublishProject(item, output, useWindowsTfm: false);

                output.GlobFiles("**/*")
                    .Select(p => (Source: p, Target: mainOutput / output.GetRelativePathTo(p)))
                    .ForEach(t => t.Source.Copy(t.Target, ExistsPolicy.FileSkip));
            }

            // The worker has private dependencies the app lacks, so merge its whole output with FileSkip.
            // Clean the output first so stale artifacts from an earlier RID or configuration stay out.
            AbsolutePath workerOutput = OutputDirectory / "Beutl.FFmpegWorker";
            workerOutput.CreateOrCleanDirectory();
            PublishProject("Beutl.FFmpegWorker", workerOutput, useWindowsTfm: true);
            workerOutput.GlobFiles("**/*")
                .Select(p => (Source: p, Target: mainOutput / workerOutput.GetRelativePathTo(p)))
                .ForEach(t => t.Source.Copy(t.Target, ExistsPolicy.FileSkip));

            CopyDistributionLicenses(mainOutput);
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

            // Dotnet.Bundle 0.9.13 does not forward CFBundleURLTypes to its plist writer.
            // Register the protocol in the generated bundle before it is signed.
            AbsolutePath plistPath = output / "Beutl.app" / "Contents" / "Info.plist";
            XDocument plist = XDocument.Load(plistPath);
            plist.Root!.Element("dict")!.Add(
                new XElement("key", "CFBundleURLTypes"),
                new XElement("array", new XElement("dict",
                    new XElement("key", "CFBundleURLName"),
                    new XElement("string", "net.beditor.beutl"),
                    new XElement("key", "CFBundleURLSchemes"),
                    new XElement("array", new XElement("string", "beutl")))));
            // XDocument emits an empty [] internal subset when re-serializing
            // this DTD, which makes the bundle's Info.plist invalid to plutil.
            plist.DocumentType?.Remove();
            plist.Save(plistPath);

            // BundleApp leaves a partial worker that cannot start, and the worker has private dependencies, so replace
            // it with its whole output, merged with FileSkip. Clean the output first so stale artifacts stay out.
            AbsolutePath workerProj = SourceDirectory / "Beutl.FFmpegWorker" / "Beutl.FFmpegWorker.csproj";
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

            AbsolutePath mcpProj = SourceDirectory / "Beutl.AgentToolkit.Mcp" / "Beutl.AgentToolkit.Mcp.csproj";
            AbsolutePath mcpOutput = OutputDirectory / "Beutl.AgentToolkit.Mcp";
            mcpOutput.CreateOrCleanDirectory();
            DotNetPublish(s => s
                .SetRuntime(Runtime)
                .SetSelfContained(true)
                .SetFramework(tfm)
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetProject(mcpProj)
                .SetOutput(mcpOutput));

            mcpOutput.GlobFiles("**/*")
                .Select(p => (Source: p, Target: bundleContents / mcpOutput.GetRelativePathTo(p)))
                .ForEach(t => t.Source.Copy(t.Target, ExistsPolicy.FileSkip));

            CopyDistributionLicenses(output / "Beutl.app" / "Contents" / "Resources");
        });

    Target NuGetPack => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            foreach ((AbsolutePath project, string framework) in GetPublicLibraries())
            {
                DotNetBuild(s => s
                    .EnableNoRestore()
                    .SetFramework(framework)
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetProjectFile(project));

                DotNetPack(s => s
                    .EnableNoRestore()
                    .SetConfiguration(Configuration)
                    .SetVersions(Version, AssemblyVersion, InformationalVersion)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetProject(project));
            }

            DotNetPack(s => s
                .EnableNoRestore()
                .SetConfiguration(Configuration)
                .SetVersions(Version, AssemblyVersion, InformationalVersion)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetProject(RootDirectory / "sdk" / "Beutl.Extensibility.Sdk" / "Beutl.Extensibility.Sdk.csproj"));
        });
}
