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
public sealed partial class SceneSound : Sound
{
    public SceneSound()
    {
        ScanProperties<SceneSound>();
    }

    [Display(Name = nameof(AudioStrings.SceneSound_ReferencedScene), ResourceType = typeof(AudioStrings))]
    public IExpressionProperty<Scene?> ReferencedScene { get; } = Property.CreateReference<Scene?>();

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
            return;
        }

        var sourceNode = context.CreateNode(
            parameters: r,
            factory: static r => new SceneNode(r),
            updater: static (r, node) => node._resource = r,
            comparer: static (r, node) => node._resource == r
        );

        var shiftNode = context.CreateShiftNode(OffsetPosition.Value);
        context.Connect(sourceNode, shiftNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(shiftNode, speedNode);

        var gainNode = context.CreateGainNode(Gain);
        context.Connect(speedNode, gainNode);

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

        public override AudioBuffer Process(AudioProcessContext context)
        {
            var scene = _resource?.ReferencedScene;
            var compositor = _resource?._compositor;
            if (scene == null || compositor == null)
            {
                return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
            }

            if (!Resource.s_evaluatingScenes.Enter(scene))
            {
                throw new InvalidOperationException("A circular reference was detected.");
            }

            try
            {
                if (_composer?.SampleRate != context.SampleRate)
                {
                    _composer?.Dispose();
                    _composer = new Composer { SampleRate = context.SampleRate };
                }

                // The embed plays the referenced scene's own window from the clip-local request,
                // matching the standalone sample provider that composes FromWindowTime ranges.
                // Without the shift a non-zero scene Start would offset the embed's audio
                // against its video and the standalone view (#3603).
                TimeRange window = context.TimeRange.AddStart(scene.Start.Value);
                var frame = compositor.EvaluateAudio(window);
                var buffer = _composer.Compose(window, frame);
                if (buffer == null)
                {
                    return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
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
            if (_composer is null || scene is null || compositor is null)
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
