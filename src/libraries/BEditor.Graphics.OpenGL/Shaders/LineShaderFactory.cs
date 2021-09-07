// LineShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics.OpenGL
{
    internal sealed class LineShaderFactory : ShaderFactory
    {
        private string? Vertex;

        private string? Fragment;

        public override Shader Create()
        {
            if (Fragment == null)
            {
                Fragment = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.line.frag");
            }

            if (Vertex == null)
            {
                Vertex = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.line.vert");
            }

            return new(Vertex, Fragment);
        }
    }
}