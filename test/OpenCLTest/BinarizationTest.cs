using System;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class BinarizationTest
#if !GITHUB_ACTIONS
        : IDisposable
#endif
    {
        public const string FilePath = "../../../../../docs/example/original.png";

        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(FilePath);

            img.Binarization(127);
        }

#if !GITHUB_ACTIONS
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
        public void Gpu()
        {
            using var img = Image.Decode(FilePath);

            img.Binarization(127, context);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
#endif
    }
}