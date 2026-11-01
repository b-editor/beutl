using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.ProjectSystem;

[Display(Name = nameof(AudioStrings.SceneSound), ResourceType = typeof(AudioStrings))]
public sealed partial class SceneSound : Sound, ISplittable
{
    public SceneSound()
    {
        ScanProperties<SceneSound>();
    }

    [Display(Name = nameof(AudioStrings.SceneSound_ReferencedScene), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<Scene?> ReferencedScene { get; } = Property.CreateReference<Scene?>();

    public void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta)
    {
        if (backward) OffsetPosition.Value += startDelta;
    }

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

        var shiftNode = context.CreateShiftNode(OffsetPosition.Value);
        context.Connect(sourceNode, shiftNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(shiftNode, speedNode);

        var pannerNode = context.CreatePannerNode(Pan, SurroundPan, CenterLevel, LfeLevel, context.ChannelLayout);
        context.Connect(speedNode, pannerNode);

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(pannerNode, gainNode);

        AudioNode currentNode = gainNode;

        if (Effect.Value != null && Effect.Value.IsEnabled.Value)
        {
            currentNode = Effect.Value.CreateNode(context, currentNode);
        }

        var clipNode = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
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
        private Composer? _composer;
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
                if (_composer?.SampleRate != context.SampleRate || _composer.ChannelLayout != Layout)
                {
                    _composer?.Dispose();
                    _composer = new Composer { SampleRate = context.SampleRate, ChannelLayout = Layout };
                }

                // The embed composes the referenced scene's own window from the clip-local request.
                TimeRange window = context.TimeRange.AddStart(scene.Start.Value);
                var frame = compositor.EvaluateAudio(window);
                var buffer = _composer.Compose(window, frame);
                if (buffer == null)
                {
                    return CreateSilentFlush(context);
                }

                return buffer;
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
            if (_composer is null || _composer.ChannelLayout != Layout || _composer.SampleRate != context.SampleRate
                || scene is null || compositor is null)
                return CreateSilentFlush(context);

            if (!Resource.s_evaluatingScenes.Enter(scene))
                throw new InvalidOperationException("A circular reference was detected.");

            try
            {
                TimeRange window = context.TimeRange.AddStart(scene.Start.Value);
                CompositionFrame frame = compositor.EvaluateAudio(window);
                return _composer.Flush(window, frame.Eligibility)
                    ?? CreateSilentFlush(context);
            }
            finally
            {
                Resource.s_evaluatingScenes.Exit(scene);
            }
        }

        public override int GetTotalLatencySamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            return _composer?.GetTotalLatencySamples(sampleRate) ?? 0;
        }

        public override int GetDrainLatencySamples(int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            return _composer?.GetDrainLatencySamples(sampleRate) ?? 0;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _composer?.Dispose();
            _composer = null;
        }
    }
}
