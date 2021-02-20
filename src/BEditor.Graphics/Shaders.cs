using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Graphics
{
    internal static class Shaders
    {
        public const string TextureFrag =
            "#version 330\n" +

            "out vec4 outputColor;\n" +

            "in vec2 texCoord;\n" +

            "uniform vec4 color;\n" +
            "uniform sampler2D texture;\n" +

            "void main()\n" +
            "{\n" +
            "   outputColor = texture(texture, texCoord) * color;\n" +
            "}";
        public const string TextureVert =
            "#version 330 core\n" +

            "layout(location = 0) in vec3 aPosition;\n" +
            "layout(location = 1) in vec2 aTexCoord;\n" +

            "out vec2 texCoord;\n" +
            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "void main(void)\n" +
            "{\n" +
            "   texCoord = aTexCoord;\n" +
            "   gl_Position = vec4(aPosition, 1.0) * model * view * projection;\n" +
            "}";

        public const string Flag =
            "#version 330 core\n" +
            "out vec4 FragColor;\n" +

            "uniform vec4 color;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = color;\n"+
            "}";

        public const string Vert =
            "#version 330 core\n" +
            "layout (location = 0) in vec3 aPos;\n" +

            "uniform mat4 model;\n" +
            "uniform mat4 view;\n" +
            "uniform mat4 projection;\n" +

            "void main()\n" +
            "{\n" +
            "   gl_Position = vec4(aPos, 1.0) * model * view * projection;\n" +
            "}";

        public const string LightFlag =
            "#version 330 core\n" +
            "out vec4 FragColor;\n" +

            "uniform vec4 objectColor;\n" +
            "uniform vec4 lightColor;\n" +

            "void main()\n" +
            "{\n" +
            "   FragColor = lightColor * objectColor;\n" +
            "}";
    }
}
