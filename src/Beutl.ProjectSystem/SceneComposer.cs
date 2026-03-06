using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer(Scene scene) : Composer
{
    private readonly SceneCompositor _compositor = new(scene);

    public SceneCompositor Compositor => _compositor;

    public AudioBuffer? Compose(TimeRange timeRange)
    {
        var frame = _compositor.EvaluateAudio(timeRange);
        return base.Compose(timeRange, frame);
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (disposing)
            _compositor.Dispose();
    }
}
