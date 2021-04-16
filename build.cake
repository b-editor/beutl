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

Task("ConsolePublish")
    .IsDependentOn("Clean")
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

Task("AvaloniaExePublish")
    .IsDependentOn("Clean")
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
        Console.WriteLine($"beditor_avalonia_{rid}");
        Console.WriteLine("========================================");

        var binaryPath = DirectoryPath.FromString($"./src/BEditor.Avalonia/bin/{configuration}/net5.0/{rid}/publish");
        CreateDirectory(binaryPath);
        CleanDirectory(binaryPath);

        DotNetCorePublish("./src/BEditor.Avalonia/BEditor.Avalonia.csproj", new DotNetCorePublishSettings
        {
            Configuration = configuration,
            SelfContained = true,
            Runtime = rid
        });

        Zip(binaryPath, publishDir.CombineWithFilePath($"beditor_avalonia_{rid}.zip"));
    }
});

Task("NugetPack")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetCorePack("./BEditor.Api.sln", new DotNetCorePackSettings()
    {
        Configuration = configuration,
        OutputDirectory = publishDir.Combine("nuget"),
    });
});

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("NugetPack")
    .IsDependentOn("AvaloniaExePublish")
    .IsDependentOn("ConsolePublish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);