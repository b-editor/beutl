using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Assimp;
using AssimpFile = Silk.NET.Assimp.File;

namespace Beutl.Graphics3D.Models;

// Record the files Assimp actually opens, including buffers and material libraries.
// Callbacks return native errors: managed I/O exceptions must never cross the C ABI.
internal sealed unsafe class ModelImportFileIO : IDisposable
{
    private readonly string _directory;
    private readonly GCHandle _self;
    private readonly Dictionary<nint, (FileStream Stream, GCHandle Handle)> _openFiles = [];

    public ModelImportFileIO(string directory)
    {
        _directory = directory;
        _self = GCHandle.Alloc(this);
        FileIO = new FileIO
        {
            UserData = (byte*)GCHandle.ToIntPtr(_self),
            OpenProc = (delegate* unmanaged[Cdecl]<FileIO*, byte*, byte*, AssimpFile*>)&Open,
            CloseProc = (delegate* unmanaged[Cdecl]<FileIO*, AssimpFile*, void>)&Close,
        };
    }

    public FileIO FileIO { get; }

    public HashSet<string> Paths { get; } = new(StringComparer.Ordinal);
    public HashSet<string> MissingPaths { get; } = new(StringComparer.Ordinal);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static AssimpFile* Open(FileIO* io, byte* name, byte* mode)
    {
        FileStream? stream = null;
        GCHandle handle = default;
        AssimpFile* file = null;
        ModelImportFileIO? owner = null;
        string? path = null;
        try
        {
            if (Marshal.PtrToStringUTF8((nint)mode) is not ("r" or "rb" or "rt"))
                return null;
            owner = (ModelImportFileIO)GCHandle.FromIntPtr((nint)io->UserData).Target!;
            path = Path.GetFullPath(Marshal.PtrToStringUTF8((nint)name)!.Replace('\\', Path.DirectorySeparatorChar), owner._directory);
            stream = System.IO.File.OpenRead(path);
            handle = GCHandle.Alloc(stream);
            file = (AssimpFile*)NativeMemory.Alloc((nuint)sizeof(AssimpFile));
            *file = new AssimpFile
            {
                UserData = (byte*)GCHandle.ToIntPtr(handle),
                ReadProc = (delegate* unmanaged[Cdecl]<AssimpFile*, byte*, nuint, nuint, nuint>)&Read,
                WriteProc = (delegate* unmanaged[Cdecl]<AssimpFile*, byte*, nuint, nuint, nuint>)&Write,
                TellProc = (delegate* unmanaged[Cdecl]<AssimpFile*, nuint>)&Tell,
                FileSizeProc = (delegate* unmanaged[Cdecl]<AssimpFile*, nuint>)&Size,
                SeekProc = (delegate* unmanaged[Cdecl]<AssimpFile*, nuint, Origin, Return>)&Seek,
                FlushProc = (delegate* unmanaged[Cdecl]<AssimpFile*, void>)&Flush,
            };
            owner.Paths.Add(path);
            owner._openFiles.Add((nint)file, (stream, handle));
            return file;
        }
        catch (Exception ex)
        {
            if ((ex is FileNotFoundException or DirectoryNotFoundException) && path != null)
                owner?.MissingPaths.Add(path);
            try { stream?.Dispose(); }
            catch { }
            if (handle.IsAllocated) handle.Free();
            NativeMemory.Free(file);
            return null;
        }
    }

    private static FileStream Stream(AssimpFile* file)
        => (FileStream)GCHandle.FromIntPtr((nint)file->UserData).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Read(AssimpFile* file, byte* buffer, nuint size, nuint count)
    {
        if (size == 0) return 0;
        nuint read = 0;
        try
        {
            nuint length = checked(size * count);
            FileStream stream = Stream(file);
            while (read < length)
            {
                int chunk = (int)Math.Min(length - read, (nuint)int.MaxValue);
                int bytes = stream.Read(new Span<byte>(buffer + read, chunk));
                if (bytes == 0) break;
                read += (nuint)bytes;
            }
        }
        catch { }
        return read / size;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Write(AssimpFile* file, byte* buffer, nuint size, nuint count) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Tell(AssimpFile* file)
    {
        try { return (nuint)Stream(file).Position; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint Size(AssimpFile* file)
    {
        try { return (nuint)Stream(file).Length; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Return Seek(AssimpFile* file, nuint offset, Origin origin)
    {
        try
        {
            Stream(file).Seek(unchecked((long)offset), (SeekOrigin)origin);
            return Return.Success;
        }
        catch { return Return.Failure; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Flush(AssimpFile* file) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Close(FileIO* io, AssimpFile* file)
    {
        try
        {
            var owner = (ModelImportFileIO)GCHandle.FromIntPtr((nint)io->UserData).Target!;
            owner.CloseFile((nint)file);
        }
        catch { }
    }

    private void CloseFile(nint file)
    {
        if (!_openFiles.Remove(file, out var opened)) return;
        try { opened.Stream.Dispose(); }
        finally
        {
            opened.Handle.Free();
            NativeMemory.Free((void*)file);
        }
    }

    public void Dispose()
    {
        foreach (nint file in _openFiles.Keys.ToArray())
            CloseFile(file);
        _self.Free();
    }
}
