using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    public unsafe class DeviceInfo
    {
        private readonly Dictionary<string, byte[]> _infos = new();

        internal DeviceInfo(void* platform, int index)
        {
            Index = index;

            // get a device
            uint count = 0;
            CL.GetDeviceIDs(platform, (long)CLDeviceType.CL_DEVICE_TYPE_ALL, 0, null, &count).CheckError();
            var devices = (void**)Marshal.AllocCoTaskMem((int)(count * IntPtr.Size));
            try
            {
                CL.GetDeviceIDs(platform, (long)CLDeviceType.CL_DEVICE_TYPE_ALL, count, devices, &count).CheckError();

                // get device infos
                foreach (long info in Enum.GetValues(typeof(CLDeviceInfo)))
                {
                    var a = Enum.GetName(typeof(CLDeviceInfo), info);
                    var size = new IntPtr();
                    CL.GetDeviceInfo(devices[index], info, IntPtr.Zero, null, &size).CheckError();
                    var value = new byte[(int)size];
                    fixed (byte* valuePointer = value)
                    {
                        CL.GetDeviceInfo(devices[index], info, size, valuePointer, null).CheckError();
                        _infos.Add(Enum.GetName(typeof(CLDeviceInfo), info), value);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(new IntPtr(devices));
            }
        }

        public int Index { get; }

        public List<string> Keys => _infos.Keys.ToList();

        public string GetValueAsString(string key)
        {
            return Encoding.UTF8.GetString(_infos[key], 0, _infos[key].Length).Trim();
        }

        public bool GetValueAsBool(string key)
        {
            return BitConverter.ToBoolean(_infos[key], 0);
        }

        public uint GetValueAsUInt(string key)
        {
            return BitConverter.ToUInt32(_infos[key], 0);
        }

        public ulong GetValueAsULong(string key)
        {
            return BitConverter.ToUInt64(_infos[key], 0);
        }

        public ulong GetValueAsSizeT(string key)
        {
            if (IntPtr.Size == 4)
                return BitConverter.ToUInt32(_infos[key], 0);
            else
                return BitConverter.ToUInt64(_infos[key], 0);
        }

        public ulong[] GetValueAsSizeTArray(string key)
        {
            if (IntPtr.Size == 4)
            {
                var num = _infos[key].Length / 4;
                var array = new ulong[num];
                for (var i = 0; i < num; i++)
                    array[i] = BitConverter.ToUInt32(_infos[key], 4 * i);
                return array;
            }
            else
            {
                var num = _infos[key].Length / 8;
                var array = new ulong[num];
                for (var i = 0; i < num; i++)
                    array[i] = BitConverter.ToUInt64(_infos[key], 8 * i);
                return array;
            }
        }

        public CLDeviceType GetValueAsClDeviceType(string key)
        {
            return (CLDeviceType)BitConverter.ToInt64(_infos[key], 0);
        }

        public CLDeviceFpConfig GetValueAsClDeviceFpConfig(string key)
        {
            return (CLDeviceFpConfig)BitConverter.ToInt64(_infos[key], 0);
        }

        public CLDeviceMemoryCacheType GetValueAsClDeviceMemCacheType(string key)
        {
            return (CLDeviceMemoryCacheType)BitConverter.ToInt64(_infos[key], 0);
        }

        public CLDeviceLocalMemoryType GetValueAsClDeviceLocalMemType(string key)
        {
            return (CLDeviceLocalMemoryType)BitConverter.ToInt64(_infos[key], 0);
        }

        public CLDeviceExecCapabilities GetValueAsClDeviceExecCapabilities(string key)
        {
            return (CLDeviceExecCapabilities)BitConverter.ToInt64(_infos[key], 0);
        }

        public CLCommandQueueProperties GetValueAsClCommandQueueProperties(string key)
        {
            return (CLCommandQueueProperties)BitConverter.ToInt64(_infos[key], 0);
        }
    }
}