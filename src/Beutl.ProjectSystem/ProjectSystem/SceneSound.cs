using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.ProjectSystem;

[Display(Name = nameof(AudioStrings.SceneSound), ResourceType = typeof(AudioStrings))]
public sealed partial class SceneSound : Sound, ITimeMappedSource
{
    public SceneSound()
    {
        ScanProperties<SceneSound>();
    }

    [Display(Name = nameof(AudioStrings.SceneSound_ReferencedScene), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<Scene?> ReferencedScene { get; } = Property.CreateReference<Scene?>();

    bool ITimeMappedSource.HasDynamicTimeline => SceneReference.IsDynamic(ReferencedScene);

    MediaTime? ITimeMappedSource.GetSourceDuration(DecoderRegistry? decoders) => SceneReference.GetDuration(this, ReferencedScene);

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        if (!IsEnabled.Value)
        {
            context.Clear();
            return;
        }

        var r = (Resource)resource;
        if (r.ReferencedScene == null)
        {
            context.Clear();
            AudioReadScope.AddUnavailableSource(context, TimeRange.Value);
            return;
        }

        var sourceNode = context.CreateNode(
            parameters: r,
            factory: r => new SceneNode(r) { Layout = context.ChannelLayout },
            updater: (r, node) =>
            {
                node._resource = r;
                node.Layout = context.ChannelLayout;
            },
            comparer: static (r, node) => node._resource == r
        );

        var shiftNode = context.CreateShiftNode(OffsetPosition);
        context.Connect(sourceNode, shiftNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(shiftNode, speedNode);

        ComposeOutputStages(context, speedNode, context.ChannelLayout);
    }

    public partial class Resource
    {
        internal static readonly SceneEvaluationGuard s_evaluatingScenes = new();
        internal SceneCompositor? _compositor;

        public override SoundSource.Resource? GetSoundSource() => null;

        partial void PostReconcile(SceneSound obj, CompositionContext context)
        {
            SceneCompositor.Refresh(ref _compositor, ReferencedScene, context);
        }

        partial void PostDispose(bool disposing)
        {
            _compositor?.Dispose();
            _compositor = null;
        }
    }

    private sealed class SceneNode(Resource? resource) : AudioNode
    {
        internal Resource? _resource = resource;
        private SceneComposer? _composer;
        internal AudioChannelLayout Layout { get; set; }

        protected override AudioBuffer CreateSilentFlush(AudioProcessContext context)
            => new(context.SampleRate, Layout.ChannelCount, context.GetSampleCount());

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var scene = _resource?.ReferencedScene;
            var compositor = _resource?._compositor;
            if (scene == null || compositor == null)
            {
                AudioReadScope.ReportFailure();
                return CreateSilentFlush(context);
            }

            if (!Resource.s_evaluatingScenes.Enter(scene))
            {
                throw new InvalidOperationException("A circular reference was detected.");
            }

            try
            {
                if (_composer is not { } composer || composer.Compositor != compositor
                    || composer.SampleRate != context.SampleRate || composer.ChannelLayout != Layout)
                {
                    _composer?.Dispose();
                    _composer = composer = new SceneComposer(compositor)
                    {
                        SampleRate = context.SampleRate,
                        ChannelLayout = Layout,
                    };
                }

                // Keep the compensated mix on the referenced-scene timeline. At a trim, read
                // enough held samples to finish that mix without taking live input past the cut.
                MediaTime start = scene.Start.Value;
                MediaTime duration = scene.Duration.Value;
                if (context.ProcessEndTime is { } terminal && terminal < duration)
                    duration = terminal;
                TimeRange window = context.TimeRange.AddStart(start);
                return composer.ComposePlayback(window, start + duration) ?? CreateSilentFlush(context);
            }
            finally
            {
                Resource.s_evaluatingScenes.Exit(scene);
            }
        }

        public override AudioBuffer Flush(AudioProcessContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var scene = _resource?.ReferencedScene;
            var compositor = _resource?._compositor;
            if (_composer is not { HasCompensatedPlayback: false } composer || composer.Compositor != compositor
                || composer.ChannelLayout != Layout || composer.SampleRate != context.SampleRate
                || scene is null || compositor is null)
                return CreateSilentFlush(context);

            if (!Resource.s_evaluatingScenes.Enter(scene))
                throw new InvalidOperationException("A circular reference was detected.");

            try
            {
                TimeRange window = context.TimeRange.AddStart(scene.Start.Value);
                CompositionFrame frame = compositor.EvaluateAudio(window);
                composer.MasterGain = scene.MasterGain;
                return composer.Flush(window, frame.Eligibility)
                    ?? CreateSilentFlush(context);
            }
            finally
            {
                Resource.s_evaluatingScenes.Exit(scene);
            }
        }

        // An embed can still contain outputs with independent, varying latencies.
        internal override bool HasFixedLatency(int sampleRate) => false;

        public override int GetTotalLatencySamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            return _composer is { HasCompensatedPlayback: false } composer ? composer.GetTotalLatencySamples(sampleRate) : 0;
        }

        public override int GetDrainLatencySamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            return _composer is { HasCompensatedPlayback: false } composer ? composer.GetDrainLatencySamples(sampleRate) : 0;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _composer?.Dispose();
            _composer = null;
        }
    }
}
