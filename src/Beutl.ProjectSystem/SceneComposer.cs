using System.Runtime.ExceptionServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer : Composer
{
    private readonly SceneCompositor _compositor;

    public SceneComposer(Scene scene, bool disableResourceShare = false)
        : this(scene, disableResourceShare, forceOriginalSource: false)
    {
    }

    public SceneComposer(Scene scene, bool disableResourceShare, bool forceOriginalSource)
        : this(scene, RenderIntent.Preview, disableResourceShare, forceOriginalSource)
    {
    }

    public SceneComposer(
        Scene scene,
        RenderIntent renderIntent,
        bool disableResourceShare = false,
        bool forceOriginalSource = false)
        : base(renderIntent)
    {
        _compositor = new SceneCompositor(scene, renderIntent)
        {
            DisableResourceShare = disableResourceShare,
            ForceOriginalSource = forceOriginalSource,
        };
    }

    public SceneCompositor Compositor => _compositor;

    public AudioBuffer? Compose(TimeRange timeRange)
    {
        var frame = _compositor.EvaluateAudio(timeRange);
        return base.Compose(timeRange, frame);
    }

    protected override void OnDispose(bool disposing)
    {
        Exception? failure = null;
        try
        {
            base.OnDispose(disposing);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (disposing)
        {
            try
            {
                _compositor.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
