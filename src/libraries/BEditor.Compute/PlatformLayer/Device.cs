// Device.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    /// <summary>
    /// Represents the OpenCL device.
    /// </summary>
    public unsafe class Device
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Device"/> class.
        /// </summary>
        /// <param name="platform">The platform.</param>
        /// <param name="index">The index of devices in the platform.</param>
        public Device(Platform platform, int index)
        {
            Platform = platform;
            Index = index;

            // get a device
            uint count = 0;
            CL.GetDeviceIDs(platform.Pointer, (long)CLDeviceType.CL_DEVICE_TYPE_ALL, 0, null, &count).CheckError();

            var devices = (void**)Marshal.AllocCoTaskMem((int)(count * IntPtr.Size));
            try
            {
                CL.GetDeviceIDs(platform.Pointer, (long)CLDeviceType.CL_DEVICE_TYPE_ALL, count, devices, &count).CheckError();
                Pointer = devices[index];
            }
            finally
            {
                Marshal.FreeCoTaskMem(new IntPtr(devices));
            }

            Info = Platform.PlatformInfos[platform.Index].DeviceInfos[index];
        }

        /// <summary>
        /// Gets the platform.
        /// </summary>
        public Platform Platform { get; }

        /// <summary>
        /// Gets the index of devices in the platform.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Gets the device info.
        /// </summary>
        public DeviceInfo Info { get; }

        /// <summary>
        /// Creates the context.
        /// </summary>
        /// <returns>Returns the created <see cref="Context"/>.</returns>
        public Context CreateContext()
        {
            return new Context(new Device[] { this });
        }
    }
}