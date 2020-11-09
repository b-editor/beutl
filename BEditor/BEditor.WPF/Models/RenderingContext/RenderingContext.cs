using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using BEditor.Core.Data.ObjectData;
using BEditor.Core.Media;
using BEditor.Core.Renderer;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BEditor.Models {
    public sealed class RenderingContext : BaseRenderingContext {
        public override int Width { get => GLControl.Width; }
        public override int Height { get => GLControl.Height; }

        private GLControl GLControl = new GLControl(GraphicsMode);

        static GraphicsMode GraphicsMode = new GraphicsMode(
            GraphicsMode.Default.ColorFormat,
            GraphicsMode.Default.Depth,
            8,//GraphicsMode.Default.Stencil,
            8,//GraphicsMode.Default.Samples,
            GraphicsMode.Default.AccumulatorFormat,
            GraphicsMode.Default.Buffers,
            GraphicsMode.Default.Stereo);

        public RenderingContext(int width, int height) : base(width, height) {
            GLControl.Width = width;
            GLControl.Height = height;

            Initialize();
        }

        public override void MakeCurrent() {
            GLControl.MakeCurrent();
            //MakeCurrent(HDC, HRC);
        }

        public override void SwapBuffers() {
            GLControl.SwapBuffers();
        }

        public override void Resize(int width, int height, bool Perspective = false, float x = 0, float y = 0, float z = 1024, float tx = 0, float ty = 0, float tz = 0, float near = 0.1F, float far = 20000) {
            base.Resize(width, height, Perspective, x, y, z, tx, ty, tz, near, far);
            GLControl.Width = width;
            GLControl.Height = height;
        }

        public override void Dispose() {
            base.Dispose();

            //DeleteRenderingContext(HDC, HRC, BMP);
        }
    }
}
