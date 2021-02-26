using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

namespace BEditor.Graphics
{
    public class Light
    {
        public Light(Vector3 pos, Color ambient, Color diffuse, Color specular)
        {
            Position = pos;
            Ambient = ambient;
            Diffuse = diffuse;
            Specular = specular;
        }

        public Color Ambient { get; set; }
        public Color Diffuse { get; set; }
        public Color Specular { get; set; }
        public Vector3 Position { get; set; }
    }
}
