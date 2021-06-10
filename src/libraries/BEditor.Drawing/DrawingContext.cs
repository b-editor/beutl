// DrawingContext.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

using BEditor.Compute.OpenCL;
using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Drawing
{
    /// <summary>
    /// The context used for pixel manipulation on the GPU.
    /// </summary>
    public class DrawingContext : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DrawingContext"/> class.
        /// </summary>
        /// <param name="commandQueue">The command queue.</param>
        public DrawingContext(CommandQueue commandQueue)
        {
            CommandQueue = commandQueue;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="DrawingContext"/> class.
        /// </summary>
        ~DrawingContext()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the platform.
        /// </summary>
        public Platform Platform => Device.Platform;

        /// <summary>
        /// Gets the device.
        /// </summary>
        public Device Device => CommandQueue.Device;

        /// <summary>
        /// Gets the context.
        /// </summary>
        public Context Context => CommandQueue.Context;

        /// <summary>
        /// Gets the command queue.
        /// </summary>
        public CommandQueue CommandQueue { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets the cached programs.
        /// </summary>
        public Dictionary<string, CLProgram> Programs { get; } = new();

        /// <summary>
        /// Creates the drawing context.
        /// </summary>
        /// <param name="platformindex">The index of the platform.</param>
        /// <returns>Returns an instance of <see cref="DrawingContext"/> on success, or <see langword="null"/> on failure.</returns>
        public static unsafe DrawingContext? Create(int platformindex)
        {
            try
            {
                uint c;
                if ((CLStatusCode)CL.GetPlatformIDs(1, null, &c) is not CLStatusCode.CL_SUCCESS)
                {
                    return null;
                }

                var platform = new Platform(platformindex);
                var device = platform.CreateDevices(1)[0];
                var context = device.CreateContext();
                var queue = context.CreateCommandQueue(device);

                return new(queue);
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                CommandQueue.Dispose();
                Context.Dispose();

                IsDisposed = true;
            }

            GC.SuppressFinalize(this);
        }
    }
}