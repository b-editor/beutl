#addin nuget:?package=Cake.Compression

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var publishDir = DirectoryPath.FromString("./publish");

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .WithCriteria(c => HasArgument("rebuild"))
    .Does(() =>
{
    CreateDirectory(publishDir);
    CleanDirectory(publishDir);
    DotNetCoreClean("./BEditor.sln");
});

Task("Build")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetCoreBuild("./BEditor.sln", new DotNetCoreBuildSettings
    {
        Configuration = configuration
    });
});

Task("WPFPublish")
    .IsDependentOn("Build")
    .Does(() =>
{
    var binaryPath = DirectoryPath.FromString($"./src/BEditor.WPF/bin/{configuration}/net5.0-windows/win-x64/publish");
    CreateDirectory(binaryPath);
    CleanDirectory(binaryPath);

    DotNetCorePublish("./src/BEditor.WPF/BEditor.WPF.csproj", new DotNetCorePublishSettings
    {
        Configuration = configuration,
        SelfContained = true,
        Runtime = "win-x64",
        NoBuild = true
    });

    Zip(binaryPath, publishDir.CombineWithFilePath("beditor_wpf.zip"));
});

Task("ConsolePublish")
    .IsDependentOn("Build")
    .Does(() =>
{
    var rids = new string[]
    {
        "win-x64",
        "linux-x64",
        "osx-x64"
    };

    foreach (var rid in rids)
    {
        Console.WriteLine("========================================");
        Console.WriteLine($"beditor_console_{rid}");
        Console.WriteLine("========================================");

        var binaryPath = DirectoryPath.FromString($"./src/BEditor.Console/bin/{configuration}/net5.0/{rid}/publish");
        CreateDirectory(binaryPath);
        CleanDirectory(binaryPath);

        DotNetCorePublish("./src/BEditor.Console/BEditor.Console.csproj", new DotNetCorePublishSettings
        {
            Configuration = configuration,
            SelfContained = true,
            Runtime = rid
        });

        Zip(binaryPath, publishDir.CombineWithFilePath($"beditor_console_{rid}.zip"));
    }
});

Task("NugetPack")
    .IsDependentOn("Build")
    .Does(() =>
{
    DotNetCorePack("./BEditor.Api.sln", new DotNetCorePackSettings()
    {
        OutputDirectory = publishDir.Combine("nuget")
    });
});

Task("Default")
    .IsDependentOn("NugetPack")
    .IsDependentOn("ConsolePublish")
    .IsDependentOn("WPFPublish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);