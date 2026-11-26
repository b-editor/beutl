using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor;

internal sealed partial class ProjectAssetDirectory
{
    private static unsafe class Windows
    {
        private const uint FileTraverse = 0x20;
        private const uint FileReadAttributes = 0x80;
        private const uint DeleteAccess = 0x10000;
        private const uint Synchronize = 0x100000;
        private const uint GenericWrite = 0x40000000;
        private const uint ShareAll = 7;
        private const uint Open = 1;
        private const uint Create = 2;
        private const uint OpenIf = 3;
        private const uint DirectoryFile = 1;
        private const uint NonDirectoryFile = 0x40;
        private const uint SynchronousIoNonAlert = 0x20;
        private const uint OpenReparsePoint = 0x200000;
        private const uint CaseInsensitive = 0x40;
        private const int StatusNameNotFound = unchecked((int)0xc0000034);
        private const int StatusPathNotFound = unchecked((int)0xc000003a);
        private const int StatusDirectoryNotEmpty = unchecked((int)0xc0000101);
        private const int FileRenameInformation = 10;
        private const int FileDispositionInformation = 13;

        internal static SafeFileHandle OpenRoot(string path)
        {
            const uint OpenExisting = 3;
            const uint BackupSemantics = 0x02000000;
            const uint OpenReparsePointFlag = 0x00200000;
            SafeFileHandle handle = CreateFileW(path, FileTraverse | FileReadAttributes, ShareAll, 0,
                OpenExisting, BackupSemantics | OpenReparsePointFlag, 0);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw FileError(path, error);
            }
            return RequireType(handle, path, directory: true);
        }

        internal static SafeFileHandle OpenDirectory(SafeFileHandle parent, string name, string path, bool create, out bool created)
        {
            int status = OpenRelative(parent, name, FileTraverse | FileReadAttributes, ShareAll,
                create ? OpenIf : Open, DirectoryFile, out nint result, out IoStatusBlock io);
            ThrowIfFailed(status, path);
            const int FileCreated = 2;
            created = io.Information == FileCreated;
            return RequireType(new SafeFileHandle(result, ownsHandle: true), path, directory: true);
        }

        internal static SafeFileHandle CreateFile(SafeFileHandle parent, string name, string path)
        {
            int status = OpenRelative(parent, name, GenericWrite, 0, Create, NonDirectoryFile, out nint result, out _);
            ThrowIfFailed(status, path);
            return new SafeFileHandle(result, ownsHandle: true);
        }

        internal static DirectoryIdentity? GetIdentity(SafeFileHandle handle)
        {
            const int FileIdInfo = 18;
            return GetFileInformationByHandleEx(handle, FileIdInfo, out FileIdentity info, (uint)sizeof(FileIdentity))
                   && (info.Low != 0 || info.High != 0)
                ? new DirectoryIdentity(info.Volume, info.Low, info.High)
                : null;
        }

        internal static void Rename(SafeFileHandle source, string name, string sourcePath,
            SafeFileHandle destination, string destinationName, string destinationPath)
        {
            int status = OpenRelative(source, name, DeleteAccess | FileReadAttributes, ShareAll, Open,
                NonDirectoryFile, out nint result, out _);
            ThrowIfFailed(status, sourcePath);
            using SafeFileHandle handle = RequireType(new SafeFileHandle(result, ownsHandle: true), sourcePath, directory: false);

            int nameBytes = checked(destinationName.Length * sizeof(char));
            Span<byte> buffer = stackalloc byte[sizeof(RenameInformation) + nameBytes];
            buffer.Clear();
            fixed (byte* data = buffer)
            {
                var info = (RenameInformation*)data;
                info->RootDirectory = destination.DangerousGetHandle();
                info->FileNameLength = (uint)nameBytes;
                destinationName.AsSpan().CopyTo(new Span<char>(&info->FileName, destinationName.Length));
                status = NtSetInformationFile(handle, out _, data, (uint)buffer.Length, FileRenameInformation);
                GC.KeepAlive(destination);
            }
            ThrowIfFailed(status, destinationPath);
        }

        internal static void Delete(SafeFileHandle parent, string name, string path, bool directory)
        {
            int status = OpenRelative(parent, name, DeleteAccess | FileReadAttributes, ShareAll, Open,
                directory ? DirectoryFile : NonDirectoryFile, out nint result, out _);
            if (status is StatusNameNotFound or StatusPathNotFound) return;
            ThrowIfFailed(status, path);
            using SafeFileHandle handle = RequireType(new SafeFileHandle(result, ownsHandle: true), path, directory);
            byte deleteFile = 1;
            status = NtSetInformationFile(handle, out _, &deleteFile, sizeof(byte), FileDispositionInformation);
            if (status != StatusDirectoryNotEmpty) ThrowIfFailed(status, path);
        }

        private static SafeFileHandle RequireType(SafeFileHandle handle, string path, bool directory)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(handle);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
                    throw new IOException($"The project asset path is a link or has an unexpected file type: '{path}'.");
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static int OpenRelative(SafeFileHandle parent, string name, uint access, uint share, uint disposition,
            uint options, out nint result, out IoStatusBlock io)
        {
            fixed (char* buffer = name)
            {
                ushort length = checked((ushort)(name.Length * sizeof(char)));
                var fileName = new UnicodeString { Length = length, MaximumLength = length, Buffer = buffer };
                var attributes = new ObjectAttributes
                {
                    Length = (uint)sizeof(ObjectAttributes),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = &fileName,
                    Attributes = CaseInsensitive,
                };
                int status = NtCreateFile(out result, access | Synchronize, &attributes, out io,
                    0, 0, share, disposition, options | SynchronousIoNonAlert | OpenReparsePoint, 0, 0);
                GC.KeepAlive(parent);
                return status;
            }
        }

        private static void ThrowIfFailed(int status, string path)
        {
            if (status < 0) throw FileError(path, RtlNtStatusToDosError(status));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public char* Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            public uint Length;
            public nint RootDirectory;
            public UnicodeString* ObjectName;
            public uint Attributes;
            public nint SecurityDescriptor;
            public nint SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            public nint Status;
            public nuint Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RenameInformation
        {
            public uint Flags;
            public nint RootDirectory;
            public uint FileNameLength;
            public char FileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public ulong Volume;
            public ulong Low;
            public ulong High;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security,
            uint disposition, uint flags, nint template);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out FileIdentity info, uint length);

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(out nint handle, uint access, ObjectAttributes* attributes, out IoStatusBlock io,
            nint allocationSize, uint fileAttributes, uint share, uint disposition, uint options, nint eaBuffer, uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock io,
            void* information, uint length, int informationClass);

        [DllImport("ntdll.dll")]
        private static extern int RtlNtStatusToDosError(int status);
    }
}
