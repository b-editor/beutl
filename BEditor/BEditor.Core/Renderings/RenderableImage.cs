using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;

namespace BEditor.Core.Renderings
{
    public struct RenderableImage : IRenderable<Image>
    {
        public Image Source { get; init; }

        public void Dispose() => Source.Dispose();
        public IRenderable<Image> Render(IRenderer<Image> renderer)
        {
            try
            {
                renderer.OnRender(Source);
                renderer.OnCompleted();
            }
            catch (Exception e)
            {
                renderer.OnError(e);
            }
            finally
            {
                renderer.OnFinally();
            }

            return this;
        }

        public static implicit operator RenderableImage(Image image) => new() { Source = image };
        public static implicit operator Image(RenderableImage image) => image.Source;
    }
}
