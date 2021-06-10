// PlatformInfo.cs
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
    /// Represents the OpenCL platform info.
    /// </summary>
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
                var size = IntPtr.Zero;
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

        /// <summary>
        /// Gets the index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the information about the devices.
        /// </summary>
        public List<DeviceInfo> DeviceInfos { get; } = new();

        /// <summary>
        /// Gets the keys.
        /// </summary>
        public List<string> Keys => _infos.Keys.ToList();

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>Returns the value.</returns>
        public string this[string key]
        {
            get => Encoding.UTF8.GetString(_infos[key], 0, _infos[key].Length).Trim();
        }
    }
}