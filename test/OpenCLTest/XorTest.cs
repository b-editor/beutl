using System;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class XorTest
#if !GITHUB_ACTIONS
        : IDisposable
#endif
    {
        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Xor();
        }

#if !GITHUB_ACTIONS
        private readonly DrawingContext context;

        public XorTest()
        {
            context = DrawingContext.Create(0);

            var op = (XorOperation)default;
            var prog = context.Context.CreateProgram(op.GetSource());
            var key = op.GetType().Name;

            context.Programs.Add(key, prog);
        }

        [Test]
        public void Gpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Xor(context);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
#endif
    }
}
