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
        Console.WriteLine($"beditor_{rid}");
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

        Zip(binaryPath, publishDir.CombineWithFilePath($"beditor_{rid}.zip"));
    }
});

Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("AvaloniaExePublish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);