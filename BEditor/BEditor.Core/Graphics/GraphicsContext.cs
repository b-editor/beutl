using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace BEditor.Core.Graphics
{
    public unsafe sealed class GraphicsContext : BaseGraphicsContext
    {
        private readonly GameWindow GameWindow;

        public GraphicsContext(int width, int height) : base(width, height)
        {
            GameWindow = new GameWindow(
                GameWindowSettings.Default,
                new()
                {
                    Size = new(width, height),
                    StartVisible = false
                });
            Camera = new OrthographicCamera(new(0, 0, 1024), width, height);
            Initialize();
        }

        public Camera Camera { get; set; }

        public override void MakeCurrent()
        {
            try
            {
                GameWindow.MakeCurrent();
            }
            catch
            {
                Debug.Assert(false);
            }
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
            using var shader = Shader.FromFile(
                Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.vert"),
                Path.Combine(AppContext.BaseDirectory, "Shaders", "TextureShader.frag"));

            shader.Use();

            var vertexLocation = shader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = shader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            shader.SetInt("texture", 0);

            GL.Enable(EnableCap.Blend);

            var blendFunc = Blend.BlentFunc[drawObject.Blend.BlendType.Index];

            blendFunc?.Invoke();
            if (blendFunc is null)
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.Color4(color.ToOpenTK());
            GL.Material(MaterialFace.Front, MaterialParameter.Ambient, ambient.ToOpenTK());
            GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, diffuse.ToOpenTK());
            GL.Material(MaterialFace.Front, MaterialParameter.Specular, specular.ToOpenTK());
            GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            GL.Enable(EnableCap.Texture2D);

            var model = Matrix4.Identity
                * Matrix4.CreateTranslation(center.ToOpenTK())
                    * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(nx))
                    * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(ny))
                    * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(nz))
                        * Matrix4.CreateTranslation(coordinate.ToOpenTK())
                            * Matrix4.CreateScale(scalex, scaley, scalez);

            shader.SetMatrix4("model", model);
            shader.SetMatrix4("view", Camera.GetViewMatrix());
            shader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            shader.Use();

            texture.Render(TextureUnit.Texture0);
        }
        public override void SwapBuffers()
        {
            GameWindow.Context.SwapBuffers();
        }
        public override void Dispose()
        {
            base.Dispose();
            GameWindow.Dispose();
        }
    }
}
