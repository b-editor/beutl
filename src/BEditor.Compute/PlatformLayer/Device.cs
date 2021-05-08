using System;
using System.Runtime.InteropServices;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute.PlatformLayer
{
    public unsafe class Device
    {
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

        public Platform Platform { get; }

        public int Index { get; }

        public void* Pointer { get; }

        public DeviceInfo Info { get; }

        public Context CreateContext()
        {
            return new Context(new Device[] { this });
        }
    }
}