using System;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class SepiaTest : IDisposable
    {
        private readonly DrawingContext context;

        public SepiaTest()
        {
            context = DrawingContext.Create(0);

            var op = (SepiaOperation)default;
            var prog = context.Context.CreateProgram(op.GetSource());
            var key = op.GetType().Name;

            context.Programs.Add(key, prog);
        }

        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Sepia();
        }

        [Test]
        public void Gpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Sepia(context);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
