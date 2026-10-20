using System.IO.Compression;
using System.Text;
using Beutl.Api.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.HeadlessUITests;

// Packages for the package tests. Each adds what it creates to cleanup when one is given.
internal static class TestPackages
{
    // An extracted package under the install path. A data package holds materials and templates whose
    // item.txt holds its version; a loadable one declares the framework so that its assembly folder loads.
    public static LocalPackage CreateExtracted(
        PackageIdentity identity, bool data, bool loadable = false, ICollection<string>? cleanup = null)
    {
        string version = identity.Version.ToString();
        string directory = Path.Combine(Helper.InstallPath, $"{identity.Id}.{version}");
        cleanup?.Add(directory);
        Directory.CreateDirectory(directory);
        string[] tags = data ? [PackageKinds.MaterialTag, PackageKinds.TemplateTag] : ["effects"];
        File.WriteAllText(Path.Combine(directory, $"{identity.Id}.{version}.nupkg"), "");
        string dependencies = loadable ? "<dependencies><group targetFramework=\"net10.0\" /></dependencies>" : "";
        if (loadable) Directory.CreateDirectory(Path.Combine(directory, "lib", Helper.GetFrameworkName().ToString()));
        File.WriteAllText(Path.Combine(directory, identity.Id + ".nuspec"),
            $"<package><metadata><id>{identity.Id}</id><version>{version}</version><authors>test</authors><description>test</description><tags>{string.Join(' ', tags)}</tags>{dependencies}</metadata></package>");
        if (data)
        {
            foreach (string kind in new[] { "materials", "templates" })
            {
                Directory.CreateDirectory(Path.Combine(directory, kind));
                File.WriteAllText(Path.Combine(directory, kind, "item.txt"), version);
            }
        }
        return new LocalPackage { Name = identity.Id, Version = version, Tags = [.. tags], InstalledPath = directory };
    }

    // A .nupkg in the local package source whose content/payload.txt holds "local payload". The cleanup gets
    // the file and the folder that installing it creates.
    public static PackageIdentity CreateNupkg(string idPrefix, string version, ICollection<string> cleanup)
    {
        var identity = new PackageIdentity($"{idPrefix}.{Guid.NewGuid():N}", NuGetVersion.Parse(version));
        string sourcePath = Helper.GetNupkgFilePath(identity.Id, version);
        cleanup.Add(sourcePath);
        cleanup.Add(Helper.PackagePathResolver.GetInstallPath(identity));
        using var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create);
        using (StreamWriter writer = new(archive.CreateEntry($"{identity.Id}.nuspec").Open(), new UTF8Encoding(false)))
        {
            writer.Write($$"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{{identity.Id}}</id>
                    <version>{{version}}</version>
                    <title>Local package test</title>
                    <authors>tester</authors>
                    <description>A package placed directly in packageSource</description>
                  </metadata>
                </package>
                """);
        }
        using (StreamWriter writer = new(archive.CreateEntry("content/payload.txt").Open(), new UTF8Encoding(false)))
        {
            writer.Write("local payload");
        }
        return identity;
    }

    // A .nupkg whose assembly uses Beutl.Extensibility.NotInHost, a type the host lacks. The cleanup gets what
    // building and installing it creates.
    public static byte[] CreateIncompatibleNupkg(PackageIdentity identity, ICollection<string> cleanup)
    {
        string root = Directory.CreateTempSubdirectory("beutl-incompatible-").FullName;
        cleanup.Add(root);
        cleanup.Add(Helper.GetNupkgFilePath(identity.Id, identity.Version.ToString()));
        cleanup.Add(Helper.PackagePathResolver.GetInstallPath(identity));
        string extensibility = Compile(root, "Beutl.Extensibility", "namespace Beutl.Extensibility; public class NotInHost { }");
        string assembly = Compile(Path.Combine(root, "plugin"), "Incompatible",
            "public class Uses { public System.Type Type = typeof(Beutl.Extensibility.NotInHost); }", extensibility);
        string framework = Helper.GetFrameworkName().GetShortFolderName();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (StreamWriter writer = new(archive.CreateEntry($"{identity.Id}.nuspec").Open(), new UTF8Encoding(false)))
            {
                writer.Write($$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata>
                        <id>{{identity.Id}}</id>
                        <version>{{identity.Version}}</version>
                        <authors>tester</authors>
                        <description>Uses an API the host lacks</description>
                        <dependencies><group targetFramework="{{framework}}" /></dependencies>
                      </metadata>
                    </package>
                    """);
            }
            archive.CreateEntryFromFile(assembly, $"lib/{framework}/Incompatible.dll");
        }
        return stream.ToArray();
    }

    private static string Compile(string directory, string name, string source, params string[] references)
    {
        string path = Path.Combine(Directory.CreateDirectory(directory).FullName, $"{name}.dll");
        EmitResult result = CSharpCompilation.Create(
                name,
                [CSharpSyntaxTree.ParseText(source)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), .. references.Select(reference => MetadataReference.CreateFromFile(reference))],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .Emit(path);
        Assert.That(result.Success, Is.True, () => string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }

    // Deletes the files and folders at paths, and empties the list. A path whose folder does not exist, such as
    // the install folder of a package that never resolved, has nothing to delete.
    public static void Delete(List<string> paths)
    {
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (Directory.Exists(Path.GetDirectoryName(path)))
                File.Delete(path);
        }
        paths.Clear();
    }
}

// Keeps a file under the app root as it is, or that it is absent, and puts it back on Dispose.
internal sealed class AppRootFileBackup : IDisposable
{
    private readonly byte[]? _original;

    public AppRootFileBackup(string name)
    {
        Path = System.IO.Path.Combine(Helper.AppRoot, name);
        _original = File.Exists(Path) ? File.ReadAllBytes(Path) : null;
    }

    public string Path { get; }

    public bool Existed => _original is not null;

    public void Dispose()
    {
        if (_original is null)
            File.Delete(Path);
        else
            File.WriteAllBytes(Path, _original);
    }
}
