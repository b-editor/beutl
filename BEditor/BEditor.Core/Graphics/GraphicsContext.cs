using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Primitive;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BEditor.Core.Graphics
{
    public unsafe sealed class GraphicsContext : IDisposable
    {
        private readonly GameWindow GameWindow;
        private static Shader textureShader;
        private static Shader Shader;
        private static Shader lightShader;

        public GraphicsContext(int width, int height)
        {
            Width = width;
            Height = height;
            GameWindow = new GameWindow(
                GameWindowSettings.Default,
                new()
                {
                    Size = new(width, height),
                    StartVisible = false
                });

            textureShader ??= Shader.FromFile(
                Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.vert"),
                Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.frag"));

            Shader ??= Shader.FromFile(
                Path.Combine(AppContext.BaseDirectory, "Shaders", "Shader.vert"),
                Path.Combine(AppContext.BaseDirectory, "Shaders", "Shader.frag"));

            lightShader ??= Shader.FromFile(
                Path.Combine(AppContext.BaseDirectory, "Shaders", "Shader.vert"),
                Path.Combine(AppContext.BaseDirectory, "Shaders", "Light.frag"));


            Camera = new OrthographicCamera(new(0, 0, 1024), width, height);

            Clear();
        }
        ~GraphicsContext()
        {
            if (!IsDisposed) Dispose();
        }

        public int Width { get; }
        public int Height { get; }
        public float Aspect => ((float)Width) / ((float)Height);
        public bool IsCurrent => GameWindow.Context.IsCurrent;
        public bool IsDisposed { get; private set; }
        public Camera Camera { get; set; }

        public void Clear()
        {
            MakeCurrent();

            //法線の自動調節
            //GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            //GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            GL.Disable(EnableCap.DepthTest);
            //GL.Disable(EnableCap.Lighting);

            GL.ClearColor(Color.FromHTML(Settings.Default.BackgroundColor).ToOpenTK());
        }
        public void MakeCurrent()
        {
            try
            {
                if (!IsCurrent)
                    GameWindow.MakeCurrent();
            }
            catch
            {
                Debug.Assert(false);
            }
        }
        public void SwapBuffers()
        {
            GameWindow.Context.SwapBuffers();
        }
        internal void DrawImage(Image<BGRA32> img, ClipData data, EffectRenderArgs args)
        {
            if (img == null) return;

            #region MyRegion

            var frame = args.Frame;
            var drawObject = data.Effect[0] as ImageObject;

            float alpha = (float)(drawObject.Blend.Alpha.GetValue(frame) / 100);

            var scale = (float)(drawObject.Zoom.Scale.GetValue(frame) / 100);
            var scalex = (float)(drawObject.Zoom.ScaleX.GetValue(frame) / 100) * scale;
            var scaley = (float)(drawObject.Zoom.ScaleY.GetValue(frame) / 100) * scale;
            var scalez = (float)(drawObject.Zoom.ScaleZ.GetValue(frame) / 100) * scale;

            var coordinate = new System.Numerics.Vector3(
                drawObject.Coordinate.X.GetValue(frame),
                drawObject.Coordinate.Y.GetValue(frame),
                drawObject.Coordinate.Z.GetValue(frame));

            var center = new System.Numerics.Vector3(
                drawObject.Coordinate.CenterX.GetValue(frame),
                drawObject.Coordinate.CenterY.GetValue(frame),
                drawObject.Coordinate.CenterZ.GetValue(frame));


            var nx = drawObject.Angle.AngleX.GetValue(frame);
            var ny = drawObject.Angle.AngleY.GetValue(frame);
            var nz = drawObject.Angle.AngleZ.GetValue(frame);

            Color ambient = drawObject.Material.Ambient.GetValue(frame);
            Color diffuse = drawObject.Material.Diffuse.GetValue(frame);
            Color specular = drawObject.Material.Specular.GetValue(frame);
            float shininess = drawObject.Material.Shininess.GetValue(frame);
            var c = drawObject.Blend.Color.GetValue(frame);
            var color = Color.FromARGB((byte)(c.A * alpha), c.R, c.G, c.B);

            #endregion

            MakeCurrent();

            using var texture = Texture.FromImage(img);
            texture.Use(TextureUnit.Texture0);
            //using var textureShader = Shader.FromFile(
            //    Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.vert"),
            //    Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.frag"));

            textureShader.Use();

            var vertexLocation = textureShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = textureShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            textureShader.SetInt("texture", 0);

            GL.Enable(EnableCap.Blend);

            var blendFunc = Blend.BlentFunc[drawObject.Blend.BlendType.Index];

            blendFunc?.Invoke();
            if (blendFunc is null)
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            //GL.Color4(color.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Ambient, ambient.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, diffuse.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Specular, specular.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            GL.Enable(EnableCap.Texture2D);

            var model = Matrix4.Identity
                * Matrix4.CreateTranslation(center.ToOpenTK())
                    * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(nx))
                    * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(ny))
                    * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(nz))
                        * Matrix4.CreateTranslation(coordinate.ToOpenTK())
                            * Matrix4.CreateScale(scalex, scaley, scalez);

            textureShader.SetVector4("color", color.ToVector4());
            textureShader.SetMatrix4("model", model);
            textureShader.SetMatrix4("view", Camera.GetViewMatrix());
            textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            textureShader.Use();

            texture.Render(TextureUnit.Texture0);
        }
        public void DrawTexture(Texture texture, Transform transform, Color color)
        {
            MakeCurrent();
            texture.Use(TextureUnit.Texture0);

            textureShader.Use();

            var vertexLocation = textureShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = textureShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            textureShader.SetInt("texture", 0);

            GL.Enable(EnableCap.Blend);


            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Enable(EnableCap.Texture2D);

            textureShader.SetVector4("color", color.ToVector4());
            textureShader.SetMatrix4("model", transform.Matrix);
            textureShader.SetMatrix4("view", Camera.GetViewMatrix());
            textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            textureShader.Use();

            texture.Render(TextureUnit.Texture0);
        }
        public void DrawCube(Cube cube, Transform transform)
        {
            MakeCurrent();

            Shader.Use();

            var vertexLocation = Shader.GetAttribLocation("aPos");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);


            GL.BindVertexArray(cube.VertexArrayObject);

            Shader.SetMatrix4("model", transform.Matrix);
            Shader.SetMatrix4("view", Camera.GetViewMatrix());
            Shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
            Shader.SetVector4("color", cube.Color.ToVector4());

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }
        public void Dispose()
        {
            if (IsDisposed) return;

            GameWindow.Dispose();

            IsDisposed = true;
        }
        public unsafe void ReadImage(Image<BGRA32> image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrent();

            GL.ReadBuffer(ReadBufferMode.Front);

            fixed (BGRA32* data = image.Data)
                GL.ReadPixels(0, 0, image.Width, image.Height, PixelFormat.Bgra, PixelType.UnsignedByte, (IntPtr)data);

            image.Flip(FlipMode.X);
        }
    }
}
