using System;

namespace BEditor.Core.Renderer {
    public interface IBaseRenderingContext : IDisposable {
        int Height { get; }
        bool IsInitialized { get; }
        int Width { get; }

        void Clear(bool Perspective = false, float x = 0, float y = 0, float z = 1024, float tx = 0, float ty = 0, float tz = 0, float near = 0.1F, float far = 20000);
        void MakeCurrent();
        void Resize(int width, int height, bool Perspective = false, float x = 0, float y = 0, float z = 1024, float tx = 0, float ty = 0, float tz = 0, float near = 0.1F, float far = 20000);
        void SwapBuffers();
        void UnMakeCurrent();
    }
}