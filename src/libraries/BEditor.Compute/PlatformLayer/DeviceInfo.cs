// DeviceInfo.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    /// <summary>
    /// Represents the OpenCL device info.
    /// </summary>
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
                    var size = IntPtr.Zero;
                    CL.GetDeviceInfo(devices[index], info, IntPtr.Zero, null, &size).CheckError();
                    var value = new byte[(int)size];
                    fixed (byte* valuePointer = value)
                    {
                        CL.GetDeviceInfo(devices[index], info, size, valuePointer, null).CheckError();
                        _infos.Add(Enum.GetName(typeof(CLDeviceInfo), info)!, value);
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(new IntPtr(devices));
            }
        }

        /// <summary>
        /// Gets the index of devices in the platform.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the keys.
        /// </summary>
        public List<string> Keys => _infos.Keys.ToList();

        /// <summary>
        /// Gets the value as string.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public string GetValueAsString(string key)
        {
            return Encoding.UTF8.GetString(_infos[key], 0, _infos[key].Length).Trim();
        }

        /// <summary>
        /// Gets the value as boolean.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public bool GetValueAsBool(string key)
        {
            return BitConverter.ToBoolean(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as 32-bit unsigned integer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public uint GetValueAsUInt(string key)
        {
            return BitConverter.ToUInt32(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as 64-bit unsigned integer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public ulong GetValueAsULong(string key)
        {
            return BitConverter.ToUInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as native unsigned integer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public ulong GetValueAsSizeT(string key)
        {
            if (IntPtr.Size == 4)
            {
                return BitConverter.ToUInt32(_infos[key], 0);
            }
            else
            {
                return BitConverter.ToUInt64(_infos[key], 0);
            }
        }

        /// <summary>
        /// Gets the value as native unsigned integer.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public ulong[] GetValueAsSizeTArray(string key)
        {
            if (IntPtr.Size == 4)
            {
                var num = _infos[key].Length / 4;
                var array = new ulong[num];
                for (var i = 0; i < num; i++)
                {
                    array[i] = BitConverter.ToUInt32(_infos[key], 4 * i);
                }

                return array;
            }
            else
            {
                var num = _infos[key].Length / 8;
                var array = new ulong[num];
                for (var i = 0; i < num; i++)
                {
                    array[i] = BitConverter.ToUInt64(_infos[key], 8 * i);
                }

                return array;
            }
        }

        /// <summary>
        /// Gets the value as <see cref="CLDeviceType"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLDeviceType GetValueAsClDeviceType(string key)
        {
            return (CLDeviceType)BitConverter.ToInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as <see cref="CLDeviceFpConfig"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLDeviceFpConfig GetValueAsClDeviceFpConfig(string key)
        {
            return (CLDeviceFpConfig)BitConverter.ToInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as <see cref="CLDeviceMemoryCacheType"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLDeviceMemoryCacheType GetValueAsClDeviceMemCacheType(string key)
        {
            return (CLDeviceMemoryCacheType)BitConverter.ToInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as <see cref="CLDeviceLocalMemoryType"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLDeviceLocalMemoryType GetValueAsClDeviceLocalMemType(string key)
        {
            return (CLDeviceLocalMemoryType)BitConverter.ToInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as <see cref="CLDeviceExecCapabilities"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLDeviceExecCapabilities GetValueAsClDeviceExecCapabilities(string key)
        {
            return (CLDeviceExecCapabilities)BitConverter.ToInt64(_infos[key], 0);
        }

        /// <summary>
        /// Gets the value as <see cref="CLCommandQueueProperties"/>.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public CLCommandQueueProperties GetValueAsClCommandQueueProperties(string key)
        {
            return (CLCommandQueueProperties)BitConverter.ToInt64(_infos[key], 0);
        }
    }
}