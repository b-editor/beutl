using System;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class ChromaKeyTest
#if !GITHUB_ACTIONS
        : IDisposable
#endif
    {
        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.ChromaKey(Colors.Green, 80, 80);
        }

#if !GITHUB_ACTIONS
        private readonly DrawingContext context;

        public ChromaKeyTest()
        {
            context = DrawingContext.Create(0);

            var op = (ChromaKeyOperation)default;
            var prog = context.Context.CreateProgram(op.GetSource());
            var key = op.GetType().Name;

            context.Programs.Add(key, prog);
        }

        [Test]
        public void Gpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.ChromaKey(Colors.Green, 80, 80, context);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
#endif
    }
}