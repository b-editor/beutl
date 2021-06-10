// CL.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;

namespace BEditor.Compute.OpenCL
{
#pragma warning disable CA1401, CS1591, SA1600

    /// <summary>
    /// Provides OpenCL functions.
    /// </summary>
    public static unsafe class CL
    {
        private const string Library = "opencl";
        private static readonly bool _registeredResolver = false;

        static CL()
        {
            static string GetLibraryName()
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
                {
                    return "libOpenCL.so.1";
                }
                else if (OperatingSystem.IsWindows())
                {
                    return "OpenCL.dll";
                }
                else if (OperatingSystem.IsIOS() || OperatingSystem.IsMacOS())
                {
                    return "/System/Library/Frameworks/OpenCL.framework/OpenCL";
                }
                else
                {
                    throw new NotSupportedException($"The library name couldn't be resolved for the given platform ('{RuntimeInformation.OSDescription}').");
                }
            }

            if (!_registeredResolver)
            {
                NativeLibrary.SetDllImportResolver(typeof(CL).Assembly, (_, assembly, path) =>
                {
                    var libName = GetLibraryName();

                    if (!NativeLibrary.TryLoad(libName, assembly, path, out var libHandle))
                    {
                        throw new DllNotFoundException(
                            $"Could not load the dll '{libName}' (this load is intercepted, specified in DllImport as '{Library}').");
                    }

                    return libHandle;
                });

                _registeredResolver = true;
            }
        }

        [DllImport(Library, EntryPoint = "clGetPlatformIDs")]
        public static extern int GetPlatformIDs(uint num_entries, void** platforms, uint* num_platforms);

        [DllImport(Library, EntryPoint = "clGetPlatformInfo")]
        public static extern int GetPlatformInfo(void* platform, long param_name, IntPtr param_value_size, void* param_value, void* param_value_size_ret);

        [DllImport(Library, EntryPoint = "clGetDeviceIDs")]
        public static extern int GetDeviceIDs(void* platform, long device_type, uint num_entries, void** devices, uint* num_devices);

        [DllImport(Library, EntryPoint = "clGetDeviceInfo")]
        public static extern int GetDeviceInfo(void* device, long param_name, IntPtr param_value_size, void* param_value, void* param_value_size_ret);

        [DllImport(Library, EntryPoint = "clCreateContext")]
        public static extern void* CreateContext(void* properties, uint num_devices, void** devices, void* notify, void* user_data, int* error_code);

        [DllImport(Library, EntryPoint = "clReleaseContext")]
        public static extern int ReleaseContext(void* context);

        [DllImport(Library, EntryPoint = "clCreateCommandQueue")]
        public static extern void* CreateCommandQueue(void* context, void* device, CLCommandQueueProperties properties, int* error_code);

        [DllImport(Library, EntryPoint = "clReleaseCommandQueue")]
        public static extern int ReleaseCommandQueue(void* command_queue);

        [DllImport(Library, EntryPoint = "clCreateProgramWithSource")]
        public static extern void* CreateProgramWithSource(void* context, int count, byte** strings, void* lengths, int* error_code);

        [DllImport(Library, EntryPoint = "clBuildProgram")]
        public static extern int BuildProgram(void* program, uint num_devices, void** device_list, byte* options, void* notify, void* user_data);

        [DllImport(Library, EntryPoint = "clGetProgramBuildInfo")]
        public static extern int GetProgramBuildInfo(void* program, void* device, CLProgramBuildInfo param_name, IntPtr param_value_size, void* param_value, void* param_value_size_ret);

        [DllImport(Library, EntryPoint = "clReleaseProgram")]
        public static extern int ReleaseProgram(void* program);

        [DllImport(Library, EntryPoint = "clCreateKernel")]
        public static extern void* CreateKernel(void* program, byte* kernel_name, int* error_code);

        [DllImport(Library, EntryPoint = "clReleaseKernel")]
        public static extern int ReleaseKernel(void* kernel);

        [DllImport(Library, EntryPoint = "clCreateBuffer")]
        public static extern void* CreateBuffer(void* context, CLMemoryFlags flags, IntPtr size, void* host_ptr, int* error_code);

        [DllImport(Library, EntryPoint = "clEnqueueReadBuffer")]
        public static extern int EnqueueReadBuffer(void* command_queue, void* buffer, bool blocking_read, IntPtr offset, IntPtr cb, void* ptr, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clEnqueueWriteBuffer")]
        public static extern int EnqueueWriteBuffer(void* command_queue, void* buffer, bool blocking_write, IntPtr offset, IntPtr cb, void* ptr, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clEnqueueMapBuffer")]
        public static extern void* EnqueueMapBuffer(void* command_queue, void* buffer, bool blocking_map, CLMapFlags map_flags, IntPtr offset, IntPtr size, uint num_events_in_wait_list, void* event_wait_list, void** event_, int* error_code);

        [DllImport(Library, EntryPoint = "clEnqueueUnmapMemObject")]
        public static extern int EnqueueUnmapMemObject(void* command_queue, void* memobj, void* mapped_ptr, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clReleaseMemObject")]
        public static extern int ReleaseMemObject(void* mem);

        [DllImport(Library, EntryPoint = "clSVMAlloc")]
        public static extern void* SVMAlloc(void* context, CLMemoryFlags flags, IntPtr size, uint alignment);

        [DllImport(Library, EntryPoint = "clEnqueueSVMMap")]
        public static extern int EnqueueSVMMap(void* command_queue, bool blocking_map, CLMapFlags map_flags, void* svm_ptr, IntPtr size, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clEnqueueSVMUnmap")]
        public static extern int EnqueueSVMUnmap(void* command_queue, void* svm_ptr, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clSVMFree")]
        public static extern void SVMFree(void* context, void* svm_pointer);

        [DllImport(Library, EntryPoint = "clSetKernelArg")]
        public static extern int SetKernelArg(void* kernel, int arg_index, int arg_size, void* arg_value);

        [DllImport(Library, EntryPoint = "clSetKernelArgSVMPointer")]
        public static extern int SetKernelArgSVMPointer(void* kernel, int arg_index, void* arg_value);

        [DllImport(Library, EntryPoint = "clEnqueueNDRangeKernel")]
        public static extern int EnqueueNDRangeKernel(void* command_queue, void* kernel, uint work_dim, IntPtr* global_work_offset, IntPtr* gloal_work_size, IntPtr* local_work_size, uint num_events_in_wait_list, void* event_wait_list, void** event_);

        [DllImport(Library, EntryPoint = "clWaitForEvents")]
        public static extern int WaitForEvents(uint num_events, void** event_list);

        [DllImport(Library, EntryPoint = "clGetEventProfilingInfo")]
        public static extern int GetEventProfilingInfo(void* event_, CLProfilingInfo param_name, IntPtr param_value_size, void* param_value, IntPtr* param_value_size_ret);

        [DllImport(Library, EntryPoint = "clFinish")]
        public static extern int Finish(void* command_queue);
    }
#pragma warning restore CA1401, CS1591, SA1600
}