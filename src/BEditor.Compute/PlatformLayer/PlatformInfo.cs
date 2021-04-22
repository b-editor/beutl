using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    public unsafe class PlatformInfo
    {
        private readonly Dictionary<string, byte[]> _infos = new();

        internal PlatformInfo(int index)
        {
            Index = index;

            // get a platform
            uint count = 0;
            CL.GetPlatformIDs(0, null, &count).CheckError();
            var platforms = (void**)Marshal.AllocCoTaskMem((int)(count * IntPtr.Size));
            void* platform;
            try
            {
                CL.GetPlatformIDs(count, platforms, &count).CheckError();
                platform = platforms[index];
            }
            finally
            {
                Marshal.FreeCoTaskMem(new IntPtr(platforms));
            }

            // get platform infos
            foreach (long info in Enum.GetValues(typeof(CLPlatformInfo)))
            {
                var size = new IntPtr();
                CL.GetPlatformInfo(platform, info, IntPtr.Zero, null, &size).CheckError();
                var value = new byte[(int)size];
                fixed (byte* valuePointer = value)
                {
                    CL.GetPlatformInfo(platform, info, size, valuePointer, null).CheckError();
                    _infos.Add(Enum.GetName(typeof(CLPlatformInfo), info)!, value);
                }
            }

            // get devices
            CL.GetDeviceIDs(platform, (long)CLDeviceType.CL_DEVICE_TYPE_ALL, 0, null, &count).CheckError();

            // create device infos
            for (var i = 0; i < count; i++)
            {
                DeviceInfos.Add(new DeviceInfo(platform, i));
            }
        }

        public int Index { get; }

        public List<DeviceInfo> DeviceInfos { get; } = new();

        public List<string> Keys => _infos.Keys.ToList();

        public string this[string key]
        {
            get => Encoding.UTF8.GetString(_infos[key], 0, _infos[key].Length).Trim();
        }

        public string GetValueAsString(string key)
        {
            return Encoding.UTF8.GetString(_infos[key], 0, _infos[key].Length).Trim();
        }
    }
}