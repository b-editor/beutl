using Beutl.Audio;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor
{
    private sealed class EvaluationServices(SceneCompositor owner) : ICompositionEvaluationServices
    {
        // Nested queries share this frame's services, while separate preview/export evaluations are isolated.
        private readonly Dictionary<(int Layer, MediaTime Time), IReadOnlyList<EngineObject.Resource>?> _queries = [];
        private const int MaxQueryDepth = 32;

        internal CompositionFrame? Frame { get; set; }

        public object SessionIdentity => owner;

        public PixelSize FrameSize => owner.Scene.FrameSize.Value;

        public Rational FrameRate => owner.Scene.FindHierarchicalParent<Project>().GetFrameRate();

        public bool TryReadFrame<T>(Func<CompositionFrame, T> read, out T result)
        {
            ArgumentNullException.ThrowIfNull(read);
            if (Frame is { } frame)
            {
                result = read(frame);
                return true;
            }
            result = default!;
            return false;
        }

        public IReadOnlyList<CompositionLayerInfo> GetLayers(MediaTime time)
        {
            using var elements = new PooledList<Element>();
            owner.SortLayers(time, elements, CompositionTarget.Graphics);
            return elements.Select(x => new CompositionLayerInfo(x.Id.Value, x.ZIndex.Value, x.Range)).ToArray();
        }

        public CompositionLayerLease EvaluateLayer(int zIndex, MediaTime time)
        {
            var key = (zIndex, time);
            if (_queries.ContainsKey(key))
                throw new InvalidOperationException($"Circular composition query at layer {zIndex}, time {time}.");
            if (_queries.Count >= MaxQueryDepth)
                throw new InvalidOperationException($"Composition queries exceeded {MaxQueryDepth} nested layers.");
            _queries.Add(key, null);
            var compositor = new SceneCompositor(owner.Scene)
            {
                DisableResourceShare = true,
                ForceOriginalSource = owner.ForceOriginalSource
            };
            void Release()
            {
                try { compositor.Dispose(); }
                finally { _queries.Remove(key); }
            }
            try
            {
                CompositionFrame frame = compositor.EvaluateGraphics(time, zIndex, this);
                _queries[key] = frame.Objects;
                return new CompositionLayerLease(frame.Objects, Release);
            }
            catch
            {
                Release();
                throw;
            }
        }

        public T ReadLayer<T>(int zIndex, MediaTime time, Func<IReadOnlyList<EngineObject.Resource>, T> read)
        {
            ArgumentNullException.ThrowIfNull(read);
            if (_queries.TryGetValue((zIndex, time), out var resources) && resources is not null)
                return read(resources);
            using var layer = EvaluateLayer(zIndex, time);
            return read(layer.Resources);
        }

        public AudioBuffer? ReadAudio(MediaTime start, MediaTime duration)
        {
            if (duration <= MediaTime.Zero) return null;
            using var composer = new SceneComposer(owner.Scene, disableResourceShare: true,
                forceOriginalSource: owner.ForceOriginalSource);
            return composer.Compose(new TimeRange(start, duration));
        }
    }
}
