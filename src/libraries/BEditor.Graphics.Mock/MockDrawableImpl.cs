using System;

using BEditor.Drawing;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Mock
{
    public class MockDrawableImpl : IDrawableImpl
    {
        public Color Color { get; set; }

        public BlendMode BlendMode { get; set; }

        public Material Material { get; set; }

        public Transform Transform { get; set; }

        public bool IsDisposed { get; }

        public RasterizerState RasterizerState { get; set; }

        public DepthStencilState DepthStencilState { get; set; }

        public void Dispose()
        {
        }
    }
}
