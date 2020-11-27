using NUnit.Framework;

using System.IO;
using System.Runtime.InteropServices;

namespace NUnitTestProject1
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        public void PtrTest()
        {
            var point = new Point();
            unsafe
            {
                // Allocでボックス化 (コピー)
                var handle = GCHandle.Alloc(point);
                var ptr = (Point*)GCHandle.ToIntPtr(handle);

                ptr->X = 100;

                handle.Free();
            }

            ref var a = ref point;
        }
    }

    public struct Point
    {
        public double X;
        public double Y;
    }
}