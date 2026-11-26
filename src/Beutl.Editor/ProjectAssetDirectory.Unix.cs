using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Beutl.Editor;

internal sealed partial class ProjectAssetDirectory
{
    private static class Unix
    {
        private const int Interrupted = 4;
        private const int NoSuchEntry = 2;
        private const int EntryExists = 17;
        private const int InvalidArgument = 22;
        private const int DefaultFileMode = 0x1b6; // 0666, subject to umask
        private const int DefaultDirectoryMode = 0x1ff; // 0777, subject to umask
        private const int MacSearch = 0x40100000;
        private const int LinuxPath = 0x200000;
        private static readonly bool s_mac = OperatingSystem.IsMacOS();
        private static readonly bool s_arm = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm;
        private static readonly bool s_macArm = s_mac && s_arm;
        private static readonly int s_closeOnExec = s_mac ? 0x1000000 : 0x80000;
        private static readonly int s_noFollow = s_mac ? 0x100 : s_arm ? 0x8000 : 0x20000;
        // Darwin O_SEARCH and Linux O_PATH keep traversal possible through search-only ancestors.
        private static readonly int s_directoryFlags = s_closeOnExec | s_noFollow
            | (s_mac ? MacSearch : LinuxPath | (s_arm ? 0x4000 : 0x10000));

        internal static SafeFileHandle OpenRoot(string path)
        {
            if (!s_mac && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            int fd;
            do { fd = Open(path, s_directoryFlags); }
            while (fd < 0 && Marshal.GetLastPInvokeError() == Interrupted);
            return OwnFileHandle(fd, path);
        }

        internal static SafeFileHandle OpenDirectory(SafeFileHandle parent, string name, string path, bool create, out bool created)
        {
            int fd = OpenChild(parent, name, s_directoryFlags);
            created = false;
            if (fd < 0 && create && Marshal.GetLastPInvokeError() == NoSuchEntry)
            {
                created = MkdirAt(parent, name, DefaultDirectoryMode) == 0;
                if (!created && Marshal.GetLastPInvokeError() != EntryExists)
                    throw FileError(path, Marshal.GetLastPInvokeError());
                fd = OpenChild(parent, name, s_directoryFlags);
            }
            return OwnFileHandle(fd, path);
        }

        internal static SafeFileHandle CreateFile(SafeFileHandle parent, string name, string path)
        {
            const int WriteOnly = 1;
            int createExclusive = s_mac ? 0xa00 : 0xc0;
            int fd = OpenChild(parent, name, s_closeOnExec | s_noFollow | WriteOnly | createExclusive, DefaultFileMode);
            return OwnFileHandle(fd, path);
        }

        internal static DirectoryIdentity? GetIdentity(SafeFileHandle handle)
        {
            if (s_mac)
            {
                const uint DeviceAndFileId = 0x02000002; // ATTR_CMN_FILEID | ATTR_CMN_DEVID
                const int IdentitySize = 16;
                var attributes = new MacAttributeList { BitmapCount = 5, CommonAttributes = DeviceAndFileId };
                return GetMacAttributes(handle, ref attributes, out MacFileIdentity info, IdentitySize, 0) == 0
                       && info.Size == IdentitySize && info.FileId != 0
                    ? new DirectoryIdentity(info.Device, info.FileId)
                    : null;
            }

            try
            {
                const int EmptyPath = 0x1000;
                const uint Inode = 0x100;
                return Statx(handle, "", EmptyPath, Inode, out LinuxFileIdentity info) == 0
                       && (info.Mask & Inode) != 0 && info.Inode != 0
                    ? new DirectoryIdentity(((ulong)info.DeviceMajor << 32) | info.DeviceMinor, info.Inode)
                    : null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        internal static bool Publish(SafeFileHandle source, string name, SafeFileHandle destination, string destinationName, string path)
        {
            try
            {
                const uint MacRenameExclusive = 4;
                const uint LinuxRenameNoReplace = 1;
                int result = s_mac
                    ? RenameAtMac(source, name, destination, destinationName, MacRenameExclusive)
                    : RenameAtLinux(source, name, destination, destinationName, LinuxRenameNoReplace);
                if (result == 0) return false;
                int error = Marshal.GetLastPInvokeError();
                bool unavailable = s_mac ? error is 45 or 78 or 102 : error is 38 or 95;
                if (!unavailable && error != InvalidArgument) throw FileError(path, error);
            }
            catch (EntryPointNotFoundException)
            {
                // Older libc versions may lack the exclusive rename wrapper.
            }

            // linkat also fails if the destination name is occupied. The caller owns both names
            // after this succeeds, so it can clean up even if removing the staging entry fails.
            if (LinkAt(source, name, destination, destinationName, 0) != 0)
                throw FileError(path, Marshal.GetLastPInvokeError());
            return true;
        }

        internal static void Delete(SafeFileHandle parent, string name, string path, bool directory)
        {
            int removeDirectory = s_mac ? 0x80 : 0x200;
            if (UnlinkAt(parent, name, directory ? removeDirectory : 0) == 0) return;
            int error = Marshal.GetLastPInvokeError();
            int notEmpty = s_mac ? 66 : 39;
            if (error != NoSuchEntry && !(directory && (error == notEmpty || error == EntryExists)))
                throw FileError(path, error);
        }

        private static int OpenChild(SafeFileHandle parent, string name, int flags, int mode = 0)
        {
            int fd;
            do
            {
                fd = s_macArm && mode != 0
                    ? OpenAtMacArm(parent, name, flags, 0, 0, 0, 0, 0, mode)
                    : OpenAt(parent, name, flags, mode);
            }
            while (fd < 0 && Marshal.GetLastPInvokeError() == Interrupted);
            return fd;
        }

        private static SafeFileHandle OwnFileHandle(int fd, string path)
        {
            if (fd < 0) throw FileError(path, Marshal.GetLastPInvokeError());
            // SafeFileHandle treats fd 0 as invalid, though open can return it when stdin is closed.
            if (fd == 0)
            {
                const int MacDuplicateCloseOnExec = 67;
                const int LinuxDuplicateCloseOnExec = 1030;
                fd = s_macArm ? FcntlMacArm(0, MacDuplicateCloseOnExec, 0, 0, 0, 0, 0, 0, 1)
                    : Fcntl(0, s_mac ? MacDuplicateCloseOnExec : LinuxDuplicateCloseOnExec, 1);
                int error = Marshal.GetLastPInvokeError();
                Close(0);
                if (fd < 0) throw FileError(path, error);
            }
            return new SafeFileHandle(fd, ownsHandle: true);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MacAttributeList
        {
            public ushort BitmapCount;
            public ushort Reserved;
            public uint CommonAttributes;
            public uint VolumeAttributes;
            public uint DirectoryAttributes;
            public uint FileAttributes;
            public uint ForkAttributes;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct MacFileIdentity
        {
            public uint Size;
            public uint Device;
            public ulong FileId;
        }

        // Linux statx has one ABI across the supported Linux architectures.
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxFileIdentity
        {
            [FieldOffset(0)] public uint Mask;
            [FieldOffset(32)] public ulong Inode;
            [FieldOffset(136)] public uint DeviceMajor;
            [FieldOffset(140)] public uint DeviceMinor;
        }

        [DllImport("libc", EntryPoint = "fgetattrlist")]
        private static extern int GetMacAttributes(SafeFileHandle handle, ref MacAttributeList attributes,
            out MacFileIdentity info, nuint length, nuint flags);

        [DllImport("libc", EntryPoint = "statx")]
        private static extern int Statx(SafeFileHandle handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags, uint mask, out LinuxFileIdentity info);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

        [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
        private static extern int OpenAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            int flags, int mode);

        // Darwin ARM64 passes C variadic arguments on the stack. Fill x3-x7 so mode is placed there.
        [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
        private static extern int OpenAtMacArm(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            int flags, nint x3, nint x4, nint x5, nint x6, nint x7, int mode);

        [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
        private static extern int MkdirAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int mode);

        [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
        private static extern int RenameAtLinux(SafeFileHandle source, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            SafeFileHandle destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, uint flags);

        [DllImport("libc", EntryPoint = "renameatx_np", SetLastError = true)]
        private static extern int RenameAtMac(SafeFileHandle source, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            SafeFileHandle destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, uint flags);

        [DllImport("libc", EntryPoint = "linkat", SetLastError = true)]
        private static extern int LinkAt(SafeFileHandle source, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            SafeFileHandle destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, int flags);

        [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
        private static extern int UnlinkAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

        [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        private static extern int Fcntl(int fd, int command, int argument);

        [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        private static extern int FcntlMacArm(int fd, int command, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, int argument);

        [DllImport("libc", EntryPoint = "close")]
        private static extern int Close(int fd);
    }
}
