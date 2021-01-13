using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

#if !OldOpenTK
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
#else

#endif
using Color = BEditor.Drawing.Color;
using System.Runtime.InteropServices;
using BEditor.Core.Renderings;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Graphics
{
    public abstract class BaseGraphicsContext : IDisposable
    {
        public BaseGraphicsContext(int width, int height)
        {
            Width = width;
            Height = height;
        }


        public virtual int Width { get; private set; }
        public virtual int Height { get; private set; }
        public float Aspect => ((float)Width) / ((float)Height);

        public abstract void MakeCurrent();
        public abstract void SwapBuffers();
        public virtual void Dispose()
        {

        }
        public virtual void Clear()
        {
            MakeCurrent();

            //法線の自動調節
            GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);

            GL.ClearColor(Color.FromHTML(Settings.Default.BackgroundColor).ToOpenTK());
        }

        protected void Initialize()
        {
            GL.Viewport(0, 0, Width, Height);

            //法線の自動調節
            GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);

            GL.ClearColor(Color.FromHTML(Settings.Default.BackgroundColor).ToOpenTK());
        }

        
        public void ReadPixels(Image<BGRA32> image)
        {
            MakeCurrent();
            GLTK.GetPixels(image);
        }
    }
}
