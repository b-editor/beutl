using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Configuration;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer : Composer
{
    private readonly SceneCompositor _compositor;

    public SceneComposer(Scene scene, FontManager? fonts, bool disableResourceShare = false, EditorConfig? editorConfig = null, ProxyStoreConfig? proxyConfig = null, DecoderRegistry? decoders = null)
        : this(scene, fonts, disableResourceShare, forceOriginalSource: false, editorConfig, proxyConfig, decoders)
    {
    }

    public SceneComposer(Scene scene, FontManager? fonts, bool disableResourceShare, bool forceOriginalSource, EditorConfig? editorConfig = null, ProxyStoreConfig? proxyConfig = null, DecoderRegistry? decoders = null)
    {
        _compositor = new SceneCompositor(scene, fonts, editorConfig, proxyConfig, decoders)
        {
            DisableResourceShare = disableResourceShare,
            ForceOriginalSource = forceOriginalSource,
        };
    }

    public SceneCompositor Compositor => _compositor;

    public AudioBuffer? Compose(TimeRange timeRange)
    {
        MasterGain = _compositor.Scene.MasterGain;
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
