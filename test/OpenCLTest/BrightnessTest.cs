using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.PixelOperation;

using NUnit.Framework;

namespace OpenCLTest
{
    public class BrightnessTest
#if !GITHUB_ACTIONS
        : IDisposable
#endif
    {
        [Test]
        public void Cpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Brightness(128);
        }

#if !GITHUB_ACTIONS
        private readonly DrawingContext context;

        public BrightnessTest()
        {
            context = DrawingContext.Create(0);

            var op = (BrightnessOperation)default;
            var prog = context.Context.CreateProgram(op.GetSource());
            var key = op.GetType().Name;

            context.Programs.Add(key, prog);
        }

        [Test]
        public void Gpu()
        {
            using var img = Image.Decode(BinarizationTest.FilePath);

            img.Brightness(context, 128);
        }

        public void Dispose()
        {
            context.Dispose();

            GC.SuppressFinalize(this);
        }
#endif
    }
}
