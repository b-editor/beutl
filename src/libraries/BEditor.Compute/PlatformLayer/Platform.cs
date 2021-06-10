// Platform.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    /// <summary>
    /// Represents the OpenCL platform.
    /// </summary>
    public unsafe class Platform
    {
        static Platform()
        {
            // get platforms
            uint count = 0;
            CL.GetPlatformIDs(0, null, &count).CheckError();

            // create platform infos
            for (var i = 0; i < count; i++)
            {
                PlatformInfos.Add(new PlatformInfo(i));
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Platform"/> class.
        /// </summary>
        /// <param name="index">The index of platforms.</param>
        public Platform(int index)
        {
            Index = index;

            // get a platform
            uint count = 0;
            CL.GetPlatformIDs(0, null, &count).CheckError();
            var platforms = (void**)Marshal.AllocCoTaskMem((int)(count * IntPtr.Size));

            try
            {
                CL.GetPlatformIDs(count, platforms, &count).CheckError();
                Pointer = platforms[index];
            }
            finally
            {
                Marshal.FreeCoTaskMem(new IntPtr(platforms));
            }

            Info = PlatformInfos[index];
        }

        /// <summary>
        /// Gets the information about the platforms.
        /// </summary>
        public static List<PlatformInfo> PlatformInfos { get; } = new List<PlatformInfo>();

        /// <summary>
        /// Gets the index.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        public void* Pointer { get; }

        /// <summary>
        /// Gets the platform info.
        /// </summary>
        public PlatformInfo Info { get; }

        /// <summary>
        /// Creates the devices.
        /// </summary>
        /// <param name="indices">The indices.</param>
        /// <returns>Returns the created <see cref="Device"/>.</returns>
        public Device[] CreateDevices(params int[] indices)
        {
            return indices
                .Select(i => new Device(this, i))
                .ToArray();
        }
    }
}