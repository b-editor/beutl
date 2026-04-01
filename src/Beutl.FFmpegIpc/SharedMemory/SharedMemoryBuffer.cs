using System.IO.MemoryMappedFiles;

namespace Beutl.FFmpegIpc.SharedMemory;

/// <summary>
/// クロスプラットフォーム対応の共有メモリバッファ。
/// ファイルバック方式でWindows/macOS/Linuxすべてで動作する。
/// Linux: /dev/shm を使用（RAMディスク）
/// macOS/Windows: 一時ディレクトリを使用
/// </summary>
public sealed class SharedMemoryBuffer : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly string? _filePath;
    private readonly bool _ownsFile;

    private SharedMemoryBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, string name, long capacity, string? filePath, bool ownsFile)
    {
        _mmf = mmf;
        _accessor = accessor;
        Name = name;
        Capacity = capacity;
        _filePath = filePath;
        _ownsFile = ownsFile;
    }

    public string Name { get; }

    public long Capacity { get; }

    /// <summary>
    /// 新しい共有メモリバッファを作成する。名前からファイルパスを決定する。
    /// </summary>
    public static SharedMemoryBuffer Create(string name, long capacity)
    {
        if (OperatingSystem.IsWindows())
        {
            var mmf = MemoryMappedFile.CreateNew(name, capacity, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, HandleInheritability.None);
            var accessor = mmf.CreateViewAccessor(0, capacity);
            return new SharedMemoryBuffer(mmf, accessor, name, capacity, null, ownsFile: true);
        }
        else
        {
            string filePath = GetSharedMemoryPath(name);
            var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            stream.SetLength(capacity);
            var mmf = MemoryMappedFile.CreateFromFile(stream, null, capacity, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: false);
            var accessor = mmf.CreateViewAccessor(0, capacity);
            return new SharedMemoryBuffer(mmf, accessor, name, capacity, filePath, ownsFile: true);
        }
    }

    /// <summary>
    /// 既存の共有メモリバッファを開く。
    /// </summary>
    public static SharedMemoryBuffer Open(string name, long capacity)
    {
        if (OperatingSystem.IsWindows())
        {
            var mmf = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.ReadWrite, HandleInheritability.None);
            var accessor = mmf.CreateViewAccessor(0, capacity);
            return new SharedMemoryBuffer(mmf, accessor, name, capacity, null, ownsFile: false);
        }
        else
        {
            string filePath = GetSharedMemoryPath(name);
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var mmf = MemoryMappedFile.CreateFromFile(stream, null, 0, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: false);
            var accessor = mmf.CreateViewAccessor(0, capacity);
            return new SharedMemoryBuffer(mmf, accessor, name, capacity, filePath, ownsFile: false);
        }
    }

    public unsafe byte* AcquirePointer()
    {
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        return ptr;
    }

    public void ReleasePointer()
    {
        _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
    }

    public unsafe void Write(ReadOnlySpan<byte> data, long offset = 0)
    {
        if (offset + data.Length > Capacity)
            throw new ArgumentOutOfRangeException(nameof(offset), "Write exceeds buffer capacity.");

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            data.CopyTo(new Span<byte>(ptr + offset, data.Length));
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public unsafe void Read(Span<byte> destination, long offset = 0)
    {
        if (offset + destination.Length > Capacity)
            throw new ArgumentOutOfRangeException(nameof(offset), "Read exceeds buffer capacity.");

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            new ReadOnlySpan<byte>(ptr + offset, destination.Length).CopyTo(destination);
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public void Dispose()
    {
        _accessor.Dispose();
        _mmf.Dispose();

        if (_ownsFile && _filePath != null)
        {
            try { File.Delete(_filePath); } catch { }
        }
    }

    private static string GetSharedMemoryPath(string name)
    {
        // Linux: /dev/shm はRAMディスクで高速
        if (OperatingSystem.IsLinux() && Directory.Exists("/dev/shm"))
        {
            return Path.Combine("/dev/shm", name);
        }

        // macOS / Windows: 一時ディレクトリ
        return Path.Combine(Path.GetTempPath(), name);
    }
}
