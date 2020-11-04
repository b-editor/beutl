using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using BEditor.Core.Renderer;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BEditor.Models {
    public sealed class RenderingContext : BaseRenderingContext {
        public override int Width { get => GLControl.Width; }
        public override int Height { get => GLControl.Height; }

        private GLControl GLControl = new GLControl();

        public RenderingContext(int width, int height) : base(width, height) {
            GLControl.Width = width;
            GLControl.Height = height;
            Initialize();
        }

        public override void MakeCurrent() => GLControl.MakeCurrent();
        public override void SwapBuffers() => GLControl.SwapBuffers();

        public override void Resize(in int width, in int height, in bool Perspective = false, in float x = 0, in float y = 0, in float z = 1024, in float tx = 0, in float ty = 0, in float tz = 0, in float near = 0.1F, in float far = 20000) {
            base.Resize(width, height, Perspective, x, y, z, tx, ty, tz, near, far);
            GLControl.Width = width;
            GLControl.Height = height;
        }

        ~RenderingContext() {
            GLControl.Dispose();
        }
    }
}
