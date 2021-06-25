namespace BEditor.Graphics.OpenGL
{
    public class ShaderFactory
    {
        public static readonly ShaderFactory Texture = new TextureShaderFactory();

        public static readonly ShaderFactory Lighting = new LightingShaderFactory();

        public static readonly ShaderFactory TextureLighting = new LightingTextureShaderFactory();

        public static readonly ShaderFactory Line = new LineShaderFactory();

        public static readonly ShaderFactory Default = new();

        internal const string Frag =
            "#version 330 core\n" +
            "out vec4 FragColor;\n" +

            "uniform vec4 color;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = color;\n" +
            "}";

        internal const string Vert =
            "#version 330 core\n" +
            "layout (location = 0) in vec3 aPos;\n" +

            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "void main()\n" +
            "{\n" +
            "   gl_Position = vec4(aPos, 1.0) * model * view * projection;\n" +
            "}";

        public virtual Shader Create()
        {
            return new(Vert, Frag);
        }
    }
}