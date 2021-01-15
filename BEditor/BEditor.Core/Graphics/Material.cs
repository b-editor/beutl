using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Core.Graphics
{
    public struct Material
    {
        public Material(Color ambient, Color diffuse, Color specular, float shininess)
        {
            Ambient = ambient;
            Diffuse = diffuse;
            Specular = specular;
            Shininess = shininess;
        }

        public Color Ambient { get; }
        public Color Diffuse { get; }
        public Color Specular { get; }
        public float Shininess { get; }
    }
}
