// TextureShaderFactory.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Graphics.OpenGL
{
    internal sealed class TextureShaderFactory : ShaderFactory
    {
        private string? Fragment;

        private string? Vertex;

        public override Shader Create()
        {
            if (Fragment == null)
            {
                Fragment = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.texture.frag");
            }

            if (Vertex == null)
            {
                Vertex = ReadEmbeddedFile("BEditor.Graphics.OpenGL.Resources.texture.vert");
            }

            return new(Vertex, Fragment);
        }
    }
}