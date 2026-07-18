using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
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
    public IProperty<Scene?> ReferencedScene { get; } = Property.Create<Scene?>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        if (!IsEnabled)
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

        var shiftNode = context.CreateShiftNode(OffsetPosition.CurrentValue);
        context.Connect(sourceNode, shiftNode);

        var speedNode = context.CreateSpeedNode(Speed);
        context.Connect(shiftNode, speedNode);

        // Create gain node with animation support
        var gainNode = context.CreateGainNode(Gain);
        context.Connect(speedNode, gainNode);

        AudioNode currentNode = gainNode;

        // Add effect if present
        if (Effect.CurrentValue != null && Effect.CurrentValue.IsEnabled)
        {
            currentNode = Effect.CurrentValue.CreateNode(context, currentNode);
        }

        var clipNode = context.CreateClipNode(TimeRange.Start, TimeRange.Duration);
        context.Connect(currentNode, clipNode);
        context.MarkAsOutput(clipNode);
    }

    public partial class Resource
    {
        private static readonly AsyncLocal<HashSet<Scene>?> s_evaluatingScenes = new();
        internal SceneCompositor? _compositor;
        private RenderPullPurpose _pullPurpose = RenderPullPurpose.Frame;

        internal RenderPullPurpose PullPurpose => _pullPurpose;

        public override SoundSource.Resource? GetSoundSource() => null;

        internal static bool Enter(Scene scene)
        {
            var set = s_evaluatingScenes.Value ??= new(ReferenceEqualityComparer.Instance);
            return set.Add(scene);
        }

        internal static void Exit(Scene scene)
        {
            s_evaluatingScenes.Value?.Remove(scene);
        }

        partial void PostUpdate(SceneSound obj, CompositionContext context)
        {
            bool forceOriginalSource = !context.PreferProxy;
            _pullPurpose = context.PullPurpose;
            if (_compositor?.Scene != ReferencedScene
                || _compositor?.DisableResourceShare != context.DisableResourceShare
                || _compositor?.ForceOriginalSource != forceOriginalSource
                || _compositor?.RenderIntent != context.RenderIntent)
            {
                _compositor?.Dispose();
                _compositor = null;
            }

            if (ReferencedScene != null && _compositor == null)
            {
                _compositor = new SceneCompositor(ReferencedScene, context.RenderIntent)
                {
                    DisableResourceShare = context.DisableResourceShare,
                    ForceOriginalSource = forceOriginalSource,
                };
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            SceneCompositor? compositor = _compositor;
            _compositor = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, compositor);
            ThrowIfCleanupFailed(failure);
        }
    }

    private sealed class SceneNode(Resource? resource) : AudioNode
    {
        internal Resource? _resource = resource;
        private Composer? _composer;

        public override AudioBuffer Process(AudioProcessContext context)
        {
            Resource? resource = _resource;
            var scene = resource?.ReferencedScene;
            var compositor = resource?._compositor;
            if (resource == null || scene == null || compositor == null)
            {
                return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
            }

            if (!Resource.Enter(scene))
            {
                throw new InvalidOperationException("A circular reference was detected.");
            }

            try
            {
                if (_composer?.SampleRate != context.SampleRate
                    || _composer?.RenderIntent != compositor.RenderIntent)
                {
                    _composer?.Dispose();
                    _composer = new Composer(compositor.RenderIntent) { SampleRate = context.SampleRate };
                }

                var frame = compositor.EvaluateAudio(context.TimeRange, resource.PullPurpose);
                var buffer = _composer.Compose(context.TimeRange, frame);
                if (buffer == null)
                {
                    return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
                }

                return buffer;
            }
            finally
            {
                Resource.Exit(scene);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _composer?.Dispose();
            _composer = null;
        }
    }
}
