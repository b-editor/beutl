using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    public unsafe class Platform
    {
        public static List<PlatformInfo> PlatformInfos { get; } = new List<PlatformInfo>();

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

        public int Index { get; }
        public void* Pointer { get; }
        public PlatformInfo Info { get; }

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

        public Device[] CreateDevices(params int[] indices)
        {
            return indices
                .Select(i => new Device(this, i))
                .ToArray();
        }
    }
}