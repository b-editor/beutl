#addin nuget:?package=Cake.Compression

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var publishDir = DirectoryPath.FromString("./publish");
CreateDirectory(publishDir);

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
            "osx-x64",
        };

        foreach (var rid in rids)
        {
            Console.WriteLine("========================================");
            Console.WriteLine($"beditor_{rid}");
            Console.WriteLine("========================================");

            var binaryPath = DirectoryPath.FromString($"./tmp/{rid}");
            CreateDirectory(binaryPath);
            CleanDirectory(binaryPath);

            DotNetCorePublish("./src/BEditor.PackageInstaller/BEditor.PackageInstaller.csproj", new DotNetCorePublishSettings
            {
                Configuration = configuration,
                SelfContained = true,
                Runtime = rid,
                Framework = "net5.0",
                OutputDirectory = binaryPath
            });

            DotNetCorePublish("./src/BEditor.Avalonia/BEditor.Avalonia.csproj", new DotNetCorePublishSettings
            {
                Configuration = configuration,
                SelfContained = true,
                Runtime = rid,
                Framework = "net5.0",
                OutputDirectory = binaryPath
            });

            Zip(binaryPath, publishDir.CombineWithFilePath($"beditor_{rid}.zip"));
        }
        DeleteDirectory("./tmp", new DeleteDirectorySettings
        {
            Recursive = true,
            Force = true
        });
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
        DotNetCorePack($"./src/{item}/{item}.csproj", new DotNetCorePackSettings
        {
            Configuration = configuration,
            OutputDirectory = publishDir.Combine("nuget")
        });
    }
});


Task("Default")
    .IsDependentOn("Clean")
    .IsDependentOn("AvaloniaExePublish");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);