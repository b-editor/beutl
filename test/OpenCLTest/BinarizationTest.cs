using System;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class BinarizationTest : IDisposable
    {
        public const string FilePath = "../../../../../docs/example/original.png";
        private readonly DrawingContext context;

        public BinarizationTest()
        {
            context = DrawingContext.Create(0);

            var op = (BinarizationOperation)default;
            var prog = context.Context.CreateProgram(op.GetSource());
            var key = op.GetType().Name;

            context.Programs.Add(key, prog);
        }

        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(FilePath);

            img.Binarization(127);
        }

        [Test]
        public void Gpu()
        {
            using var img = Image.Decode(FilePath);

            img.Binarization(context, 127);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
