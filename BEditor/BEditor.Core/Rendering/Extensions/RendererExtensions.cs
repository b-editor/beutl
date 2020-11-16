using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Rendering;

namespace BEditor.Rendering.Extensions
{
    public static class RendererExtensions
    {
        public static IRenderable<T> Render<T>(this IRenderable<T> renderable, Action<T> onRender, Action onCompleted = null, Action<Exception> onError = null, Action onFinally = null)
        {
            renderable.Render(new PrivateRenderer<T>()
            {
                onRender = onRender,
                onCompleted = onCompleted,
                onError = onError,
                onFinally = onFinally
            });

            return renderable;
        }
        public static IRenderable<T> Render<T>(this IRenderable<T> renderable, Action onRender, Action onCompleted = null, Action<Exception> onError = null, Action onFinally = null)
        {
            renderable.Render(new PrivateRenderer<T>()
            {
                onRender = _ => onRender?.Invoke(),
                onCompleted = onCompleted,
                onError = onError,
                onFinally = onFinally
            });

            return renderable;
        }

        private class PrivateRenderer<T> : IRenderer<T>
        {
            public Action<T> onRender;
            public Action onCompleted;
            public Action<Exception> onError;
            public Action onFinally;

            public void OnCompleted() => onCompleted?.Invoke();
            public void OnError(Exception error) => onError?.Invoke(error);
            public void OnFinally() => onFinally?.Invoke();
            public void OnRender(T value) => onRender?.Invoke(value);
        }
    }
}
