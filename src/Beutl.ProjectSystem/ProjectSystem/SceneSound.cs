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

[Display(Name = nameof(Strings.SceneSoundReference), ResourceType = typeof(Strings))]
public sealed partial class SceneSound : Sound
{
    public SceneSound()
    {
        ScanProperties<SceneSound>();
    }

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
            if (_compositor?.Scene != ReferencedScene)
            {
                _compositor?.Dispose();
                _compositor = null;
            }

            if (ReferencedScene != null && _compositor == null)
            {
                _compositor = new SceneCompositor(ReferencedScene);
            }
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

            if (!Resource.Enter(scene))
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

                var frame = compositor.EvaluateAudio(context.TimeRange);
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
