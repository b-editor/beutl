#addin nuget:?package=Cake.Compression

using System;
var target = Argument("target", "Default");
var runtime = Argument("runtime", "win-x64;linux-x64;osx-x64");
var configuration = Argument("configuration", "Release");
var publishDir = DirectoryPath.FromString("./publish");
CreateDirectory(publishDir);

void Publish(string rid)
{
    Console.WriteLine("========================================");
    Console.WriteLine($"beditor_{rid}");
    Console.WriteLine("========================================");

    var binaryPath = DirectoryPath.FromString($"./tmp/{rid}");
    CreateDirectory(binaryPath);
    CleanDirectory(binaryPath);

    DotNetCorePublish("./src/executable/BEditor.PackageInstaller/BEditor.PackageInstaller.csproj", new DotNetCorePublishSettings
    {
        Configuration = configuration,
        SelfContained = true,
        Runtime = rid,
        Framework = "net5.0",
        OutputDirectory = binaryPath
    });

    DotNetCorePublish("./src/executable/BEditor.Avalonia/BEditor.Avalonia.csproj", new DotNetCorePublishSettings
    {
        Configuration = configuration,
        SelfContained = true,
        Runtime = rid,
        Framework = "net5.0",
        OutputDirectory = binaryPath
    });

    Zip(binaryPath, publishDir.CombineWithFilePath($"beditor_{rid}.zip"));

    DeleteDirectory("./tmp", new DeleteDirectorySettings
    {
        Recursive = true,
        Force = true
    });
}

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

Task("AppPublish")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        var runtimes = runtime.Split(';');
        foreach (var item in runtimes)
        {
            Publish(item);
        }
    });

Task("NugetPublish")
    .IsDependentOn("Clean")
    .Does(() =>
{
    var libraries = new string[]
    {
        "BEditor.Audio",
        "BEditor.Base",
        "BEditor.Compute",
        "BEditor.Core",
        "BEditor.Drawing",
        "BEditor.Graphics",
        "BEditor.Media",
        "BEditor.Packaging",
        "BEditor.Settings",
    };

    foreach (var item in libraries)
    {
        DotNetCorePack($"./src/libraries/{item}/{item}.csproj", new DotNetCorePackSettings
        {
            Configuration = configuration,
            OutputDirectory = publishDir.Combine("nuget")
        });
    }
});


Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("AppPublish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);