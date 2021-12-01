using System.Numerics;
using BEditorNext.Graphics;
using BEditorNext.ProjectItems;

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
            var framerate = (_scene.Parent as Project)?.FrameRate ?? 30;
            var curTp = ToTimeSpan(FrameNumber, framerate);
            var args = new RenderTaskExecuteArgs(curTp, this, _renderables);

            foreach (var item in _scene.Clips)
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

    private void ProcessClip(Clip clip, in RenderTaskExecuteArgs args)
    {
        _renderables.Clear();
        var list = clip.Children;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is RenderTask task && task.IsEnabled)
            {
                UpdateProperty(task, args.CurrentTime);
                task.Execute(args);
            }
        }


        for (int i = 0; i < _renderables.Count; i++)
        {
            var renderable = _renderables[i];
            if (!renderable.IsDisposed)
            {
                renderable.Render(this);
                renderable.Dispose();
            }
        }

        _renderables.Clear();
    }

    private static void UpdateProperty(RenderTask task, TimeSpan timeSpan)
    {
        var length = task.Setters.Count;
        for (int i = 0; i < length; i++)
        {
            var item = task.Setters[i];

            if (item is IAnimatableSetter anmSetter)
            {
                anmSetter.SetProperty(task, timeSpan);
            }
            else
            {
                item.SetProperty(task);
            }
        }
    }

    private static int ToFrameNumber(TimeSpan tp, int rate)
    {
        return (int)(tp.TotalSeconds * rate);
    }

    private static int TicksPerFrame(int rate)
    {
        return 10000000 / rate;
    }

    private static TimeSpan ToTimeSpan(int f, int rate)
    {
        return TimeSpan.FromSeconds(f / (double)rate);
    }
}
