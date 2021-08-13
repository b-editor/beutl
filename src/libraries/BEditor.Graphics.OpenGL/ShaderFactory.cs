using System.IO;
using System.Reflection;

namespace BEditor.Graphics.OpenGL
{
    public class ShaderFactory
    {
        public static readonly ShaderFactory Texture = new TextureShaderFactory();

        public static readonly ShaderFactory Lighting = new LightingShaderFactory();

        public static readonly ShaderFactory TextureLighting = new LightingTextureShaderFactory();

        public static readonly ShaderFactory Line = new LineShaderFactory();

        public static readonly ShaderFactory Default = new();

        private string? Fragment;

        private string? Vertex;

        public virtual Shader Create()
        {
            if (Fragment == null)
            {
                Fragment = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.shader.frag");
            }

            if (Vertex == null)
            {
                Vertex = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.shader.vert");
            }

            return new(Vertex, Fragment);
        }

        protected static string ReadEmbeddedFile(string file)
        {
            var asm = Assembly.GetCallingAssembly();
            using var stream = asm.GetManifestResourceStream(file)!;
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
    }
}