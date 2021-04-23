using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.PlatformLayer;
using BEditor.Compute.Runtime;

namespace BEditor.Drawing
{
    public class DrawingContext : IDisposable
    {
        public DrawingContext(CommandQueue commandQueue)
        {
            CommandQueue = commandQueue;
        }

        ~DrawingContext()
        {
            Dispose();
        }

        public Platform Platform => Device.Platform;

        public Device Device => CommandQueue.Device;

        public Context Context => CommandQueue.Context;

        public CommandQueue CommandQueue { get; }

        public bool IsDisposed { get; private set; }

        public Dictionary<string, CLProgram> Programs { get; } = new();

        public static DrawingContext Create(int platformindex)
        {
            var platform = new Platform(platformindex);
            var device = platform.CreateDevices(1)[0];
            var context = device.CreateContext();
            var queue = context.CreateCommandQueue(device);

            return new(queue);
        }

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