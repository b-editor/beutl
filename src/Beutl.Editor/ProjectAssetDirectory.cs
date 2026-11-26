using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor;

// Each native call resolves a single name relative to an already opened directory.
internal sealed partial class ProjectAssetDirectory : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal string DisplayPath { get; }

    private ProjectAssetDirectory(SafeFileHandle handle, string path)
    {
        _handle = handle;
        DisplayPath = path;
    }

    internal static ProjectAssetDirectory OpenRoot(string path)
    {
        string root = Path.GetPathRoot(path) ?? throw new IOException($"No filesystem root for '{path}'.");
        SafeFileHandle handle = OperatingSystem.IsWindows() ? Windows.OpenRoot(root) : Unix.OpenRoot(root);
        var current = new ProjectAssetDirectory(handle, root);
        try
        {
            foreach (string name in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                ProjectAssetDirectory child = current.OpenDirectory(name, create: false, out _);
                current.Dispose();
                current = child;
            }
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    internal ProjectAssetDirectory OpenDirectory(string name, bool create, out bool created)
    {
        string path = ChildPath(name);
        SafeFileHandle handle = OperatingSystem.IsWindows()
            ? Windows.OpenDirectory(_handle, name, path, create, out created)
            : Unix.OpenDirectory(_handle, name, path, create, out created);
        return new ProjectAssetDirectory(handle, path);
    }

    internal SafeFileHandle CreateFile(string name)
    {
        string path = ChildPath(name);
        return OperatingSystem.IsWindows() ? Windows.CreateFile(_handle, name, path) : Unix.CreateFile(_handle, name, path);
    }

    // When an exclusive rename is unavailable, publication can leave the staging name for cleanup.
    internal bool PublishFile(string name, ProjectAssetDirectory destination, string destinationName)
    {
        string sourcePath = ChildPath(name);
        string destinationPath = destination.ChildPath(destinationName);
        if (OperatingSystem.IsWindows())
        {
            Windows.Rename(_handle, name, sourcePath, destination._handle, destinationName, destinationPath);
            return false;
        }
        return Unix.Publish(_handle, name, destination._handle, destinationName, destinationPath);
    }

    internal void Delete(string name, bool directory = false)
    {
        string path = ChildPath(name);
        if (OperatingSystem.IsWindows()) Windows.Delete(_handle, name, path, directory);
        else Unix.Delete(_handle, name, path, directory);
    }

    private string ChildPath(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException($"Invalid project asset name '{name}'.");
        return Path.Combine(DisplayPath, name);
    }

    private static IOException FileError(string path, int error)
    {
        var cause = new Win32Exception(error);
        return new IOException($"Could not access project asset path '{path}': {cause.Message}", cause);
    }

    public void Dispose() => _handle.Dispose();
}
