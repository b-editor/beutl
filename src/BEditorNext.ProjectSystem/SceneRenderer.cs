using BEditorNext.Collections;
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext;

internal class SceneRenderer : IRenderer
{
    private readonly Scene _scene;
    private readonly RenderableList _renderables = new();

    public SceneRenderer(Scene scene, int width, int height)
    {
        _scene = scene;
        Graphics = new Graphics.Graphics(width, height);
    }

    public IGraphics Graphics { get; }

    public int FrameNumber => _scene.CurrentFrame;

    public bool IsDisposed { get; private set; }

    public bool IsRendering { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;

        Graphics?.Dispose();

        IsDisposed = true;
    }

    public IRenderer.RenderResult Render()
    {
        if (!IsRendering)
        {
            int framerate = (_scene.Parent as Project)?.FrameRate ?? 30;
            TimeSpan curTp = ToTimeSpan(FrameNumber, framerate);
            var args = new OperationRenderArgs(curTp, this, _renderables);

            foreach (SceneLayer item in _scene.Layers)
            {
                if (item.Start <= curTp &&
                    curTp < item.Length + item.Start)
                {
                    ProcessClip(item, args);
                }
            }
        }

        return new IRenderer.RenderResult(Graphics.GetBitmap());
    }

    private void ProcessClip(SceneLayer layer, in OperationRenderArgs args)
    {
        _renderables.Clear();
        IElementList list = layer.Children;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is RenderOperation op && op.IsEnabled)
            {
                UpdateProperty(op, args.CurrentTime);
                op.Render(args);
            }
        }


        for (int i = 0; i < _renderables.Count; i++)
        {
            IRenderable renderable = _renderables[i];
            if (!renderable.IsDisposed)
            {
                renderable.Render(this);
                renderable.Dispose();
            }
        }

        _renderables.Clear();
    }

    private static void UpdateProperty(RenderOperation op, TimeSpan timeSpan)
    {
        int length = op.Setters.Count;
        for (int i = 0; i < length; i++)
        {
            ISetter item = op.Setters[i];

            if (item is IAnimatableSetter anmSetter)
            {
                anmSetter.SetProperty(op, timeSpan);
            }
            else
            {
                item.SetProperty(op);
            }
        }
    }

    //private static int ToFrameNumber(TimeSpan tp, int rate)
    //{
    //    return (int)(tp.TotalSeconds * rate);
    //}

    //private static int TicksPerFrame(int rate)
    //{
    //    return 10000000 / rate;
    //}

    private static TimeSpan ToTimeSpan(int f, int rate)
    {
        return TimeSpan.FromSeconds(f / (double)rate);
    }
}
