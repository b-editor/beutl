using BEditor.Core.Graphics;

namespace BEditor.Core.Service
{
    public interface IGraphicsContextService
    {
        BaseGraphicsContext CreateContext(int width, int height);
    }
}