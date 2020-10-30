using System;
using System.Collections.Generic;
using System.Text;

using BEditor.NET.SDL2;

namespace BEditor.NET.Renderer {
    public class RenderingContext : BaseRenderingContext {
        public override void MakeCurrent() {
            if (SDL.SDL_GL_GetCurrentContext() != context) {
                SDL.SDL_GL_MakeCurrent(handle, context);
            }
        }
        public override void SwapBuffers() => SDL.SDL_GL_SwapWindow(handle);

        private IntPtr context;
        private IntPtr handle;

        public RenderingContext(int width, int height) : base(width, height) {
            handle = SDL.SDL_CreateWindow("", 1, 1, width, height, SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL);
            context = SDL.SDL_GL_CreateContext(handle);

            if (context == IntPtr.Zero) {
                throw new Exception(SDL.SDL_GetError());
            }

            Initialize();
        }

        ~RenderingContext() {
            SDL.SDL_GL_DeleteContext(context);
        }
    }
}
